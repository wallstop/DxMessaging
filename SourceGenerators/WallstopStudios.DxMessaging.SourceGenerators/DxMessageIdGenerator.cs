namespace SourceGenerators.WallstopStudios.DxMessaging.SourceGenerators;

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
    // --- Constants for Attributes and Interfaces ---
    private const string BaseInterfaceFullName = "DxMessaging.Core.IMessage"; // Base interface

    private const string BroadcastAttrFullName =
        "DxMessaging.Core.Attributes.DxBroadcastMessageAttribute";
    private const string TargetedAttrFullName =
        "DxMessaging.Core.Attributes.DxTargetedMessageAttribute";
    private const string UntargetedAttrFullName =
        "DxMessaging.Core.Attributes.DxUntargetedMessageAttribute";

    private const string BroadcastInterfaceFullName = "DxMessaging.Core.Messages.IBroadcastMessage";
    private const string TargetedInterfaceFullName = "DxMessaging.Core.Messages.ITargetedMessage";
    private const string UntargetedInterfaceFullName =
        "DxMessaging.Core.Messages.IUntargetedMessage";

    // --- Diagnostics ---
    private static readonly DiagnosticDescriptor CollisionError = new DiagnosticDescriptor(
        id: "DXMSG001",
        title: "Message ID Collision",
        messageFormat: "OptimizedMessageId collision detected across different message types. The generated ID '{0}' is shared by the following types: {1}. Please rename one or more types slightly to resolve the hash collision.",
        category: "DxMessaging",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    private static readonly DiagnosticDescriptor CollisionWarning = new DiagnosticDescriptor(
        id: "DXMSG003", // New ID
        title: "Message ID Collision Fallback",
        messageFormat: "OptimizedMessageId collision detected for ID '{0}'. The following types will use a fallback implementation (HasOptimizedId = false): {1}.",
        category: "DxMessaging",
        defaultSeverity: DiagnosticSeverity.Warning, // Set severity to Warning
        isEnabledByDefault: true
    );

    private static readonly DiagnosticDescriptor MultipleAttributesError = new DiagnosticDescriptor(
        id: "DXMSG002",
        title: "Multiple Message Attributes",
        messageFormat: "Type '{0}' cannot have more than one Dx message attribute ([DxBroadcastMessage], [DxTargetedMessage], [DxUntargetedMessage]).",
        category: "DxMessaging",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    // Helper record to pass data through the pipeline
    private record struct SemanticTargetInfo(
        INamedTypeSymbol TypeSymbol,
        TypeDeclarationSyntax DeclarationSyntax,
        string TargetInterfaceFullName
    );

    // Helper record for final processing stage
    private record struct MessageInfo(
        INamedTypeSymbol TypeSymbol,
        TypeDeclarationSyntax DeclarationSyntax,
        string TargetInterfaceFullName,
        int GeneratedId
    );

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // --- Step 1: Find all classes/structs with attribute lists (Potential Candidates) ---
        IncrementalValuesProvider<TypeDeclarationSyntax> potentialTypeDeclarations =
            context.SyntaxProvider.CreateSyntaxProvider(
                predicate: static (node, _) => IsSyntaxTargetForGeneration(node), // Quick syntax filter
                transform: static (ctx, ct) => (TypeDeclarationSyntax)ctx.Node
            ); // Just pass the node

        // --- Step 2: Get Semantic Info and Filter by Attributes ---
        IncrementalValuesProvider<SemanticTargetInfo?> semanticTargets = potentialTypeDeclarations
            .Select(static (typeDecl, ct) => new { typeDecl, ct }) // Combine with CancellationToken if needed implicitly by GetSemanticTargetForGeneration
            .Combine(context.CompilationProvider)
            .Select(
                static (data, ct) =>
                    GetSemanticTargetForGeneration(data.Left.typeDecl, data.Right, ct)
            );

        // --- Step 3: Filter out invalid targets ---
        IncrementalValuesProvider<SemanticTargetInfo> validSemanticTargets = semanticTargets
            .Where(static target => target.HasValue)
            .Select(static (target, _) => target!.Value); // Use non-null assertion or keep filtering

        // --- Step 4: Calculate Hash IDs ---
        IncrementalValuesProvider<MessageInfo> typesWithIds = validSemanticTargets.Select(
            static (target, ct) =>
            {
                string fullyQualifiedName = target.TypeSymbol.ToDisplayString(
                    SymbolDisplayFormat.FullyQualifiedFormat
                );
                int generatedId = ComputeStableHashCode(fullyQualifiedName);
                return new MessageInfo(
                    target.TypeSymbol,
                    target.DeclarationSyntax,
                    target.TargetInterfaceFullName,
                    generatedId
                );
            }
        );

        // --- Step 5: Collect all valid types with IDs ---
        IncrementalValueProvider<(Compilation, ImmutableArray<MessageInfo>)> compilationAndTypes =
            context.CompilationProvider.Combine(typesWithIds.Collect());

        // --- Step 6: Generate source or diagnostics ---
        context.RegisterSourceOutput(
            compilationAndTypes,
            static (spc, source) => Execute(source.Item1, source.Item2, spc)
        );
    }

    // Quick syntax filter: Checks if the node is a class or struct with any attributes
    private static bool IsSyntaxTargetForGeneration(SyntaxNode node) =>
        node is TypeDeclarationSyntax { AttributeLists.Count: > 0 } typeDecl
        && (
            typeDecl.IsKind(SyntaxKind.ClassDeclaration)
            || typeDecl.IsKind(SyntaxKind.StructDeclaration)
        );

    // Semantic filter: Checks if the type has exactly one of the target attributes
    private static SemanticTargetInfo? GetSemanticTargetForGeneration(
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

        string? foundTargetInterface = null;
        bool multipleAttributes = false;

        foreach (AttributeData attributeData in typeSymbol.GetAttributes())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (attributeData.AttributeClass == null)
                continue;

            string currentAttributeFullName = attributeData.AttributeClass.ToDisplayString();
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
                if (foundTargetInterface != null)
                {
                    // Found more than one relevant attribute!
                    multipleAttributes = true;
                    break; // No need to check further attributes
                }
                foundTargetInterface = targetInterfaceForThisAttribute;
            }
        }

        if (multipleAttributes || foundTargetInterface == null)
        {
            // Report error for multiple attributes later in Execute if needed,
            // but don't consider this a valid target for generation now.
            // If foundTargetInterface is null, it didn't have any relevant attribute.
            return null;
        }

        // Found exactly one relevant attribute
        return new SemanticTargetInfo(typeSymbol, typeDeclarationSyntax, foundTargetInterface);
    }

    // --- Main execution logic: collision check, warning, and source generation ---
    private static void Execute(
        Compilation compilation,
        ImmutableArray<MessageInfo> typesToGenerate,
        SourceProductionContext context
    )
    {
        if (typesToGenerate.IsDefaultOrEmpty)
        {
            return; // Nothing to do
        }

        var validTypes = new List<MessageInfo>(typesToGenerate.Length);
        var typesWithMultipleAttributes = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

        // --- Step 1: Check for Multiple Attributes on the same type ---
        var groupedByType = typesToGenerate.GroupBy(
            m => m.TypeSymbol,
            SymbolEqualityComparer.Default
        );

        foreach (var group in groupedByType)
        {
            if (group.Count() > 1)
            {
                ISymbol collidingSymbol = group.Key;
                typesWithMultipleAttributes.Add(collidingSymbol);
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        MultipleAttributesError,
                        collidingSymbol.Locations.FirstOrDefault() ?? Location.None,
                        collidingSymbol.ToDisplayString()
                    )
                );
            }
            else
            {
                // Add types with only one attribute to the list for further processing
                validTypes.Add(group.First());
            }
        }

        // If no types remain after filtering multi-attribute ones, exit
        if (validTypes.Count == 0)
        {
            return;
        }

        // --- Step 2: Detect Collisions Among Valid Types ---
        var collisionGroups = validTypes
            .GroupBy(m => m.GeneratedId)
            .Where(g => g.Count() > 1)
            .ToList(); // Evaluate the query

        // --- Step 3: Identify all types involved in any collision & Report Warnings ---
        var collidingSymbols = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        if (collisionGroups.Count > 0)
        {
            foreach (var group in collisionGroups)
            {
                // Report WARNING diagnostic for each collision group
                string collidingTypeNames = string.Join(
                    ", ",
                    group.Select(m => $"'{m.TypeSymbol.ToDisplayString()}'")
                );
                // Report warning attached to the first type in the group
                Location location = group.First().DeclarationSyntax.Identifier.GetLocation();
                context.ReportDiagnostic(
                    Diagnostic.Create(CollisionWarning, location, group.Key, collidingTypeNames)
                );

                // Add all symbols from this collision group to the set for later checking
                foreach (var collidingInfo in group)
                {
                    collidingSymbols.Add(collidingInfo.TypeSymbol);
                }
            }
        }

        // --- Step 4: Generate Source for ALL valid types ---
        // Iterate through the list of types that had only one valid attribute
        foreach (var info in validTypes)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            // Decide whether to use the optimized ID based on collision participation
            bool useOptimized = !collidingSymbols.Contains(info.TypeSymbol);

            // Generate source, passing the flag indicating optimized or fallback
            string source = GenerateSource(
                info.TargetInterfaceFullName,
                info.TypeSymbol,
                info.GeneratedId, // Pass the ID, GenerateSource uses it conditionally
                useOptimized
            ); // Pass the optimization flag

            context.AddSource(
                $"{info.TypeSymbol.Name}_{info.TargetInterfaceFullName.Split('.').Last()}.g.cs",
                SourceText.From(source, Encoding.UTF8)
            );
        }
    }

    // --- Stable Hash Function (FNV-1a - remains the same) ---
    private static int ComputeStableHashCode(string text)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (char c in text)
            {
                hash = (hash ^ c) * 16777619;
            }
            return (int)hash;
        }
    }

    // --- Source Generation Logic (Takes target interface name AND optimization flag) ---
    private static string GenerateSource(
        string targetInterfaceFullName,
        INamedTypeSymbol typeSymbol,
        int generatedId, // Still needed for the optimized case
        bool useOptimizedId
    ) // New parameter to control output
    {
        string namespaceName = typeSymbol.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : typeSymbol.ContainingNamespace.ToDisplayString();

        string className = typeSymbol.Name;
        string typeNameWithGenerics = typeSymbol.ToDisplayString(
            SymbolDisplayFormat.MinimallyQualifiedFormat
        );
        string fullyQualifiedName = typeSymbol.ToDisplayString(
            SymbolDisplayFormat.FullyQualifiedFormat
        );

        string typeKind = typeSymbol.TypeKind switch
        {
            TypeKind.Class => "class",
            TypeKind.Struct => "struct",
            _ => throw new InvalidOperationException(
                "Unsupported type kind for message generation"
            ),
        };

        bool alreadyDeclaresInterface = typeSymbol.Interfaces.Any(iface =>
            iface.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            == targetInterfaceFullName
        );

        string interfaceDeclaration = alreadyDeclaresInterface
            ? ""
            : $": {targetInterfaceFullName}";

        // --- Conditional generation logic ---
        string hasOptimizedIdValue = useOptimizedId ? "true" : "false";
        // Use the actual generated ID if optimized, otherwise 'default' (which is 0 for int)
        string optimizedIdValue = useOptimizedId ? generatedId.ToString() : "default";
        // ---

        return $$"""
            // <auto-generated by DxMessageIdGenerator/>
            #pragma warning disable
            #nullable enable annotations

            namespace {{namespaceName}}
            {
                partial {{typeKind}} {{typeNameWithGenerics}} {{interfaceDeclaration}}
                {
                    /// <inheritdoc cref="{{BaseInterfaceFullName}}.MessageType"/>
                    System.Type {{BaseInterfaceFullName}}.MessageType => typeof({{fullyQualifiedName}});

                    /// <inheritdoc cref="{{BaseInterfaceFullName}}.HasOptimizedId"/>
                    bool {{BaseInterfaceFullName}}.HasOptimizedId => {{hasOptimizedIdValue}}; // Use conditional value

                    /// <inheritdoc cref="{{BaseInterfaceFullName}}.OptimizedMessageId"/>
                    int {{BaseInterfaceFullName}}.OptimizedMessageId => {{optimizedIdValue}}; // Use conditional value
                }
            }
            """;
    }
}
