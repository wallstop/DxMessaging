#if UNITY_2021_3_OR_NEWER
namespace DxMessaging.Tests.Runtime.Core
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Text;
    using DxMessaging.Core;
    using DxMessaging.Core.MessageBus;
    using DxMessaging.Tests.Runtime;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;

    /// <summary>
    /// Pins invariants on the DxMessaging public surface. The fixture is
    /// intentionally read-only (it does NOT touch the bus during the
    /// tests), so it can run in the default suite without a noticeable
    /// wall-clock cost.
    /// </summary>
    public sealed class PublicSurfaceContractTests
    {
        private const string SnapshotResourceFolderName = "Snapshots";
        private const string PublicSurfaceSnapshotFileName = "public-surface.txt";

        private const string CoreNamespacePrefix = "DxMessaging.Core";

        /// <summary>
        /// Enumerates every <c>public</c> type in the
        /// <c>DxMessaging.Core</c> namespace (and its sub-namespaces) and
        /// compares the sorted list to a stored snapshot. The snapshot file
        /// is generated on first run if missing; subsequent runs fail when
        /// the type list drifts. The diff format is one fully-qualified
        /// name per line so a failing test points directly at the
        /// added/removed type.
        /// </summary>
        [Test]
        public void PublicTypeSetInDxMessagingCoreNamespaceMatchesSnapshot()
        {
            List<string> live = EnumeratePublicCoreTypeNames();
            string liveSnapshot = string.Join("\n", live);

            string snapshotPath = TryResolveSnapshotPath(out bool snapshotPathResolved);

            if (!snapshotPathResolved)
            {
                Assert.Inconclusive(
                    "Could not resolve the public-surface snapshot path. The Tests/Runtime/Core/Snapshots "
                        + "directory must exist in the package source tree (it is excluded from build "
                        + "but not from the editor's reflection of test assemblies). Skipping the diff check."
                );
                return;
            }

            if (!File.Exists(snapshotPath))
            {
                // Auto-generate the snapshot for the developer's convenience, then
                // FAIL the test so the snapshot is not silently rubber-stamped.
                File.WriteAllText(snapshotPath, liveSnapshot, new UTF8Encoding(false));
                Assert.Fail(
                    "Public surface snapshot was missing and has been auto-generated at "
                        + $"{snapshotPath}. Run the test once locally, review the auto-generated "
                        + "snapshot (and its .meta sibling), and commit it. The test will only "
                        + "pass with a committed snapshot."
                );
                return;
            }

            string stored = File.ReadAllText(snapshotPath).Replace("\r\n", "\n").TrimEnd();
            string actual = liveSnapshot.TrimEnd();

            if (string.Equals(stored, actual, StringComparison.Ordinal))
            {
                return;
            }

            HashSet<string> storedSet = new HashSet<string>(
                stored.Split('\n', StringSplitOptions.RemoveEmptyEntries),
                StringComparer.Ordinal
            );
            HashSet<string> actualSet = new HashSet<string>(
                actual.Split('\n', StringSplitOptions.RemoveEmptyEntries),
                StringComparer.Ordinal
            );

            List<string> added = actualSet
                .Except(storedSet)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();
            List<string> removed = storedSet
                .Except(actualSet)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

            string addedTxt = added.Count == 0 ? "(none)" : string.Join("\n  ", added);
            string removedTxt = removed.Count == 0 ? "(none)" : string.Join("\n  ", removed);

            Assert.Fail(
                $"DxMessaging.Core public surface drift detected. Snapshot file: {snapshotPath}\n"
                    + $"Added types:\n  {addedTxt}\n"
                    + $"Removed types:\n  {removedTxt}\n"
                    + "If the change is intentional, regenerate the snapshot by deleting the file and re-running this test."
            );
        }

        /// <summary>
        /// Enumerates every method on <see cref="IMessageBus"/> and asserts
        /// each method name appears at least once as a textual token in the
        /// runtime test source tree. This is intentionally a substring grep:
        /// the test confirms each method is mentioned somewhere in the
        /// fixtures (which is a strong proxy that someone has invoked or
        /// referenced it), not that every method is exercised by a real
        /// invocation inside a <c>[Test]</c>/<c>[UnityTest]</c> body. A
        /// stronger structural check would require Roslyn (not currently a
        /// dependency of the runtime test asmdef) or a runtime invocation
        /// trace; until that lands, the substring grep is our pragmatic
        /// floor.
        /// </summary>
        /// <remarks>
        /// The historical name of this test was
        /// <c>EveryIMessageBusMethodHasAtLeastOneTest</c>; the rename clarifies
        /// what is actually being checked so failure messages do not overstate
        /// coverage.
        /// </remarks>
        [Test]
        public void EveryIMessageBusMethodIsTextuallyMentioned()
        {
            HashSet<string> methodNames = new HashSet<string>(
                typeof(IMessageBus)
                    .GetMethods(
                        BindingFlags.Public
                            | BindingFlags.Instance
                            | BindingFlags.DeclaredOnly
                            | BindingFlags.Static
                    )
                    .Where(m => !m.IsSpecialName)
                    .Select(m => m.Name),
                StringComparer.Ordinal
            );

            // Walk the source files for the test assembly. We keep a list
            // of search roots: the parent of one well-known test fixture's
            // file (resolved from the running assembly metadata).
            List<string> searchRoots = ResolveTestSourceRoots();
            if (searchRoots.Count == 0)
            {
                Assert.Inconclusive(
                    "Could not resolve any test source root for IMessageBus method coverage check."
                );
                return;
            }

            HashSet<string> covered = new HashSet<string>(StringComparer.Ordinal);
            foreach (string root in searchRoots)
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                foreach (
                    string file in Directory.EnumerateFiles(
                        root,
                        "*.cs",
                        SearchOption.AllDirectories
                    )
                )
                {
                    string text;
                    try
                    {
                        text = File.ReadAllText(file);
                    }
                    catch (IOException)
                    {
                        continue;
                    }

                    foreach (string name in methodNames)
                    {
                        if (covered.Contains(name))
                        {
                            continue;
                        }

                        if (text.IndexOf(name, StringComparison.Ordinal) >= 0)
                        {
                            covered.Add(name);
                        }
                    }
                }
            }

            List<string> missing = methodNames
                .Except(covered)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();
            Assert.That(
                missing,
                Is.Empty,
                "IMessageBus methods with no textual mention anywhere in the test source tree (this is "
                    + "a substring grep, not a structural call-site check):\n  "
                    + string.Join("\n  ", missing)
            );
        }

        /// <summary>
        /// Tightens the existing
        /// <see cref="TestAttributeContractTests.EveryEmitPathHasAllocationCoverage"/>
        /// invariant by enumerating <see cref="MessageKind"/> directly and
        /// asserting every value appears in
        /// <see cref="MessageScenarios.AllKinds"/>. The contract tests already
        /// pin this; this test is an explicit duplicate so a mistake in one
        /// location surfaces in the other.
        /// </summary>
        [Test]
        public void EveryMessageKindAppearsInAllKinds()
        {
            HashSet<MessageKind> covered = new HashSet<MessageKind>(
                MessageScenarios.AllKinds.Select(scenario => scenario.Kind)
            );

            List<string> missing = new List<string>();
            foreach (MessageKind kind in Enum.GetValues(typeof(MessageKind)))
            {
                if (!covered.Contains(kind))
                {
                    missing.Add(kind.ToString());
                }
            }

            Assert.That(
                missing,
                Is.Empty,
                "MessageScenarios.AllKinds must yield every MessageKind. Missing: "
                    + string.Join(", ", missing)
            );
        }

        private static List<string> EnumeratePublicCoreTypeNames()
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            List<string> names = new List<string>();

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types.Where(t => t != null).ToArray();
                }

                foreach (Type type in types)
                {
                    if (!type.IsPublic && !type.IsNestedPublic)
                    {
                        continue;
                    }

                    if (type.Namespace == null)
                    {
                        continue;
                    }

                    if (
                        !type.Namespace.Equals(CoreNamespacePrefix, StringComparison.Ordinal)
                        && !type.Namespace.StartsWith(
                            CoreNamespacePrefix + ".",
                            StringComparison.Ordinal
                        )
                    )
                    {
                        continue;
                    }

                    string fullName = type.FullName;
                    if (string.IsNullOrEmpty(fullName))
                    {
                        continue;
                    }

                    if (seen.Add(fullName))
                    {
                        names.Add(fullName);
                    }
                }
            }

            names.Sort(StringComparer.Ordinal);
            return names;
        }

        private static string TryResolveSnapshotPath(out bool resolved)
        {
            // Walk up from the running assembly's location toward the
            // package root. The snapshot lives at
            // Tests/Runtime/Core/Snapshots/public-surface.txt relative to
            // the package's content root. Application.dataPath is the
            // Unity project's Assets/, but the package may be a
            // package-manager package outside of Assets/, so we instead
            // probe for the well-known directory chain.
            string[] candidates =
            {
                Path.Combine(
                    Application.dataPath,
                    "..",
                    "Packages",
                    "com.wallstop-studios.dxmessaging",
                    "Tests",
                    "Runtime",
                    "Core",
                    SnapshotResourceFolderName,
                    PublicSurfaceSnapshotFileName
                ),
                Path.Combine(
                    Application.dataPath,
                    "..",
                    "Tests",
                    "Runtime",
                    "Core",
                    SnapshotResourceFolderName,
                    PublicSurfaceSnapshotFileName
                ),
                Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "Tests",
                    "Runtime",
                    "Core",
                    SnapshotResourceFolderName,
                    PublicSurfaceSnapshotFileName
                ),
            };

            foreach (string candidate in candidates)
            {
                string full = Path.GetFullPath(candidate);
                string parent = Path.GetDirectoryName(full);
                if (parent != null && Directory.Exists(parent))
                {
                    resolved = true;
                    return full;
                }
            }

            resolved = false;
            return string.Empty;
        }

        private static List<string> ResolveTestSourceRoots()
        {
            List<string> roots = new List<string>();
            string[] candidates =
            {
                Path.Combine(
                    Application.dataPath,
                    "..",
                    "Packages",
                    "com.wallstop-studios.dxmessaging",
                    "Tests",
                    "Runtime"
                ),
                Path.Combine(Application.dataPath, "..", "Tests", "Runtime"),
                Path.Combine(Directory.GetCurrentDirectory(), "Tests", "Runtime"),
            };

            foreach (string candidate in candidates)
            {
                string full = Path.GetFullPath(candidate);
                if (Directory.Exists(full))
                {
                    roots.Add(full);
                }
            }

            return roots;
        }
    }
}
#endif
