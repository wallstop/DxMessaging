using System.Linq;
using Microsoft.CodeAnalysis;
using NUnit.Framework;

namespace WallstopStudios.DxMessaging.SourceGenerators.Tests;

[TestFixture]
public sealed class DxAutoConstructorGeneratorDiagnosticsTests
{
    [Test]
    public void ReportsNonPartialContainingType()
    {
        string source = """
using DxMessaging.Core.Attributes;

namespace Sample;

public class Container
{
    [DxAutoConstructor]
    public readonly struct NestedMessage
    {
        public readonly int value;
    }
}
""";

        GeneratorDriverRunResult result = GeneratorTestUtilities.RunDxAutoConstructor(source);
        Diagnostic[] diagnostics = result.Results[0].Diagnostics.ToArray();

        Assert.That(
            diagnostics,
            Has.Some.Matches<Diagnostic>(d => d.Id == "DXMSG003"),
            "DXMSG003 should be reported when nested types are not declared inside partial containers."
        );
        Assert.That(
            diagnostics,
            Has.Some.Matches<Diagnostic>(d => d.Id == "DXMSG004"),
            "DXMSG004 should suggest adding the partial keyword for the containing type."
        );
    }

    [Test]
    public void ReportsInvalidOptionalDefaultExpression()
    {
        string source = """
using DxMessaging.Core.Attributes;

namespace Sample;

[DxTargetedMessage]
[DxAutoConstructor]
public readonly partial struct InvalidOptional
{
    [DxOptionalParameter(Expression = "value > 0")]
    public readonly int value;
}
""";

        GeneratorDriverRunResult result = GeneratorTestUtilities.RunDxAutoConstructor(source);
        Diagnostic[] diagnostics = result.Results[0].Diagnostics.ToArray();

        Assert.That(
            diagnostics,
            Has.Some.Matches<Diagnostic>(d => d.Id == "DXMSG005"),
            "DXMSG005 should be reported for optional defaults that cannot be parsed."
        );
    }
}
