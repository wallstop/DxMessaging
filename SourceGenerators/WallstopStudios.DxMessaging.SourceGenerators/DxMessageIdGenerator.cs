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
        private static readonly DiagnosticDescriptor NonPartialContainerDiagnostic = new(
            id: "DXMSG003",
            title: "Containing type must be partial for nested generation",
            messageFormat: "Type '{0}' is nested inside non-partial container(s): {1}. Suggested fix: add the 'partial' keyword to the containing type declaration(s).",
            category: "DxMessaging",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true
        );

        private static readonly DiagnosticDescriptor AddPartialSuggestionDiagnostic = new(
            id: "DXMSG004",
            title: "Add 'partial' keyword to containing type",
            messageFormat: "Add 'partial' to the declaration of '{0}' to enable generation for nested type '{1}'.",
            category: "DxMessaging",
            defaultSeverity: DiagnosticSeverity.Info,
            isEnabledByDefault: true
        );

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
            string TargetInterfaceFullName,
            bool HasConflictingMessageAttributes
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

            string foundTargetInterface = null;
            bool multipleAttributes = false;

            // Check attributes to find the specific message type (Broadcast, Targeted, etc.)
            foreach (AttributeData attributeData in typeSymbol.GetAttributes())
            {
                cancellationToken.ThrowIfCancellationRequested();
                string currentAttributeFullName = attributeData.AttributeClass?.ToDisplayString();
                string targetInterfaceForThisAttribute = null;

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

            if (multipleAttributes)
            {
                foundTargetInterface = null;
            }

            if (foundTargetInterface == null && !multipleAttributes)
            {
                return null;
            }

            return new MessageToGenerateInfo(
                typeSymbol,
                typeDeclarationSyntax,
                foundTargetInterface,
                multipleAttributes
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
            Dictionary<ISymbol, MessageToGenerateInfo> uniqueTypes = new Dictionary<
                ISymbol,
                MessageToGenerateInfo
            >(SymbolEqualityComparer.Default);
            HashSet<ISymbol> conflictingTypes = new HashSet<ISymbol>(
                SymbolEqualityComparer.Default
            );

            foreach (MessageToGenerateInfo typeInfo in typesToGenerate)
            {
                if (typeInfo.HasConflictingMessageAttributes)
                {
                    if (conflictingTypes.Add(typeInfo.TypeSymbol))
                    {
                        context.ReportDiagnostic(
                            Diagnostic.Create(
                                MultipleAttributesError,
                                typeInfo.DeclarationSyntax.Identifier.GetLocation(),
                                typeInfo.TypeSymbol.ToDisplayString()
                            )
                        );
                    }

                    continue;
                }

                if (conflictingTypes.Contains(typeInfo.TypeSymbol))
                {
                    continue;
                }

                if (typeInfo.TargetInterfaceFullName is null)
                {
                    continue;
                }

                if (
                    uniqueTypes.TryGetValue(
                        typeInfo.TypeSymbol,
                        out MessageToGenerateInfo existingInfo
                    )
                )
                {
                    if (
                        !string.Equals(
                            existingInfo.TargetInterfaceFullName,
                            typeInfo.TargetInterfaceFullName,
                            StringComparison.Ordinal
                        ) && conflictingTypes.Add(typeInfo.TypeSymbol)
                    )
                    {
                        context.ReportDiagnostic(
                            Diagnostic.Create(
                                MultipleAttributesError,
                                typeInfo.DeclarationSyntax.Identifier.GetLocation(),
                                typeInfo.TypeSymbol.ToDisplayString()
                            )
                        );
                        uniqueTypes.Remove(typeInfo.TypeSymbol);
                    }
                }
                else
                {
                    uniqueTypes[typeInfo.TypeSymbol] = typeInfo;
                }
            }

            if (uniqueTypes.Count == 0)
            {
                return;
            }

            List<MessageToGenerateInfo> validSingleAttrTypes = new List<MessageToGenerateInfo>();
            foreach (KeyValuePair<ISymbol, MessageToGenerateInfo> entry in uniqueTypes)
            {
                if (conflictingTypes.Contains(entry.Key))
                {
                    continue;
                }

                validSingleAttrTypes.Add(entry.Value);
            }

            if (validSingleAttrTypes.Count == 0)
            {
                return;
            }

            // --- Step 2: Generate sources for each valid message type ---
            foreach (MessageToGenerateInfo messageInfo in validSingleAttrTypes)
            {
                context.CancellationToken.ThrowIfCancellationRequested();

                string targetInterfaceFullName = messageInfo.TargetInterfaceFullName;
                if (targetInterfaceFullName is null)
                {
                    continue;
                }

                // If nested, ensure all containers are declared partial; otherwise report diagnostic and skip
                if (messageInfo.TypeSymbol.ContainingType is not null)
                {
                    List<INamedTypeSymbol> nonPartial = GetNonPartialContainers(
                        messageInfo.TypeSymbol
                    );
                    if (nonPartial.Count > 0)
                    {
                        string containersList = string.Join(
                            ", ",
                            nonPartial.Select(static s =>
                                s.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
                            )
                        );
                        context.ReportDiagnostic(
                            Diagnostic.Create(
                                NonPartialContainerDiagnostic,
                                messageInfo.DeclarationSyntax.Identifier.GetLocation(),
                                messageInfo.TypeSymbol.ToDisplayString(
                                    SymbolDisplayFormat.MinimallyQualifiedFormat
                                ),
                                containersList
                            )
                        );
                        foreach (INamedTypeSymbol container in nonPartial)
                        {
                            SyntaxReference sr =
                                container.DeclaringSyntaxReferences.FirstOrDefault();
                            if (sr != null && sr.GetSyntax() is TypeDeclarationSyntax tds)
                            {
                                context.ReportDiagnostic(
                                    Diagnostic.Create(
                                        AddPartialSuggestionDiagnostic,
                                        tds.Identifier.GetLocation(),
                                        container.ToDisplayString(
                                            SymbolDisplayFormat.MinimallyQualifiedFormat
                                        ),
                                        messageInfo.TypeSymbol.ToDisplayString(
                                            SymbolDisplayFormat.MinimallyQualifiedFormat
                                        )
                                    )
                                );
                            }
                        }
                        continue;
                    }
                }

                // Generate the partial IMessage implementation source
                string implSource = GenerateImplementationSource(
                    targetInterfaceFullName,
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
                ? string.Empty
                : $"namespace {namespaceName}\n{{";
            string namespaceBlockClose = string.IsNullOrEmpty(namespaceName) ? string.Empty : "}";
            const string Indent = "    ";

            // Build container wrappers so partial can merge nested types correctly
            var containers = new Stack<INamedTypeSymbol>();
            INamedTypeSymbol current = typeSymbol.ContainingType;
            while (current is not null)
            {
                containers.Push(current);
                current = current.ContainingType;
            }

            var containersOpen = new StringBuilder();
            var containersClose = new StringBuilder();
            string currentIndent = Indent;
            foreach (INamedTypeSymbol container in containers)
            {
                string containerAccessibility = container.DeclaredAccessibility switch
                {
                    Accessibility.Public => "public",
                    Accessibility.Protected => "protected",
                    Accessibility.Private => "private",
                    Accessibility.Internal => "internal",
                    Accessibility.ProtectedOrInternal => "protected internal",
                    Accessibility.ProtectedAndInternal => "private protected",
                    _ => "internal",
                };

                string containerKind = container.TypeKind switch
                {
                    TypeKind.Class => container.IsRecord ? "record class" : "class",
                    TypeKind.Struct => container.IsRecord ? "record struct" : "struct",
                    _ => "class",
                };

                string containerTypeParams =
                    container.TypeParameters.Length > 0
                        ? "<"
                            + string.Join(", ", container.TypeParameters.Select(static p => p.Name))
                            + ">"
                        : string.Empty;

                // Avoid repeating sealed/abstract/static/readonly/ref to prevent conflicting semantics
                containersOpen.AppendLine(
                    $"{currentIndent}{containerAccessibility} partial {containerKind} {container.Name}{containerTypeParams}"
                );
                containersOpen.Append(currentIndent).AppendLine("{");
                currentIndent += Indent;
            }

            string innerIndent = currentIndent;

            // Use unqualified nested identifier for declaration (containers already opened)
            string typeGenericParams =
                typeSymbol.TypeParameters.Length > 0
                    ? "<"
                        + string.Join(", ", typeSymbol.TypeParameters.Select(static p => p.Name))
                        + ">"
                    : string.Empty;
            string typeNameWithGenerics = typeSymbol.Name + typeGenericParams;
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
                Accessibility.Public => "public",
                Accessibility.Protected => "protected",
                Accessibility.Private => "private",
                Accessibility.Internal => "internal",
                Accessibility.ProtectedOrInternal => "protected internal",
                Accessibility.ProtectedAndInternal => "private protected",
                _ => "internal",
            };

            string interfaceDeclaration = $", global::{targetInterfaceFullName}";

            // Close containers string
            for (int i = 0; i < containers.Count; i++)
            {
                currentIndent = currentIndent.Substring(
                    0,
                    Math.Max(0, currentIndent.Length - Indent.Length)
                );
                containersClose.Append(currentIndent).AppendLine("}");
            }

            return $$"""
                // <auto-generated by DxMessageIdGenerator/>
                #pragma warning disable
                #nullable enable annotations

                {{namespaceBlockOpen}}
                {{containersOpen}}{{innerIndent}}// Partial implementation for {{typeNameWithGenerics}} to implement {{BaseInterfaceFullName}}
                {{innerIndent}}{{accessibility}} partial {{typeKind}} {{typeNameWithGenerics}} : global::{{BaseInterfaceFullName}} {{interfaceDeclaration}}
                {{innerIndent}}{
                {{innerIndent}}    /// <inheritdoc/>
                {{innerIndent}}    public global::System.Type MessageType => typeof({{fullyQualifiedName}});
                {{innerIndent}}}
                {{containersClose}}
                {{namespaceBlockClose}}
                """;
        }

        private static List<INamedTypeSymbol> GetNonPartialContainers(INamedTypeSymbol typeSymbol)
        {
            List<INamedTypeSymbol> result = new();
            INamedTypeSymbol current = typeSymbol.ContainingType;
            while (current is not null)
            {
                if (!IsDeclaredFullyPartial(current))
                {
                    result.Add(current);
                }
                current = current.ContainingType;
            }
            return result;
        }

        private static bool IsDeclaredFullyPartial(INamedTypeSymbol symbol)
        {
            if (symbol.DeclaringSyntaxReferences.Length == 0)
            {
                return false;
            }
            foreach (SyntaxReference syntaxRef in symbol.DeclaringSyntaxReferences)
            {
                if (syntaxRef.GetSyntax() is TypeDeclarationSyntax tds)
                {
                    bool hasPartial = tds.Modifiers.Any(static m =>
                        m.IsKind(SyntaxKind.PartialKeyword)
                    );
                    if (!hasPartial)
                    {
                        return false;
                    }
                }
                else
                {
                    return false;
                }
            }
            return true;
        }
    }
}
