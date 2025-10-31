using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using WallstopStudios.DxMessaging.SourceGenerators;

namespace WallstopStudios.DxMessaging.SourceGenerators.Tests;

internal static class GeneratorTestUtilities
{
    private static readonly CSharpParseOptions ParseOptions = new(
        languageVersion: LanguageVersion.Latest,
        documentationMode: DocumentationMode.Diagnose
    );

    private static readonly ImmutableArray<MetadataReference> CoreReferences =
        BuildCoreReferences();

    internal static GeneratorDriverRunResult RunDxAutoConstructor(string userSource)
    {
        SyntaxTree attributeTree = CSharpSyntaxTree.ParseText(SharedStubs, ParseOptions);
        SyntaxTree userTree = CSharpSyntaxTree.ParseText(userSource, ParseOptions);

        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName: "GeneratorTests",
            syntaxTrees: new[] { attributeTree, userTree },
            references: CoreReferences,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        DxAutoConstructorGenerator generator = new();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGenerators(compilation);

        return driver.GetRunResult();
    }

    internal static GeneratorDriverRunResult RunDxMessageId(string userSource)
    {
        SyntaxTree stubs = CSharpSyntaxTree.ParseText(SharedStubs, ParseOptions);
        SyntaxTree userTree = CSharpSyntaxTree.ParseText(userSource, ParseOptions);

        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName: "GeneratorTests",
            syntaxTrees: new[] { stubs, userTree },
            references: CoreReferences,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        DxMessageIdGenerator generator = new();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGenerators(compilation);

        return driver.GetRunResult();
    }

    internal static ImmutableArray<Diagnostic> CompileSnippet(string userSource)
    {
        SyntaxTree stubs = CSharpSyntaxTree.ParseText(SharedStubs, ParseOptions);
        SyntaxTree userTree = CSharpSyntaxTree.ParseText(userSource, ParseOptions);

        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName: "SnippetCompilation",
            syntaxTrees: new[] { stubs, userTree },
            references: CoreReferences,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        return compilation.GetDiagnostics();
    }

    private static ImmutableArray<MetadataReference> BuildCoreReferences()
    {
        List<MetadataReference> references = new();

        void AddAssembly(Assembly assembly)
        {
            string location = assembly.Location;
            if (!string.IsNullOrEmpty(location))
            {
                references.Add(MetadataReference.CreateFromFile(location));
            }
        }

        AddAssembly(typeof(object).Assembly);
        AddAssembly(typeof(Attribute).Assembly);
        AddAssembly(typeof(Enumerable).Assembly);
        AddAssembly(typeof(List<>).Assembly);

        return references.ToImmutableArray();
    }

    private const string SharedStubs = """
namespace DxMessaging.Core.Attributes
{
    using System;

    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class)]
    public sealed class DxAutoConstructorAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class DxOptionalParameterAttribute : Attribute
    {
        public DxOptionalParameterAttribute() { }

        public DxOptionalParameterAttribute(object _) { }

        public string Expression { get; set; }
    }

    [AttributeUsage(AttributeTargets.Struct)]
    public sealed class DxTargetedMessageAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Struct)]
    public sealed class DxUntargetedMessageAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Struct)]
    public sealed class DxBroadcastMessageAttribute : Attribute { }
}

namespace DxMessaging.Core
{
    using System;

    public interface IMessage
    {
        Type MessageType => GetType();
    }
}

namespace DxMessaging.Core.Messages
{
    public interface IUntargetedMessage { }
    public interface ITargetedMessage { }
    public interface IBroadcastMessage { }
}

namespace UnityEngine
{
    public struct Color
    {
        public static readonly Color green = default;
    }
}
""";
}
