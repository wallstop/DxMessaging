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
        string quickStartPath = ResolveQuickStartPath();
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

    private static string ResolveQuickStartPath()
    {
        string currentDirectoryPath = TestContext.CurrentContext.TestDirectory;
        while (!string.IsNullOrEmpty(currentDirectoryPath))
        {
            string docsDirectory = Path.Combine(currentDirectoryPath, "Docs");
            string candidate = Path.Combine(docsDirectory, "QuickStart.md");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            string parentDirectoryPath =
                Path.GetDirectoryName(currentDirectoryPath) ?? string.Empty;
            if (string.IsNullOrEmpty(parentDirectoryPath))
            {
                break;
            }

            currentDirectoryPath = parentDirectoryPath;
        }

        throw new FileNotFoundException(
            "Unable to locate Docs/QuickStart.md from the current test directory."
        );
    }
}
