namespace WallstopStudios.DxMessaging.SourceGenerators
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Diagnostics;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using Microsoft.CodeAnalysis.Text;

    [Generator(LanguageNames.CSharp)]
    public sealed class DxAutoConstructorGenerator : IIncrementalGenerator
    {
        private const string AutoGenConstructorAttrFullName =
            "DxMessaging.Core.Attributes.DxAutoConstructorAttribute";

        private const string OptionalParameterAttrFullName =
            "DxMessaging.Core.Attributes.DxOptionalParameterAttribute";

        // Info needed during generation for a valid type
        private record struct TypeToGenerateInfo(
            INamedTypeSymbol TypeSymbol,
            TypeDeclarationSyntax DeclarationSyntax,
            ImmutableArray<IFieldSymbol> FieldsToInject // Public readonly non-static fields
        );

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // Find all class/struct/record declarations that have attribute lists
            IncrementalValuesProvider<TypeDeclarationSyntax> potentialTypeDeclarations =
                context.SyntaxProvider.CreateSyntaxProvider(
                    predicate: static (node, _) => IsSyntaxTargetForGeneration(node),
                    transform: static (ctx, _) => (TypeDeclarationSyntax)ctx.Node
                );

            // Get semantic info for potential types
            IncrementalValuesProvider<TypeToGenerateInfo?> semanticTargets =
                potentialTypeDeclarations
                    .Combine(context.CompilationProvider)
                    .Select(
                        static (data, ct) =>
                            GetSemanticTargetForGeneration(data.Left, data.Right, ct)
                    );

            // Filter out nulls (types that aren't valid for auto-gen constructor)
            IncrementalValuesProvider<TypeToGenerateInfo> validSemanticTargets = semanticTargets
                .Where(static target => target.HasValue)
                .Select(static (target, _) => target!.Value);

            // Collect all valid types for generation
            IncrementalValueProvider<ImmutableArray<TypeToGenerateInfo>> collectedTargets =
                validSemanticTargets.Collect();

            IncrementalValueProvider<(
                Compilation,
                ImmutableArray<TypeToGenerateInfo>
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

        private static TypeToGenerateInfo? GetSemanticTargetForGeneration(
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
                is not { } typeSymbol
            )
            {
                return null;
            }

            // Check if the type has the DxAutoGenConstructor attribute
            bool hasAutoGenConstructorAttribute = false;
            foreach (AttributeData attributeData in typeSymbol.GetAttributes())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (
                    attributeData.AttributeClass?.ToDisplayString()
                    == AutoGenConstructorAttrFullName
                )
                {
                    hasAutoGenConstructorAttribute = true;
                    break;
                }
            }

            if (!hasAutoGenConstructorAttribute)
            {
                return null;
            }

            // Find public readonly non-static fields in declaration order
            ImmutableArray<IFieldSymbol> fieldsToInject = typeSymbol
                .GetMembers()
                .OfType<IFieldSymbol>()
                .Where(f => f.DeclaredAccessibility == Accessibility.Public && !f.IsStatic)
                .OrderBy(f => f.DeclaringSyntaxReferences.FirstOrDefault()?.Span.Start ?? 0) // Order by declaration in source
                .ToImmutableArray();

            // If there are no relevant fields, we don't need to generate a constructor
            if (fieldsToInject.Length == 0)
            {
                return null;
            }

            return new TypeToGenerateInfo(typeSymbol, typeDeclarationSyntax, fieldsToInject);
        }

        private static void Execute(
            Compilation compilation,
            ImmutableArray<TypeToGenerateInfo> typesToGenerate,
            SourceProductionContext context
        )
        {
            if (typesToGenerate.IsDefaultOrEmpty)
            {
                return;
            }

            // Use a HashSet to track types already processed to avoid duplicate generation for partial classes
            HashSet<INamedTypeSymbol> processedTypes = new(SymbolEqualityComparer.Default);

            foreach (TypeToGenerateInfo typeInfo in typesToGenerate)
            {
                if (!processedTypes.Add(typeInfo.TypeSymbol) || typeInfo.FieldsToInject.Length == 0)
                {
                    continue; // Already processed this type (e.g., from another partial definition)
                }

                context.CancellationToken.ThrowIfCancellationRequested();

                // Generate the partial class/struct with the constructor
                string generatedSource = GenerateConstructorSource(
                    typeInfo.TypeSymbol,
                    typeInfo.FieldsToInject
                );
                string hintName =
                    $"{typeInfo.TypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}.AutoGenConstructor.g.cs"
                        .Replace("global::", "")
                        .Replace("<", "_")
                        .Replace(">", "_")
                        .Replace(",", "_"); // Clean hint name for file system

                context.AddSource(hintName, SourceText.From(generatedSource, Encoding.UTF8));
            }
        }

        private static string GenerateConstructorSource(
            INamedTypeSymbol typeSymbol,
            ImmutableArray<IFieldSymbol> fieldsToInject
        )
        {
            string namespaceName = typeSymbol.ContainingNamespace.IsGlobalNamespace
                ? string.Empty
                : typeSymbol.ContainingNamespace.ToDisplayString();
            string namespaceBlockOpen = string.IsNullOrEmpty(namespaceName)
                ? string.Empty
                : $"namespace {namespaceName}\n{{";
            string namespaceBlockClose = string.IsNullOrEmpty(namespaceName) ? string.Empty : "}";
            const string indent = "    ";

            string typeName = typeSymbol.ToDisplayString(
                SymbolDisplayFormat.MinimallyQualifiedFormat
            );
            string typeKind = typeSymbol.TypeKind switch
            {
                TypeKind.Class => typeSymbol.IsRecord ? "record class" : "class",
                TypeKind.Struct => typeSymbol.IsRecord ? "record struct" : "struct",
                _ => throw new InvalidOperationException(
                    "Unsupported type kind for constructor generation"
                ),
            };

            string typeAccessibility = typeSymbol.DeclaredAccessibility switch
            {
                Accessibility.Public => "public",
                Accessibility.Protected => "protected",
                Accessibility.Private => "private",
                Accessibility.Internal => "internal",
                _ => "internal", // Default to internal if not public or protected
            };

            string constructorAccessibility = "public"; // Always public as requested

            // Generate constructor parameters and body assignments
            StringBuilder constructorParams = new();
            StringBuilder constructorBody = new();

            SymbolDisplayFormat fieldTypeFormat =
                SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
                    SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers
                );

            List<(string Type, string Name, bool IsOptional)> parameterDetails = [];

            foreach (IFieldSymbol field in fieldsToInject)
            {
                string fieldType = field.Type.ToDisplayString(fieldTypeFormat);
                string fieldName = field.Name;
                bool isOptional = field
                    .GetAttributes()
                    .Any(attr =>
                        string.Equals(
                            attr.AttributeClass?.ToDisplayString(),
                            OptionalParameterAttrFullName,
                            StringComparison.Ordinal
                        )
                    );

                parameterDetails.Add((fieldType, fieldName, isOptional));
                constructorBody.AppendLine($"{indent}{indent}    this.{fieldName} = {fieldName};");
            }

            for (int i = 0; i < parameterDetails.Count; i++)
            {
                (string Type, string Name, bool IsOptional) p = parameterDetails[i];
                constructorParams.Append($"{p.Type} {p.Name}");
                if (p.IsOptional)
                {
                    constructorParams.Append(" = default");
                }

                if (i < parameterDetails.Count - 1)
                {
                    constructorParams.Append(", ");
                }
            }

            return $$"""
                // <auto-generated by DxAutoGenConstructorGenerator/>
                #pragma warning disable
                #nullable enable annotations

                {{namespaceBlockOpen}}
                {{indent}}{{typeAccessibility}} partial {{typeKind}} {{typeName}}
                {{indent}}{
                {{indent}}    /// <summary>
                {{indent}}    /// Auto-generated constructor by DxAutoGenConstructorGenerator.
                {{indent}}    /// </summary>
                {{indent}}    {{constructorAccessibility}} {{typeSymbol.Name}}({{constructorParams}})
                {{indent}}    {
                {{constructorBody}}
                {{indent}}    }
                {{indent}}}
                {{namespaceBlockClose}}
                """;
        }
    }
}
