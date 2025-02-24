namespace WallstopStudios.DxMessaging.SourceGenerators;

using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

[Generator]
public sealed class DxAutoMessageTypeGenerator : ISourceGenerator
{
    public void Initialize(GeneratorInitializationContext context)
    {
        context.RegisterForSyntaxNotifications(() => new SyntaxReceiver());
    }

    public void Execute(GeneratorExecutionContext context)
    {
        if (context.SyntaxReceiver is not SyntaxReceiver receiver)
        {
            return;
        }

        INamedTypeSymbol attributeSymbol = context.Compilation.GetTypeByMetadataName(
            "DxMessaging.Core.Attributes.DxAutoMessageTypeAttribute"
        );

        foreach (TypeDeclarationSyntax classDeclaration in receiver.CandidateClasses)
        {
            SemanticModel model = context.Compilation.GetSemanticModel(classDeclaration.SyntaxTree);
            ISymbol classSymbol = ModelExtensions.GetDeclaredSymbol(model, classDeclaration);

            if (
                classSymbol
                    .GetAttributes()
                    .Any(attributeData =>
                        attributeData.AttributeClass.Equals(
                            attributeSymbol,
                            SymbolEqualityComparer.Default
                        )
                    )
            )
            {
                string namespaceName = classSymbol.ContainingNamespace.ToDisplayString();
                string className = classSymbol.Name;
                string typeKind =
                    classDeclaration.Kind() == SyntaxKind.ClassDeclaration ? "class" : "struct";

                string source = $$"""

                    namespace {{namespaceName}}
                    {
                        public partial {{typeKind}} {{className}}
                        {
                            public System.Type MessageType => typeof({{className}});
                        }
                    }
                    
                    """;

                context.AddSource(
                    $"{className}_DxAutoMessageType.g.cs",
                    SourceText.From(source, Encoding.UTF8)
                );
            }
        }
    }

    private sealed class SyntaxReceiver : ISyntaxReceiver
    {
        public List<TypeDeclarationSyntax> CandidateClasses { get; } = [];

        public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
        {
            if (syntaxNode is TypeDeclarationSyntax typeDeclarationSyntax)
            {
                if (
                    typeDeclarationSyntax.AttributeLists.Count > 0
                    && (
                        typeDeclarationSyntax.Kind() == SyntaxKind.ClassDeclaration
                        || typeDeclarationSyntax.Kind() == SyntaxKind.StructDeclaration
                    )
                )
                {
                    CandidateClasses.Add(typeDeclarationSyntax);
                }
            }
        }
    }
}
