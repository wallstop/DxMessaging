using System.Linq;
using Microsoft.CodeAnalysis;
using NUnit.Framework;

namespace WallstopStudios.DxMessaging.SourceGenerators.Tests;

[TestFixture]
public sealed class DxMessageIdGeneratorDiagnosticsTests
{
    [Test]
    public void ReportsMultipleMessageAttributes()
    {
        string source = """
using DxMessaging.Core.Attributes;
using DxMessaging.Core.Messages;

namespace Sample;

[DxUntargetedMessage]
public readonly partial struct ConflictingMessage : IUntargetedMessage { }

[DxBroadcastMessage]
public readonly partial struct ConflictingMessage : IBroadcastMessage { }
""";

        GeneratorDriverRunResult result = GeneratorTestUtilities.RunDxMessageId(source);
        Diagnostic[] diagnostics = result.Results[0].Diagnostics.ToArray();

        Assert.That(
            diagnostics,
            Has.Some.Matches<Diagnostic>(d => d.Id == "DXMSG002"),
            "DXMSG002 should be reported when a message type has multiple Dx message attributes."
        );
    }

    [Test]
    public void ReportsNonPartialContainerForMessageIds()
    {
        string source = """
using DxMessaging.Core.Attributes;
using DxMessaging.Core.Messages;

namespace Sample;

public class Container
{
    [DxTargetedMessage]
    public readonly struct NestedMessage : ITargetedMessage { }
}
""";

        GeneratorDriverRunResult result = GeneratorTestUtilities.RunDxMessageId(source);
        Diagnostic[] diagnostics = result.Results[0].Diagnostics.ToArray();

        Assert.That(
            diagnostics,
            Has.Some.Matches<Diagnostic>(d => d.Id == "DXMSG003"),
            "DXMSG003 should be reported when a nested message type lives inside a non-partial container."
        );
        Assert.That(
            diagnostics,
            Has.Some.Matches<Diagnostic>(d => d.Id == "DXMSG004"),
            "DXMSG004 should suggest adding the partial keyword for the containing type."
        );
    }
}
