#if UNITY_2021_3_OR_NEWER
namespace DxMessaging.Tests.Runtime.Benchmarks
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Text.RegularExpressions;
    using UnityEngine;

    internal readonly struct BenchmarkEntry
    {
        internal BenchmarkEntry(string messageTech, long operationsPerSecond, bool allocating)
        {
            MessageTech = messageTech;
            OperationsPerSecond = operationsPerSecond;
            Allocating = allocating;
        }

        internal string MessageTech { get; }

        internal long OperationsPerSecond { get; }

        internal bool Allocating { get; }
    }

    public sealed class BenchmarkSession : IDisposable
    {
        private readonly string _sectionName;
        private readonly string _headingPrefix;
        private readonly IReadOnlyList<Func<string>> _docPathResolvers;
        private readonly List<BenchmarkEntry> _entries = new();

        internal BenchmarkSession(
            string sectionName,
            string headingPrefix,
            IReadOnlyList<Func<string>> docPathResolvers
        )
        {
            _sectionName = sectionName;
            _headingPrefix = headingPrefix;
            _docPathResolvers = docPathResolvers;

            Debug.Log("| Message Tech | Operations / Second | Allocations? |");
            Debug.Log("| ------------ | ------------------- | ------------ | ");
        }

        internal void Record(string messageTech, long operationsPerSecond, bool allocating)
        {
            string formattedOperations = operationsPerSecond.ToString(
                "N0",
                CultureInfo.InvariantCulture
            );
            Debug.Log($"| {messageTech} | {formattedOperations} | {(allocating ? "Yes" : "No")} |");
            _entries.Add(new BenchmarkEntry(messageTech, operationsPerSecond, allocating));
        }

        public void Dispose()
        {
            try
            {
                if (_entries.Count == 0)
                {
                    return;
                }

                if (string.IsNullOrEmpty(_sectionName))
                {
                    Debug.LogWarning(
                        "Skipping benchmark documentation update because the section name could not be determined."
                    );
                    return;
                }

                BenchmarkDocumentation.TryWriteBenchmarks(
                    _sectionName!,
                    _headingPrefix,
                    _entries,
                    _docPathResolvers
                );
            }
            finally
            {
                _entries.Clear();
            }
        }
    }

    internal static class BenchmarkDocumentation
    {
        private const string PerformanceHeader =
            "# Performance Benchmarks\n\n"
            + "This page is auto-updated by the Unity PlayMode benchmark tests in `Tests/Runtime/Benchmarks/PerformanceTests.cs`.\n\n"
            + "How it works:\n\n"
            + "- Run PlayMode tests locally in your Unity project that references this package.\n"
            + "- The benchmark test writes an OS-specific section below with a markdown table.\n"
            + "- CI runs skip writing to avoid noisy diffs.\n\n"
            + "See also: `Docs/DesignAndArchitecture.md#performance-optimizations` for design details.\n";

        internal static string GetOperatingSystemSection()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return "Windows";
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return "macOS";
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return "Linux";
            }

            return null;
        }

        private static bool IsRunningInContinuousIntegration()
        {
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CI")))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("JENKINS_URL")))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GITLAB_CI")))
            {
                return true;
            }

            return false;
        }

        internal static void TryWriteBenchmarks(
            string sectionName,
            string headingPrefix,
            IReadOnlyList<BenchmarkEntry> entries,
            IReadOnlyList<Func<string>> docPathResolvers
        )
        {
            if (entries.Count == 0)
            {
                return;
            }

            if (IsRunningInContinuousIntegration())
            {
                Debug.Log(
                    $"Skipping benchmarks update for {sectionName} because the benchmarks are running in CI."
                );
                return;
            }

            string docPath = ResolveDocPath(docPathResolvers);
            if (string.IsNullOrEmpty(docPath))
            {
                Debug.LogWarning(
                    $"Skipping benchmarks update for {sectionName} because no documentation target could be located."
                );
                return;
            }

            try
            {
                string table = BuildTable(entries);
                string originalContent = File.Exists(docPath)
                    ? File.ReadAllText(docPath)
                    : string.Empty;
                string updatedContent = ReplaceSection(
                    originalContent,
                    sectionName,
                    headingPrefix,
                    table
                );

                if (string.Equals(originalContent, updatedContent, StringComparison.Ordinal))
                {
                    Debug.Log(
                        $"Benchmark section for {sectionName} is already up to date in {docPath}."
                    );
                    return;
                }

                File.WriteAllText(docPath, updatedContent, new UTF8Encoding(false));
                Debug.Log($"Updated benchmarks for {sectionName} in {docPath}.");
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"Failed to update benchmark documentation for {sectionName}: {exception}"
                );
            }
        }

        internal static string TryFindPerformanceDocPath()
        {
            return TryFindDocPath(Path.Combine("Docs", "Performance.md"), PerformanceHeader);
        }

        internal static string TryFindComparisonsDocPath()
        {
            return TryFindDocPath(Path.Combine("Docs", "Comparisons.md"));
        }

        internal static string TryFindReadmePath()
        {
            return TrySearchForFile("README.md");
        }

        private static string BuildTable(IReadOnlyList<BenchmarkEntry> entries)
        {
            StringBuilder builder = new();
            builder.AppendLine("| Message Tech | Operations / Second | Allocations? |");
            builder.AppendLine("| ------------ | ------------------- | ------------ |");

            foreach (BenchmarkEntry entry in entries)
            {
                builder
                    .Append("| ")
                    .Append(entry.MessageTech)
                    .Append(" | ")
                    .Append(entry.OperationsPerSecond.ToString("N0", CultureInfo.InvariantCulture))
                    .Append(" | ")
                    .Append(entry.Allocating ? "Yes" : "No")
                    .AppendLine(" |");
            }

            return builder.ToString().TrimEnd('\r', '\n');
        }

        private static string ReplaceSection(
            string content,
            string sectionName,
            string headingPrefix,
            string tableContent
        )
        {
            string replacement = $"{headingPrefix}{sectionName}\n\n{tableContent}\n";
            int headingLevel = CountHeadingLevel(headingPrefix);
            string stopPattern = headingLevel <= 1 ? "#" : $"#{{1,{headingLevel}}}";
            string pattern =
                $@"^{Regex.Escape(headingPrefix)}{Regex.Escape(sectionName)}[^\S\r\n]*(?:\r?\n|$)[\s\S]*?(?=^\s*{stopPattern}\s|\Z)";
            Regex regex = new(pattern, RegexOptions.CultureInvariant | RegexOptions.Multiline);
            string updated = regex.Replace(content, replacement, 1);

            if (string.Equals(content, updated, StringComparison.Ordinal))
            {
                string prefix = content.EndsWith("\n", StringComparison.Ordinal)
                    ? string.Empty
                    : "\n";
                updated = $"{content}{prefix}{replacement}";
            }

            if (!updated.EndsWith("\n", StringComparison.Ordinal))
            {
                updated += "\n";
            }

            return updated;
        }

        private static int CountHeadingLevel(string headingPrefix)
        {
            int level = headingPrefix.Count(c => c == '#');
            return level > 0 ? level : 1;
        }

        private static string ResolveDocPath(IReadOnlyList<Func<string>> resolvers)
        {
            foreach (Func<string> resolver in resolvers)
            {
                if (resolver == null)
                {
                    continue;
                }

                try
                {
                    string path = resolver();
                    if (!string.IsNullOrEmpty(path))
                    {
                        return path;
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"Failed to resolve documentation path: {exception}");
                }
            }

            return null;
        }

        private static string TryFindDocPath(string relativePath, string seedContent = null)
        {
            string discovered = TrySearchForFile(relativePath);
            if (!string.IsNullOrEmpty(discovered))
            {
                return discovered;
            }

            string readmePath = TryFindReadmePath();
            if (!string.IsNullOrEmpty(readmePath))
            {
                string candidate = TryEnsureFileAdjacent(readmePath, relativePath, seedContent);
                if (!string.IsNullOrEmpty(candidate))
                {
                    return candidate;
                }
            }

            return TryEnsureFileAdjacent(
                Directory.GetCurrentDirectory(),
                relativePath,
                seedContent
            );
        }

        private static string TrySearchForFile(string relativePath)
        {
            string current = Directory.GetCurrentDirectory();
            while (!string.IsNullOrEmpty(current))
            {
                string candidate = Path.Combine(current, relativePath);
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                string packageCandidate = Path.Combine(
                    current,
                    "Packages",
                    "com.wallstop-studios.dxmessaging",
                    relativePath
                );
                if (File.Exists(packageCandidate))
                {
                    return packageCandidate;
                }

                DirectoryInfo parent = Directory.GetParent(current);
                current = parent?.FullName;
            }

            string assemblyLocation = typeof(BenchmarkDocumentation).Assembly.Location;
            if (string.IsNullOrEmpty(assemblyLocation))
            {
                return null;
            }

            string assemblyDirectory = Path.GetDirectoryName(assemblyLocation);
            if (string.IsNullOrEmpty(assemblyDirectory))
            {
                return null;
            }

            DirectoryInfo directory = new(assemblyDirectory);
            while (directory != null)
            {
                string candidate = Path.Combine(directory.FullName, relativePath);
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                string packageCandidate = Path.Combine(
                    directory.FullName,
                    "Packages",
                    "com.wallstop-studios.dxmessaging",
                    relativePath
                );
                if (File.Exists(packageCandidate))
                {
                    return packageCandidate;
                }

                directory = directory.Parent;
            }

            return null;
        }

        private static string TryEnsureFileAdjacent(
            string anchorPath,
            string relativePath,
            string seedContent
        )
        {
            string baseDirectory = File.Exists(anchorPath)
                ? Path.GetDirectoryName(anchorPath) ?? string.Empty
                : anchorPath;
            if (string.IsNullOrEmpty(baseDirectory))
            {
                return null;
            }

            string candidate = Path.Combine(baseDirectory, relativePath);
            string directory = Path.GetDirectoryName(candidate);
            try
            {
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (File.Exists(candidate))
                {
                    return candidate;
                }

                if (!string.IsNullOrEmpty(seedContent))
                {
                    File.WriteAllText(candidate, seedContent, new UTF8Encoding(false));
                }
                else
                {
                    File.WriteAllText(candidate, string.Empty, new UTF8Encoding(false));
                }

                return candidate;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"Failed to create documentation file at {candidate}: {exception}"
                );
                return null;
            }
        }
    }
}

#endif
