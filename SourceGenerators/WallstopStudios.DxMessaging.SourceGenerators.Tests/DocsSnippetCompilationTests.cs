using System.IO;
using System.Linq;
using NUnit.Framework;

namespace WallstopStudios.DxMessaging.SourceGenerators.Tests;

[TestFixture]
public sealed class DocsSnippetCompilationTests
{
    [Test]
    public void QuickStartStep1Compiles()
    {
        string docsRoot = Path.GetFullPath(
            Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "..",
                "..",
                "..",
                "..",
                "..",
                "Docs"
            )
        );
        string quickStartPath = Path.Combine(docsRoot, "QuickStart.md");
        Assert.That(File.Exists(quickStartPath), Is.True, $"Unable to locate {quickStartPath}.");

        string snippet = ExtractFirstCodeBlock(quickStartPath, "csharp");
        Assert.That(!string.IsNullOrWhiteSpace(snippet), Is.True, "QuickStart snippet not found.");

        string source = $"""
using DxMessaging.Core.Messages;
using DxMessaging.Core.Attributes;
using UnityEngine;

{snippet}
""";

        var diagnostics = GeneratorTestUtilities
            .CompileSnippet(source)
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .ToArray();

        if (diagnostics.Length > 0)
        {
            string message = string.Join(
                System.Environment.NewLine,
                diagnostics.Select(d => d.ToString())
            );
            Assert.Fail(
                $"QuickStart snippet failed to compile:{System.Environment.NewLine}{message}"
            );
        }
    }

    private static string ExtractFirstCodeBlock(string markdownPath, string infoString)
    {
        string[] lines = File.ReadAllLines(markdownPath);
        bool inBlock = false;
        System.Text.StringBuilder builder = new();
        foreach (string rawLine in lines)
        {
            string line = rawLine.TrimEnd();
            if (!inBlock)
            {
                if (line.StartsWith("```") && line.Length > 3 && line[3..].StartsWith(infoString))
                {
                    inBlock = true;
                }
                continue;
            }

            if (line.StartsWith("```"))
            {
                break;
            }

            builder.AppendLine(rawLine);
        }

        return builder.ToString();
    }
}
