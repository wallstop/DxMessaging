// The Unity Editor assembly that hosts this file does not enable nullable annotations; the
// dotnet-test project that compiles a linked copy DOES (`<Nullable>enable</Nullable>`). Pin the
// nullable state per-file so behavior is identical in both compilation contexts.
#nullable disable
namespace DxMessaging.Editor.Analyzers
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;

    /// <summary>
    /// Pure (Unity-API-free) classification core for the IL-reflection scanner. Takes a
    /// pre-supplied set of candidate <see cref="MessageAwareComponent"/>-derived types and
    /// produces the per-FQN snapshot that the inspector overlay consumes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Extracted from <see cref="BaseCallTypeScanner"/> so the dotnet-test project can cover the
    /// classification logic (chain walk, opt-out paths, master-toggle gating, FQN normalisation,
    /// abstract / generic-definition skipping) without depending on Unity's <c>TypeCache</c> API.
    /// The Unity-only wrapper <see cref="BaseCallTypeScanner.Scan"/> simply forwards
    /// <c>TypeCache.GetTypesDerivedFrom&lt;MessageAwareComponent&gt;()</c> as the candidate set
    /// and reads the project's ignore list off <c>DxMessagingSettings</c>.
    /// </para>
    /// <para>
    /// All inputs are pure-BCL: tests compile small Roslyn fixtures, load the resulting
    /// assemblies, enumerate <c>assembly.GetTypes().Where(t =&gt; ...)</c> as the candidate set,
    /// and assert against the returned snapshot.
    /// </para>
    /// <para>
    /// Diagnostic IDs produced match what the inspector overlay reads from the Unity-facing entry:
    /// <c>DXMSG006</c> (override missing base call), <c>DXMSG007</c> (hides via <c>new</c>; also
    /// covers DXMSG009 since IL alone can't distinguish the two — see remarks on
    /// <see cref="BaseCallIlInspector"/>), and <c>DXMSG010</c> (override calls base but a chain
    /// link does not).
    /// </para>
    /// </remarks>
    public static class BaseCallTypeScannerCore
    {
        /// <summary>
        /// The five guarded lifecycle methods on <c>MessageAwareComponent</c>. Method names are
        /// matched ordinally; only zero-parameter, void-returning, instance methods are
        /// considered.
        /// </summary>
        public static readonly string[] GuardedMethodNames =
        {
            "Awake",
            "OnEnable",
            "OnDisable",
            "OnDestroy",
            "RegisterMessageHandlers",
        };

        private const string IgnoreAttributeFullName =
            "DxMessaging.Core.Attributes.DxIgnoreMissingBaseCallAttribute";

        /// <summary>
        /// Result row produced by <see cref="Scan"/>. Mirrors the Unity-facing
        /// <c>BaseCallReportEntry</c> shape but uses pure BCL collections so the helper is
        /// callable from <c>dotnet test</c>.
        /// </summary>
        public sealed class ScanEntry
        {
            /// <summary>Fully-qualified name of the offending type (dot-form for nested types).</summary>
            public string TypeName;

            /// <summary>Method names whose overrides are missing the corresponding <c>base.*()</c> call.</summary>
            public SortedSet<string> MissingBaseFor = new(StringComparer.Ordinal);

            /// <summary>Diagnostic IDs that contributed to this entry (DXMSG006 / DXMSG007 / DXMSG010).</summary>
            public HashSet<string> DiagnosticIds = new(StringComparer.Ordinal);
        }

        /// <summary>
        /// Classify every <paramref name="candidates"/> type and return a per-FQN snapshot keyed
        /// by fully-qualified type name (dot-form for nested types). Types opted out via
        /// <c>[DxIgnoreMissingBaseCall]</c> or via <paramref name="ignoredTypeNames"/> are
        /// intentionally NOT included in the returned dictionary — the inspector overlay reads
        /// the project ignore list directly to render its "Stop ignoring" HelpBox, and the
        /// snapshot semantics here match the bridge path (DXMSG008-equivalent rows were never
        /// present in the snapshot's <c>missingBaseFor</c> either).
        /// </summary>
        /// <param name="candidates">
        /// Strict subclasses of <c>MessageAwareComponent</c>. Abstract types and generic-type
        /// definitions are skipped; the <c>MessageAwareComponent</c> type itself is also skipped.
        /// May contain <c>null</c> entries (defensively skipped).
        /// </param>
        /// <param name="ignoredTypeNames">
        /// Project-level ignore list (typically the parsed contents of
        /// <c>Assets/Editor/DxMessaging.BaseCallIgnore.txt</c>, surfaced through
        /// <c>DxMessagingSettings._baseCallIgnoredTypes</c>). May be <c>null</c>.
        /// </param>
        public static Dictionary<string, ScanEntry> Scan(
            IEnumerable<Type> candidates,
            IEnumerable<string> ignoredTypeNames
        )
        {
            Dictionary<string, ScanEntry> result = new(StringComparer.Ordinal);
            if (candidates is null)
            {
                return result;
            }

            HashSet<string> projectIgnore = ignoredTypeNames is null
                ? new HashSet<string>(StringComparer.Ordinal)
                : new HashSet<string>(ignoredTypeNames, StringComparer.Ordinal);

            foreach (Type concrete in candidates)
            {
                if (concrete == null)
                {
                    continue;
                }
                if (concrete.IsAbstract)
                {
                    continue;
                }
                if (concrete.IsGenericTypeDefinition)
                {
                    continue;
                }

                string fullName = concrete.FullName ?? string.Empty;
                if (string.IsNullOrEmpty(fullName))
                {
                    continue;
                }
                // FullName for nested types uses '+'; the analyzer (and the inspector overlay's
                // lookup) emits the dotted form. Normalise here so the scanner-produced snapshot
                // is keyed identically to the analyzer's identifiers.
                fullName = fullName.Replace('+', '.');

                bool optedOutByAttribute = TypeOrAncestorHasIgnoreAttribute(concrete);
                bool optedOutByList = projectIgnore.Contains(fullName);

                ScanEntry entry = ScanOne(concrete, fullName);
                if (entry == null || entry.MissingBaseFor.Count == 0)
                {
                    continue;
                }

                if (optedOutByAttribute || optedOutByList)
                {
                    // Suppression makes the entry an audit-marker (DXMSG008-equivalent). The
                    // overlay's "ignored" branch handles this via the ignored-types list directly,
                    // so we don't add it to the snapshot at all — the overlay reads the project
                    // list to render the "Stop ignoring" HelpBox. This matches the bridge path's
                    // snapshot semantics (DXMSG008 was never in MissingBaseFor either).
                    continue;
                }

                result[fullName] = entry;
            }

            return result;
        }

        private static bool TypeOrAncestorHasIgnoreAttribute(Type type)
        {
            // [DxIgnoreMissingBaseCall] applies with Inherited=false (matches the analyzer's
            // attribute declaration), so we only walk the type itself plus its declared methods.
            foreach (object attr in type.GetCustomAttributes(inherit: false))
            {
                if (attr.GetType().FullName == IgnoreAttributeFullName)
                {
                    return true;
                }
            }
            // Method-level: any of the five guarded methods marked with the attribute also opts
            // the entire type out from the inspector overlay (the analyzer applies the attribute
            // per-method, but the overlay tracks types — opt out at the granularity we render).
            foreach (string methodName in GuardedMethodNames)
            {
                MethodInfo m = type.GetMethod(
                    methodName,
                    BindingFlags.Public
                        | BindingFlags.NonPublic
                        | BindingFlags.Instance
                        | BindingFlags.DeclaredOnly,
                    null,
                    Type.EmptyTypes,
                    null
                );
                if (m == null)
                {
                    continue;
                }
                foreach (object attr in m.GetCustomAttributes(inherit: false))
                {
                    if (attr.GetType().FullName == IgnoreAttributeFullName)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static ScanEntry ScanOne(Type concrete, string fullName)
        {
            ScanEntry entry = new()
            {
                TypeName = fullName,
                MissingBaseFor = new SortedSet<string>(StringComparer.Ordinal),
                DiagnosticIds = new HashSet<string>(StringComparer.Ordinal),
            };

            foreach (string methodName in GuardedMethodNames)
            {
                ClassifyMethod(concrete, methodName, entry);
            }

            return entry;
        }

        private static void ClassifyMethod(Type concrete, string methodName, ScanEntry entry)
        {
            // Walk the type chain: first the leaf (concrete), then ancestors via BaseType until we
            // leave the MessageAwareComponent inheritance subtree. For the leaf we determine which
            // of DXMSG006/007/009 fires (if any). If the leaf overrides correctly, we walk
            // ancestor links to detect DXMSG010 (a broken intermediate). Each link's diagnosis is
            // independent; we only record the FIRST classification for the leaf in
            // entry.MissingBaseFor since the overlay HelpBox shows one row per method per type.

            MethodInfo declared = GetDeclaredZeroArgInstance(concrete, methodName);
            if (declared == null)
            {
                // Type does not declare this method at all — nothing to flag at this level.
                return;
            }
            if (declared.ReturnType != typeof(void))
            {
                return;
            }
            if (declared.IsStatic)
            {
                return;
            }
            if (declared.IsGenericMethodDefinition)
            {
                return;
            }

            // DXMSG009 vs DXMSG007: declares without override (or with `new`); hides the base.
            // In IL/reflection terms: the method does NOT have the override slot binding
            // (GetBaseDefinition() returns the method itself) AND the base type has a same-named
            // virtual we are hiding. The C# compiler emits the same IL for `new void X()` and
            // `void X()`-with-CS0114, so we cannot perfectly distinguish DXMSG007 from DXMSG009
            // from IL alone. The compile-time analyzer is authoritative for the precise ID;
            // here we conservatively classify the case as DXMSG007 — both produce the same
            // overlay outcome (method listed in HelpBox).
            bool isOverride = declared.GetBaseDefinition() != declared;
            bool hasNewKeyword =
                !isOverride && BaseHasSameNamedVirtual(concrete.BaseType, methodName);

            if (!isOverride)
            {
                if (hasNewKeyword)
                {
                    AddIfMissing(entry, methodName, "DXMSG007");
                }
                // else: not an override, no base virtual to hide — not our concern.
                return;
            }

            // It IS an override. Check IL for base call.
            bool callsBase = BaseCallIlInspector.MethodIlContainsBaseCall(declared, methodName);
            if (!callsBase)
            {
                AddIfMissing(entry, methodName, "DXMSG006");
                return;
            }

            // Leaf calls base. Walk the inheritance chain to look for a broken intermediate
            // (DXMSG010). Each link's IL is inspected independently; the first broken link found
            // produces DXMSG010 on the leaf and we stop. Cross-assembly ancestors with no IL body
            // are trusted (assume-clean) — the alternative would be unactionable warnings against
            // closed-source code.
            MethodInfo cursorOverridden = GetOverriddenMethod(declared);
            HashSet<MethodInfo> visited = new();
            while (cursorOverridden != null && visited.Add(cursorOverridden))
            {
                // Chain reached MessageAwareComponent itself — clean. We compare by full type
                // name so the helper does not need a hard reference to the Unity-only type.
                Type cursorDeclaring = cursorOverridden.DeclaringType;
                if (
                    cursorDeclaring != null
                    && cursorDeclaring.FullName == "DxMessaging.Unity.MessageAwareComponent"
                )
                {
                    return;
                }
                if (cursorOverridden.GetMethodBody() == null)
                {
                    // Cross-assembly / abstract — assume clean (cannot inspect).
                    return;
                }
                bool ancestorCallsBase = BaseCallIlInspector.MethodIlContainsBaseCall(
                    cursorOverridden,
                    methodName
                );
                if (!ancestorCallsBase)
                {
                    AddIfMissing(entry, methodName, "DXMSG010");
                    return;
                }
                cursorOverridden = GetOverriddenMethod(cursorOverridden);
            }
        }

        private static MethodInfo GetDeclaredZeroArgInstance(Type type, string methodName)
        {
            return type.GetMethod(
                methodName,
                BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.Instance
                    | BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null
            );
        }

        private static bool BaseHasSameNamedVirtual(Type baseType, string methodName)
        {
            while (baseType != null && baseType != typeof(object))
            {
                MethodInfo m = baseType.GetMethod(
                    methodName,
                    BindingFlags.Public
                        | BindingFlags.NonPublic
                        | BindingFlags.Instance
                        | BindingFlags.DeclaredOnly,
                    null,
                    Type.EmptyTypes,
                    null
                );
                if (m != null && (m.IsVirtual || m.IsAbstract))
                {
                    return true;
                }
                baseType = baseType.BaseType;
            }
            return false;
        }

        private static MethodInfo GetOverriddenMethod(MethodInfo derivedOverride)
        {
            // For an override, GetBaseDefinition() returns the most-base virtual (the originating
            // declaration). To walk the chain link-by-link we need the closest ancestor that
            // declares the same-named method directly — we look up each BaseType in turn and
            // return the first match. This skips intermediate types that don't override the slot
            // (e.g. a generic intermediate that just passes through), which is exactly what the
            // chain walk needs to detect DXMSG010 at the broken link rather than the pass-through.
            Type baseType = derivedOverride.DeclaringType?.BaseType;
            while (baseType != null && baseType != typeof(object))
            {
                MethodInfo m = baseType.GetMethod(
                    derivedOverride.Name,
                    BindingFlags.Public
                        | BindingFlags.NonPublic
                        | BindingFlags.Instance
                        | BindingFlags.DeclaredOnly,
                    null,
                    Type.EmptyTypes,
                    null
                );
                if (m != null)
                {
                    return m;
                }
                baseType = baseType.BaseType;
            }
            return null;
        }

        private static void AddIfMissing(ScanEntry entry, string methodName, string diagnosticId)
        {
            entry.MissingBaseFor.Add(methodName);
            entry.DiagnosticIds.Add(diagnosticId);
        }
    }
}
