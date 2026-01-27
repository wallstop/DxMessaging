using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace WallstopStudios.DxMessaging.SourceGenerators.Tests;

[TestFixture]
public sealed class DocsSnippetCompilationTests
{
    private static readonly HashSet<string> IgnoredSnippetDiagnosticIds = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        "CS0106", // modifier not valid in script (partial snippets showing members only)
        "CS1001", // identifier expected (intentionally elided samples)
        "CS8803", // top-level statements mixed with declarations (visual guide style snippets)
    };

    [Test]
    public void QuickStartStep1Compiles()
    {
        string docsRoot = ResolveDocsRoot();
        string quickStartPath = Path.Combine(docsRoot, "getting-started", "quick-start.md");
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

    [TestCaseSource(nameof(GetDocumentationSnippets))]
    public void DocumentationSnippetsCompile(string markdownPath, string snippet)
    {
        Assert.That(
            snippet,
            Is.Not.Empty,
            $"Snippet extracted from {markdownPath} should not be empty."
        );

        var diagnostics = GeneratorTestUtilities
            .ParseSnippet(snippet)
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .ToArray();

        var actionableDiagnostics = diagnostics
            .Where(d => !IgnoredSnippetDiagnosticIds.Contains(d.Id))
            .ToArray();

        if (actionableDiagnostics.Length > 0)
        {
            string message = string.Join(
                System.Environment.NewLine,
                actionableDiagnostics.Select(d => d.ToString())
            );
            Assert.Fail(
                $"Documentation snippet in {markdownPath} failed to compile:{System.Environment.NewLine}{message}"
            );
        }
    }

    private static IEnumerable<TestCaseData> GetDocumentationSnippets()
    {
        string docsRoot = ResolveDocsRoot();
        foreach (
            string markdownPath in Directory.GetFiles(docsRoot, "*.md", SearchOption.AllDirectories)
        )
        {
            foreach (string snippet in ExtractCodeBlocks(markdownPath, "csharp"))
            {
                if (ShouldSkipSnippet(snippet))
                {
                    continue;
                }

                yield return new TestCaseData(markdownPath, snippet).SetName(
                    $"{Path.GetFileName(markdownPath)} compiles"
                );
            }
        }
    }

    private static string ExtractFirstCodeBlock(string markdownPath, string infoString)
    {
        return ExtractCodeBlocks(markdownPath, infoString).FirstOrDefault() ?? string.Empty;
    }

    private static IEnumerable<string> ExtractCodeBlocks(string markdownPath, string infoString)
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
                    builder.Clear();
                }
                continue;
            }

            if (line.StartsWith("```"))
            {
                inBlock = false;
                string snippet = builder.ToString();
                if (!string.IsNullOrWhiteSpace(snippet))
                {
                    yield return snippet;
                }
                continue;
            }

            builder.AppendLine(rawLine);
        }
    }

    private static string ResolveDocsRoot()
    {
        string currentDirectoryPath = TestContext.CurrentContext.TestDirectory;
        while (!string.IsNullOrEmpty(currentDirectoryPath))
        {
            string docsDirectory = Path.Combine(currentDirectoryPath, "docs");
            string candidate = Path.Combine(docsDirectory, "getting-started", "quick-start.md");
            if (File.Exists(candidate))
            {
                return docsDirectory;
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
            "Unable to locate docs/getting-started/quick-start.md from the current test directory."
        );
    }

    [TestCase("", true, TestName = "Empty snippet should be skipped")]
    [TestCase("   ", true, TestName = "Whitespace-only snippet should be skipped")]
    [TestCase(
        "var x = 1;\nConsole.WriteLine(x);",
        false,
        TestName = "Regular compilable code should not be skipped"
    )]
    [TestCase(
        "public class MyClass { }",
        false,
        TestName = "Simple class declaration should not be skipped"
    )]
    [TestCase(
        "// Comment only\nvar x = 1;",
        false,
        TestName = "Code with comments should not be skipped"
    )]
    [TestCase(
        "MessageRegistrationHandle RegisterUntargeted<T>(Action<T> handler, int priority = 0)",
        true,
        TestName = "Single line method signature with default parameter should be skipped"
    )]
    [TestCase(
        "void Process(string name = null)",
        true,
        TestName = "Method with null default should be skipped"
    )]
    [TestCase(
        "bool IsEnabled(bool flag = false)",
        true,
        TestName = "Method with false default should be skipped"
    )]
    [TestCase(
        "void Toggle(bool active = true)",
        true,
        TestName = "Method with true default should be skipped"
    )]
    [TestCase(
        "MessageRegistrationHandle RegisterUntargeted<T>(\n    Action<T> handler,\n    int priority = 0)",
        true,
        TestName = "Multi-line signature with default params should be skipped"
    )]
    [TestCase(
        "Do something...\nthen continue",
        true,
        TestName = "Snippet with ellipsis should be skipped"
    )]
    [TestCase(
        "public void Method() { ... }",
        true,
        TestName = "Method with ellipsis body should be skipped"
    )]
    [TestCase(
        "MessageRegistrationHandle RegisterUntargeted<T>(\n    Action<T> handler,\n    int priority = 0\n)",
        true,
        TestName = "Multi-line signature with closing paren on own line should be skipped"
    )]
    [TestCase(
        "    // GameObject target\n    MessageRegistrationHandle RegisterGameObjectTargeted<T>(\n        GameObject target,\n        Action<T> handler,\n        int priority = 0\n    )",
        true,
        TestName = "Indented multi-line signature with comments should be skipped"
    )]
    [TestCase(
        "// Emit to specific target (by InstanceId)\nmessage.EmitTargeted(InstanceId target);",
        true,
        TestName = "Method call with parameter type documentation should be skipped"
    )]
    [TestCase(
        "Action RegisterUntargetedInterceptor<T>(\n    UntargetedInterceptor<T> interceptor,\n    int priority = 0\n)",
        true,
        TestName = "Interceptor signature should be skipped"
    )]
    public void ShouldSkipSnippetDetectsApiSignatures(string snippet, bool expectedSkip)
    {
        bool actualSkip = ShouldSkipSnippet(snippet);
        Assert.That(
            actualSkip,
            Is.EqualTo(expectedSkip),
            $"Expected ShouldSkipSnippet to return {expectedSkip} for snippet: '{snippet.Replace("\n", "\\n")}'"
        );
    }

    private static bool ShouldSkipSnippet(string snippet)
    {
        if (string.IsNullOrWhiteSpace(snippet))
        {
            return true;
        }

        if (snippet.Contains("..."))
        {
            return true;
        }

        foreach (char c in snippet)
        {
            if (c > 127 && !char.IsWhiteSpace(c))
            {
                return true;
            }
        }

        if (IsApiSignatureDocumentation(snippet))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Detects whether a code snippet represents API signature documentation rather than
    /// compilable sample code. API signatures in documentation (e.g., method signatures
    /// with default parameters shown for reference) are not meant to be compiled.
    /// </summary>
    /// <remarks>
    /// <para>
    /// API signature snippets need special handling because documentation often shows
    /// method signatures like <c>RegisterUntargeted&lt;T&gt;(Action&lt;T&gt; handler, int priority = 0)</c>
    /// for reference. These are informational and not valid standalone C# code.
    /// </para>
    /// <para>
    /// Detection heuristics:
    /// <list type="bullet">
    /// <item><description>Default parameter patterns on same line: <c>= 0)</c>, <c>= null)</c>, <c>= false)</c>, <c>= true)</c> and their comma variants</description></item>
    /// <item><description>Default parameter patterns at end of line: <c>= 0</c>, <c>= null</c>, <c>= false</c>, <c>= true</c> for multi-line signatures</description></item>
    /// <item><description>Method signature start patterns: return type followed by method name and opening paren</description></item>
    /// <item><description>Partial signature patterns: lines ending in comma with parameter type keywords (Action&lt;&gt;, Func&lt;&gt;, int, bool, string, GameObject, Component, InstanceId, FastHandler)</description></item>
    /// <item><description>Isolated closing parens: lines that are just <c>)</c> or <c>);</c></description></item>
    /// <item><description>Documentation-style method calls: <c>method(TypeName param);</c> showing parameter types as placeholders</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// Ratio threshold logic: A snippet is considered an API signature if:
    /// <list type="bullet">
    /// <item><description>At least 30% of non-empty lines match signature patterns, OR</description></item>
    /// <item><description>Two or more lines match signature patterns (handles multi-line signatures)</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <param name="snippet">The code snippet to analyze.</param>
    /// <returns>True if the snippet appears to be API signature documentation; otherwise false.</returns>
    private static bool IsApiSignatureDocumentation(string snippet)
    {
        string[] lines = snippet.Split('\n');
        int signatureLineCount = 0;
        int totalNonEmptyLines = 0;

        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();
            if (string.IsNullOrEmpty(line))
            {
                continue;
            }

            totalNonEmptyLines++;

            // Default parameters on same line as closing paren or comma
            bool hasDefaultParameterSameLine =
                line.Contains("= 0)")
                || line.Contains("= null)")
                || line.Contains("= false)")
                || line.Contains("= true)")
                || line.Contains("= 0,")
                || line.Contains("= null,")
                || line.Contains("= false,")
                || line.Contains("= true,");

            // Default parameters on their own line (multi-line signatures)
            bool hasDefaultParameterEndOfLine =
                line.EndsWith("= 0")
                || line.EndsWith("= null")
                || line.EndsWith("= false")
                || line.EndsWith("= true");

            // Method signature that starts with return type + method name pattern
            // e.g., "MessageRegistrationHandle RegisterUntargeted<T>("
            bool isMethodSignatureStart =
                !line.StartsWith("//")
                && !line.StartsWith("/*")
                && !line.StartsWith("var ")
                && !line.Contains(" = ")
                && line.EndsWith("(")
                && System.Text.RegularExpressions.Regex.IsMatch(
                    line,
                    @"^\w+(?:<[^>]+>)?\s+\w+(?:<[^>]+>)?\s*\($"
                );

            bool isPartialSignature =
                line.EndsWith(",")
                && !line.StartsWith("//")
                && (
                    line.Contains("Action<")
                    || line.Contains("Func<")
                    || line.Contains("int ")
                    || line.Contains("bool ")
                    || line.Contains("string ")
                    || line.Contains("GameObject ")
                    || line.Contains("Component ")
                    || line.Contains("InstanceId ")
                    || line.Contains("FastHandler")
                );

            // Isolated closing paren (multi-line signature endings)
            bool isIsolatedClosingParen = line == ")" || line == ");";

            // Method calls with type+parameter documentation style
            // e.g., "message.EmitTargeted(InstanceId target);"
            bool isDocumentationStyleMethodCall =
                !line.StartsWith("//")
                && !line.StartsWith("/*")
                && (line.EndsWith(");") || line.EndsWith(")"))
                && line.Contains("(")
                && System.Text.RegularExpressions.Regex.IsMatch(
                    line,
                    @"\(\s*[A-Z]\w*\s+\w+\s*\)[\s;]*$"
                );

            if (
                hasDefaultParameterSameLine
                || hasDefaultParameterEndOfLine
                || isMethodSignatureStart
                || isPartialSignature
                || isIsolatedClosingParen
                || isDocumentationStyleMethodCall
            )
            {
                signatureLineCount++;
            }
        }

        if (signatureLineCount > 0 && totalNonEmptyLines > 0)
        {
            double ratio = (double)signatureLineCount / totalNonEmptyLines;
            if (ratio >= 0.3 || signatureLineCount >= 2)
            {
                return true;
            }
        }

        return false;
    }
}
