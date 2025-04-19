namespace WallstopStudios.DxMessaging.SourceGenerators
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using Microsoft.CodeAnalysis.Text;

    [Generator(LanguageNames.CSharp)]
    public sealed class DxMessageIdGenerator : IIncrementalGenerator
    {
        // Base IMessage interface (used for implementation checks if needed, and property names)
        // *** Assumes the user has defined this interface in their code ***
        private const string BaseInterfaceFullName = "DxMessaging.Core.IMessage";

        // Message Type Attribute Full Names (Ensure these match your attributes)
        private const string BroadcastAttrFullName =
            "DxMessaging.Core.Attributes.DxBroadcastMessageAttribute";
        private const string TargetedAttrFullName =
            "DxMessaging.Core.Attributes.DxTargetedMessageAttribute";
        private const string UntargetedAttrFullName =
            "DxMessaging.Core.Attributes.DxUntargetedMessageAttribute";

        // Target Interface Full Names (Ensure these match your specific message interfaces)
        private const string BroadcastInterfaceFullName =
            "DxMessaging.Core.Messages.IBroadcastMessage";
        private const string TargetedInterfaceFullName =
            "DxMessaging.Core.Messages.ITargetedMessage";
        private const string UntargetedInterfaceFullName =
            "DxMessaging.Core.Messages.IUntargetedMessage";

        // Diagnostics
        private static readonly DiagnosticDescriptor MultipleAttributesError = new(
            id: "DXMSG002",
            title: "Multiple Message Attributes",
            messageFormat: "Type '{0}' cannot have more than one Dx message attribute ([DxBroadcastMessage], [DxTargetedMessage], [DxUntargetedMessage]).",
            category: "DxMessaging",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true
        );

        // Information needed during the generation phase for a valid message type
        private record struct MessageToGenerateInfo(
            INamedTypeSymbol TypeSymbol,
            TypeDeclarationSyntax DeclarationSyntax,
            string TargetInterfaceFullName // The specific interface like IBroadcastMessage
        );

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // Find all class/struct/record declarations with attributes
            IncrementalValuesProvider<TypeDeclarationSyntax> potentialTypeDeclarations =
                context.SyntaxProvider.CreateSyntaxProvider(
                    predicate: static (node, _) => IsSyntaxTargetForGeneration(node),
                    transform: static (ctx, _) => (TypeDeclarationSyntax)ctx.Node
                );

            // Get semantic info for potential types
            IncrementalValuesProvider<MessageToGenerateInfo?> semanticTargets =
                potentialTypeDeclarations
                    .Combine(context.CompilationProvider)
                    .Select(
                        static (data, ct) =>
                            GetSemanticTargetForGeneration(data.Left, data.Right, ct)
                    );

            // Filter out nulls (types that aren't valid messages)
            IncrementalValuesProvider<MessageToGenerateInfo> validSemanticTargets = semanticTargets
                .Where(static target => target.HasValue)
                .Select(static (target, _) => target!.Value);

            // Group by type symbol to handle partial classes correctly and check for multiple attributes
            IncrementalValueProvider<ImmutableArray<MessageToGenerateInfo>> collectedTargets =
                validSemanticTargets.Collect();

            IncrementalValueProvider<(
                Compilation,
                ImmutableArray<MessageToGenerateInfo>
            )> compilationAndTypes = context.CompilationProvider.Combine(collectedTargets);

            // Register the source output step
            context.RegisterSourceOutput(
                compilationAndTypes,
                static (spc, source) => Execute(source.Item1, source.Item2, spc)
            );
        }

        private static bool IsSyntaxTargetForGeneration(SyntaxNode node) =>
            node is TypeDeclarationSyntax { AttributeLists.Count: > 0 } typeDecl
            && (
                typeDecl.IsKind(SyntaxKind.ClassDeclaration)
                || typeDecl.IsKind(SyntaxKind.StructDeclaration)
                || typeDecl.IsKind(SyntaxKind.RecordDeclaration)
                || typeDecl.IsKind(SyntaxKind.RecordStructDeclaration)
            );

        private static MessageToGenerateInfo? GetSemanticTargetForGeneration(
            TypeDeclarationSyntax typeDeclarationSyntax,
            Compilation compilation,
            CancellationToken cancellationToken
        )
        {
            SemanticModel semanticModel = compilation.GetSemanticModel(
                typeDeclarationSyntax.SyntaxTree
            );
            if (
                semanticModel.GetDeclaredSymbol(typeDeclarationSyntax, cancellationToken)
                is not INamedTypeSymbol typeSymbol
            )
            {
                return null;
            }

            // Ensure it's not abstract or static (if class)
            if (
                typeSymbol.IsAbstract
                || (typeSymbol.IsStatic && typeSymbol.TypeKind == TypeKind.Class)
            )
            {
                return null; // Cannot be a concrete message type
            }

            string? foundTargetInterface = null;
            bool multipleAttributes = false;

            // Check attributes to find the specific message type (Broadcast, Targeted, etc.)
            foreach (AttributeData attributeData in typeSymbol.GetAttributes())
            {
                cancellationToken.ThrowIfCancellationRequested();
                string? currentAttributeFullName = attributeData.AttributeClass?.ToDisplayString();
                string? targetInterfaceForThisAttribute = null;

                switch (currentAttributeFullName)
                {
                    case BroadcastAttrFullName:
                        targetInterfaceForThisAttribute = BroadcastInterfaceFullName;
                        break;
                    case TargetedAttrFullName:
                        targetInterfaceForThisAttribute = TargetedInterfaceFullName;
                        break;
                    case UntargetedAttrFullName:
                        targetInterfaceForThisAttribute = UntargetedInterfaceFullName;
                        break;
                }

                if (targetInterfaceForThisAttribute != null)
                {
                    if (
                        foundTargetInterface != null
                        && foundTargetInterface != targetInterfaceForThisAttribute
                    )
                    {
                        multipleAttributes = true;
                        break;
                    }
                    foundTargetInterface = targetInterfaceForThisAttribute;
                }
            }

            if (multipleAttributes || foundTargetInterface == null)
            {
                // Don't return info if multiple different message attrs or none found.
                // The Execute method will report the error for multiple attributes later.
                return null;
            }

            return new MessageToGenerateInfo(
                typeSymbol,
                typeDeclarationSyntax,
                foundTargetInterface
            );
        }

        private static void Execute(
            Compilation compilation,
            ImmutableArray<MessageToGenerateInfo> typesToGenerate,
            SourceProductionContext context
        )
        {
            if (typesToGenerate.IsDefaultOrEmpty)
            {
                return;
            }

            // --- Step 1: Filter out types with multiple attributes applied ---
            Dictionary<ISymbol, MessageToGenerateInfo> uniqueTypes = new(
                SymbolEqualityComparer.Default
            );
            HashSet<ISymbol> typesWithMultipleAttributes = new(SymbolEqualityComparer.Default);

            foreach (MessageToGenerateInfo typeInfo in typesToGenerate)
            {
                if (uniqueTypes.ContainsKey(typeInfo.TypeSymbol))
                {
                    // If adding fails, it means the same TypeSymbol appeared multiple times.
                    // This implies multiple different valid attributes were found, report error.
                    if (typesWithMultipleAttributes.Add(typeInfo.TypeSymbol)) // Report only once
                    {
                        context.ReportDiagnostic(
                            Diagnostic.Create(
                                MultipleAttributesError,
                                typeInfo.DeclarationSyntax.Identifier.GetLocation(),
                                typeInfo.TypeSymbol.ToDisplayString()
                            )
                        );
                        // Also report for the one already in the dictionary if needed, but one report per type is usually sufficient.
                    }
                }
                else
                {
                    uniqueTypes[typeInfo.TypeSymbol] = typeInfo;
                }
            }

            List<MessageToGenerateInfo> validSingleAttrTypes = uniqueTypes
                .Where(kvp => !typesWithMultipleAttributes.Contains(kvp.Key))
                .Select(kvp => kvp.Value)
                .ToList();

            if (validSingleAttrTypes.Count == 0)
            {
                return;
            }

            // --- Step 2: Generate sources for each valid message type ---
            foreach (MessageToGenerateInfo messageInfo in validSingleAttrTypes)
            {
                context.CancellationToken.ThrowIfCancellationRequested();

                // Generate the Metadata class source
                string metadataSource = GenerateMetadataSource(messageInfo.TypeSymbol);
                string metadataHintName =
                    $"{messageInfo.TypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}.Metadata.g.cs"
                        .Replace("global::", "")
                        .Replace("<", "_")
                        .Replace(">", "_")
                        .Replace(",", "_"); // Clean hint name

                context.AddSource(metadataHintName, SourceText.From(metadataSource, Encoding.UTF8));

                // Generate the partial IMessage implementation source
                string implSource = GenerateImplementationSource(
                    messageInfo.TargetInterfaceFullName,
                    messageInfo.TypeSymbol
                );
                string implHintName =
                    $"{messageInfo.TypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}.IMessage.g.cs"
                        .Replace("global::", "")
                        .Replace("<", "_")
                        .Replace(">", "_")
                        .Replace(",", "_"); // Clean hint name

                context.AddSource(implHintName, SourceText.From(implSource, Encoding.UTF8));
            }
        }

        // Generates the MessageMetadata<T> class
        private static string GenerateMetadataSource(INamedTypeSymbol typeSymbol)
        {
            string namespaceName = typeSymbol.ContainingNamespace.IsGlobalNamespace
                ? string.Empty
                : typeSymbol.ContainingNamespace.ToDisplayString();
            string namespaceBlockOpen = string.IsNullOrEmpty(namespaceName)
                ? ""
                : $"namespace {namespaceName}\n{{";
            const string bracketOpen = "{";
            const string bracketClosed = "}";
            string namespaceBlockClose = string.IsNullOrEmpty(namespaceName) ? "" : "}";
            string indent = string.IsNullOrEmpty(namespaceName) ? "" : "    ";

            string typeNameMinimal = typeSymbol.ToDisplayString(
                SymbolDisplayFormat.MinimallyQualifiedFormat
            );
            string fullyQualifiedName = typeSymbol.ToDisplayString(
                SymbolDisplayFormat.FullyQualifiedFormat
            );
            string typeKey = fullyQualifiedName.StartsWith("global::")
                ? fullyQualifiedName.Substring("global::".Length)
                : fullyQualifiedName;
            typeKey = typeKey.Replace("\"", "\\\"");

            // --- SIMPLIFIED METADATA CLASS NAME ---
            // Use only the base name, rely on namespace and generic parameter for uniqueness.
            // Remove Arity (`1) and nesting prefixes.
            string metadataClassName = $"DxMsgMeta_{typeSymbol.Name}"; // Prefix ensures less likely to clash

            // Prepare generic parameters string IF the type is generic
            string genericParameters = "";
            string genericConstraints = "";
            if (typeSymbol.Arity > 0)
            {
                // Build "<T1, T2, ...>"
                genericParameters =
                    "<" + string.Join(", ", typeSymbol.TypeParameters.Select(p => p.Name)) + ">";

                // Build "where T1 : constraints... where T2 : constraints..."
                // Important: We need to copy existing constraints from the original type!
                var constraintsBuilder = new StringBuilder();
                foreach (var typeParam in typeSymbol.TypeParameters)
                {
                    var constraints = new List<string>();
                    if (typeParam.HasReferenceTypeConstraint)
                        constraints.Add("class");
                    if (typeParam.HasValueTypeConstraint)
                        constraints.Add("struct"); // Cannot have both class and struct
                    if (typeParam.HasUnmanagedTypeConstraint)
                        constraints.Add("unmanaged");
                    if (typeParam.HasNotNullConstraint)
                        constraints.Add("notnull"); // C# 8+
                    // Add type constraints (interfaces, base classes)
                    constraints.AddRange(
                        typeParam.ConstraintTypes.Select(ct =>
                            ct.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                        )
                    );
                    // Add constructor constraint
                    if (typeParam.HasConstructorConstraint)
                        constraints.Add("new()");

                    if (constraints.Count > 0)
                    {
                        // Always add the IMessage constraint for safety, IF the type parameter doesn't already imply it strongly.
                        // However, the runtime check relies on the actual message type implementing IMessage.
                        // Let's just copy existing constraints for now. It's cleaner.
                        // The `where TMessage : IMessage` isn't strictly needed on the metadata class itself.

                        constraintsBuilder.Append(
                            $" where {typeParam.Name} : {string.Join(", ", constraints)}"
                        );
                    }
                }
                genericConstraints = constraintsBuilder.ToString();
            }

            return $$"""
                // <auto-generated by DxMessageIdGenerator/>
                #pragma warning disable
                #nullable enable annotations

                {{namespaceBlockOpen}}
                {{indent}}}/// <summary>
                {{indent}}}/// Internal metadata storage for message type <see cref="{{typeNameMinimal}}"/>. Populated by runtime initialization.
                {{indent}}}/// </summary>
                {{indent}}}internal static class {{metadataClassName}}{{genericParameters}}{{genericConstraints}}
                {{indent}}}{{bracketOpen}}
                {{indent}}}    public static int SequentialId = -1;
                {{indent}}}    public static readonly string TypeKey = "{{typeKey}}";
                {{indent}}}{{bracketClosed}}
                {{namespaceBlockClose}}
                """;
        }

        // Generates the partial class/struct implementing IMessage
        private static string GenerateImplementationSource(
            string targetInterfaceFullName, // e.g., IBroadcastMessage
            INamedTypeSymbol typeSymbol
        )
        {
            string namespaceName = typeSymbol.ContainingNamespace.IsGlobalNamespace
                ? string.Empty
                : typeSymbol.ContainingNamespace.ToDisplayString();
            string namespaceBlockOpen = string.IsNullOrEmpty(namespaceName)
                ? ""
                : $"namespace {namespaceName}\n{{";
            string namespaceBlockClose = string.IsNullOrEmpty(namespaceName) ? "" : "}";
            string indent = string.IsNullOrEmpty(namespaceName) ? "" : "    ";

            string typeNameWithGenerics = typeSymbol.ToDisplayString(
                SymbolDisplayFormat.MinimallyQualifiedFormat
            );
            string fullyQualifiedName = typeSymbol.ToDisplayString(
                SymbolDisplayFormat.FullyQualifiedFormat
            );

            string typeKind = typeSymbol.TypeKind switch
            {
                TypeKind.Class => typeSymbol.IsRecord ? "record class" : "class",
                TypeKind.Struct => typeSymbol.IsRecord ? "record struct" : "struct",
                _ => throw new InvalidOperationException("Unsupported type kind"),
            };

            string accessibility = typeSymbol.DeclaredAccessibility switch
            {
                Accessibility.Public => "public ",
                Accessibility.Internal => "internal ",
                // Add others if necessary, default to internal if restrictive
                _ => "internal ",
            };

            // Construct the name of the corresponding metadata class
            string metadataClassName = $"DxMsgMeta_{typeSymbol.Name}"; // Must match GenerateMetadataSource

            // Fully qualified name needed if in a namespace
            string metadataClassFullName = string.IsNullOrEmpty(namespaceName)
                ? metadataClassName
                : $"{namespaceName}.{metadataClassName}";

            string interfaceDeclaration = $", global::{targetInterfaceFullName}";

            return $$"""
                // <auto-generated by DxMessageIdGenerator/>
                #pragma warning disable
                #nullable enable annotations

                {{namespaceBlockOpen}}
                {{indent}}}// Partial implementation for {{typeNameWithGenerics}} to implement {{BaseInterfaceFullName}}
                {{indent}}}{{accessibility}}partial {{typeKind}} {{typeNameWithGenerics}} : global::{{BaseInterfaceFullName}} {{interfaceDeclaration}}
                {{indent}}}{
                {{indent}}}    /// <inheritdoc/>
                {{indent}}}    global::System.Type global::{{BaseInterfaceFullName}}.MessageType => typeof({{fullyQualifiedName}});

                {{indent}}}    /// <inheritdoc/>
                {{indent}}}    [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
                {{indent}}}    public int GetSequentialId() => {{metadataClassFullName}}<{{typeNameWithGenerics}}>.SequentialId;
                {{indent}}} }
                {{namespaceBlockClose}}
                """;
        }
    }
}
