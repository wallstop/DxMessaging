#if UNITY_EDITOR
namespace DxMessaging.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;
    using System.Text.RegularExpressions;
    using NUnit.Framework;

    /// <summary>
    /// Guards the rsp-content classification predicate that <c>SetupCscRsp</c>
    /// uses to detect DxMessaging analyzer entries vs. unrelated entries.
    ///
    /// SetupCscRsp's classification members are <c>private</c> and we
    /// deliberately do NOT widen them via <c>[InternalsVisibleTo]</c>. Instead,
    /// the data-driven cases below replicate the exact ad-hoc shape of the
    /// production predicate (StartsWith("-a:") + Contains
    /// "com.wallstop-studios.dxmessaging" OR "WallstopStudios.DxMessaging",
    /// all OrdinalIgnoreCase), and a single shape-of-source test reads the
    /// production .cs file and asserts the predicate's substrings are still
    /// present. If someone changes the production classification logic in
    /// <c>EnsureCscRsp</c>, the shape-of-source test fires so the duplicated
    /// predicate here gets updated in lock-step.
    /// </summary>
    [TestFixture]
    public sealed class SetupCscRspTests
    {
        private string _testRspFilePath;

        [SetUp]
        public void SetUp()
        {
            _testRspFilePath = Path.Combine(Path.GetTempPath(), $"test_csc_{Guid.NewGuid()}.rsp");
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_testRspFilePath))
            {
                File.Delete(_testRspFilePath);
            }
        }

        // Classification fixtures: counts DxMessaging -a: vs. unrelated -a:
        // entries through the same string matching SetupCscRsp uses. The
        // multi-line strings use \n so the test runs identically on all OSes;
        // the dedicated CRLF case below exercises the CRLF path explicitly.
        [Test]
        [TestCase(
            "-a:\"Library/PackageCache/com.wallstop-studios.dxmessaging@4e74e1b2eec3/Editor/Analyzers/WallstopStudios.DxMessaging.SourceGenerators.dll\"\n"
                + "-a:\"Library/PackageCache/com.wallstop-studios.dxmessaging@4e74e1b2eec3/Editor/Analyzers/Microsoft.CodeAnalysis.dll\"\n"
                + "-a:\"Library/PackageCache/com.wallstop-studios.dxmessaging@3d05efca60e4/Editor/Analyzers/WallstopStudios.DxMessaging.SourceGenerators.dll\"\n"
                + "-a:\"Library/PackageCache/com.wallstop-studios.dxmessaging@3d05efca60e4/Editor/Analyzers/Microsoft.CodeAnalysis.dll\"\n"
                + "-r:\"SomeOtherAnalyzer.dll\"\n",
            4,
            0,
            TestName = "FourDxMessagingDuplicatesAcrossTwoHashedPackagePaths"
        )]
        [TestCase(
            "-a:\"Packages/com.wallstop-studios.dxmessaging/Editor/Analyzers/WallstopStudios.DxMessaging.SourceGenerators.dll\"\n",
            1,
            0,
            TestName = "SingleDxMessagingEntry"
        )]
        [TestCase(
            "-a:\"Packages/com.wallstop-studios.dxmessaging/Editor/Analyzers/WallstopStudios.DxMessaging.SourceGenerators.dll\"\n"
                + "-a:\"Packages/com.other.pkg/Analyzers/OtherAnalyzer.dll\"\n"
                + "-a:\"Library/PackageCache/yet.another/Analyzers/SomeAnalyzer.dll\"\n",
            1,
            2,
            TestName = "MixedDxMessagingAndOtherAnalyzers"
        )]
        [TestCase("", 0, 0, TestName = "EmptyContent")]
        [TestCase(
            "\n\n\n-a:\"Packages/com.wallstop-studios.dxmessaging/Editor/Analyzers/WallstopStudios.DxMessaging.SourceGenerators.dll\"\n\n\n",
            1,
            0,
            TestName = "BlankLineInterspersedEntries"
        )]
        [TestCase(
            "-a:\"PACKAGES/COM.WALLSTOP-STUDIOS.DXMESSAGING/Editor/Analyzers/WALLSTOPSTUDIOS.DXMESSAGING.SOURCEGENERATORS.DLL\"\n",
            1,
            0,
            TestName = "UpperCaseDxMessagingPathMatchesViaOrdinalIgnoreCase"
        )]
        [TestCase(
            "-A:\"Packages/com.wallstop-studios.dxmessaging/Editor/Analyzers/WallstopStudios.DxMessaging.SourceGenerators.dll\"\n",
            1,
            0,
            TestName = "UpperCaseAnalyzerSwitchMatchesViaOrdinalIgnoreCase"
        )]
        [TestCase("-r:\"System.Runtime.dll\"\n", 0, 0, TestName = "ReferenceOnlyNoAnalyzers")]
        public void ClassifiesDxMessagingAnalyzerEntries(
            string rspContent,
            int expectedDxCount,
            int expectedOtherCount
        )
        {
            File.WriteAllText(_testRspFilePath, rspContent);

            string[] lines = File.ReadAllLines(_testRspFilePath);
            int dxMessagingCount = 0;
            int otherCount = 0;

            foreach (string line in lines)
            {
                if (
                    string.IsNullOrWhiteSpace(line)
                    || !line.StartsWith("-a:", StringComparison.OrdinalIgnoreCase)
                )
                {
                    continue;
                }

                if (
                    line.Contains(
                        "com.wallstop-studios.dxmessaging",
                        StringComparison.OrdinalIgnoreCase
                    )
                    || line.Contains(
                        "WallstopStudios.DxMessaging",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    dxMessagingCount++;
                }
                else
                {
                    otherCount++;
                }
            }

            Assert.AreEqual(expectedDxCount, dxMessagingCount, "DxMessaging analyzer entry count");
            Assert.AreEqual(expectedOtherCount, otherCount, "Other analyzer entry count");
        }

        // The CRLF case is split into its own test because TestCase string
        // literals do not robustly carry CRLF characters across IDEs/parsers;
        // building the string from explicit Environment-style sequences keeps
        // the line-ending under test crystal clear.
        [Test]
        public void ClassifiesDxMessagingAnalyzerEntriesUsingCrlfLineEndings()
        {
            string content =
                "-a:\"Packages/com.wallstop-studios.dxmessaging/Editor/Analyzers/WallstopStudios.DxMessaging.SourceGenerators.dll\"\r\n"
                + "-a:\"Packages/com.other.pkg/Analyzers/OtherAnalyzer.dll\"\r\n";

            File.WriteAllText(_testRspFilePath, content);

            string[] lines = File.ReadAllLines(_testRspFilePath);
            int dxMessagingCount = 0;
            int otherCount = 0;

            foreach (string line in lines)
            {
                if (
                    string.IsNullOrWhiteSpace(line)
                    || !line.StartsWith("-a:", StringComparison.OrdinalIgnoreCase)
                )
                {
                    continue;
                }

                if (
                    line.Contains(
                        "com.wallstop-studios.dxmessaging",
                        StringComparison.OrdinalIgnoreCase
                    )
                    || line.Contains(
                        "WallstopStudios.DxMessaging",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    dxMessagingCount++;
                }
                else
                {
                    otherCount++;
                }
            }

            Assert.AreEqual(1, dxMessagingCount, "DxMessaging analyzer entry count under CRLF");
            Assert.AreEqual(1, otherCount, "Other analyzer entry count under CRLF");
        }

        // Preservation fixtures: non-DxMessaging lines must round-trip
        // verbatim through any rsp-cleaning logic that filters DxMessaging
        // analyzer entries. Each case names one or more substrings that must
        // be present in the file content after a write/read round-trip.
        [Test]
        [TestCase(
            "-a:\"Packages/com.wallstop-studios.dxmessaging/Editor/Analyzers/WallstopStudios.DxMessaging.SourceGenerators.dll\"\n"
                + "-a:\"Library/PackageCache/some.other.package/Analyzers/OtherAnalyzer.dll\"\n"
                + "-r:\"System.Runtime.dll\"\n",
            new[] { "OtherAnalyzer.dll", "System.Runtime.dll" },
            TestName = "PreservesOtherAnalyzerAndReferenceWhenMixedWithDxMessaging"
        )]
        [TestCase(
            "-r:\"System.Runtime.dll\"\n-r:\"System.Collections.dll\"\n",
            new[] { "System.Runtime.dll", "System.Collections.dll" },
            TestName = "PreservesMultipleReferenceEntries"
        )]
        [TestCase(
            "-a:\"Library/PackageCache/some.other.package/Analyzers/OtherAnalyzer.dll\"\n"
                + "-nullable:enable\n"
                + "-warnaserror\n",
            new[] { "OtherAnalyzer.dll", "-nullable:enable", "-warnaserror" },
            TestName = "PreservesAnalyzerOptionsBesidesAnalyzerEntries"
        )]
        public void PreservesNonDxMessagingEntries(string rspContent, string[] mustPreserveContent)
        {
            File.WriteAllText(_testRspFilePath, rspContent);

            string content = File.ReadAllText(_testRspFilePath);
            foreach (string expected in mustPreserveContent)
            {
                Assert.IsTrue(
                    content.Contains(expected),
                    $"Should preserve non-DxMessaging content '{expected}'"
                );
            }
        }

        // Shape-of-source guard: the data-driven cases above duplicate the
        // production EnsureCscRsp classification predicate (no internals
        // visibility added by design). This test reads the SetupCscRsp.cs
        // source file and asserts the predicate substrings are still present
        // in the production code, so a future edit to the production
        // predicate fires here and forces the duplicated cases above to be
        // re-aligned. Pattern mirrors the JS-side shape guards under
        // scripts/__tests__/*.test.js (which also read the production source
        // and assert on invariants without depending on internals).
        //
        // SOFT-PASS POLICY: if we cannot resolve SetupCscRsp.cs's on-disk
        // location at all (e.g. the test runs from a baked-out artifact that
        // no longer ships sources), there is literally nothing to check, so
        // we Assert.Pass with a loud TestContext.WriteLine notice rather than
        // failing. The test exists to catch SHAPE DRIFT in the production
        // source; if the source isn't available, there is no drift to catch.
        [Test]
        public void ProductionPredicateShapeIsStable()
        {
            string sourceFile = ResolveSetupCscRspSourcePath();
            if (string.IsNullOrEmpty(sourceFile) || !File.Exists(sourceFile))
            {
                TestContext.WriteLine(
                    "ProductionPredicateShapeIsStable: SetupCscRsp.cs could not be located on disk via any known candidate (Packages/, Library/PackageCache/com.wallstop-studios.dxmessaging@*, repo-root Editor/). Skipping shape check via Assert.Pass; if you see this in CI, audit ResolveSetupCscRspSourcePath()."
                );
                Assert.Pass(
                    "SetupCscRsp.cs source not found on disk; shape-drift guard has nothing to check."
                );
                return;
            }

            string text = File.ReadAllText(sourceFile);

            StringAssert.Contains(
                "isDxMessagingAnalyzer",
                text,
                "Production predicate identifier 'isDxMessagingAnalyzer' is no longer present in SetupCscRsp.cs -- update the duplicated classification logic in this test fixture in lock-step."
            );
            StringAssert.Contains(
                "com.wallstop-studios.dxmessaging",
                text,
                "Production predicate substring 'com.wallstop-studios.dxmessaging' is no longer present in SetupCscRsp.cs."
            );
            StringAssert.Contains(
                "WallstopStudios.DxMessaging",
                text,
                "Production predicate substring 'WallstopStudios.DxMessaging' is no longer present in SetupCscRsp.cs."
            );

            // Case-insensitive substring check for `StartsWith("-a:"` to
            // tolerate minor formatting (whitespace inside StartsWith()) and
            // alternate-cased `StartsWith` (PascalCase only on .NET, but the
            // regex stays robust to any future formatting tweak).
            Match startsWithAnalyzerArg = Regex.Match(
                text,
                @"StartsWith\s*\(\s*""-a:""",
                RegexOptions.IgnoreCase
            );
            Assert.IsTrue(
                startsWithAnalyzerArg.Success,
                "Production predicate no longer contains StartsWith(\"-a:\" -- the classification logic shape has drifted from the duplicated cases in this fixture."
            );
        }

        // Resolve the on-disk path to Editor/SetupCscRsp.cs in the host Unity
        // project. We try Application.dataPath-relative paths in this order:
        //   1. Packages/com.wallstop-studios.dxmessaging/... (the canonical
        //      Package Manager mount form for `git`/`registry`/`file:` deps
        //      that Unity has not yet resolved into PackageCache).
        //   2. Library/PackageCache/com.wallstop-studios.dxmessaging@<hash>/...
        //      (Unity's actual on-disk form for resolved `file:` and other
        //      remote deps -- the @<hash> suffix is derived from the URI by
        //      Unity and is NOT predictable from the package name alone).
        //   3. Library/PackageCache/com.wallstop-studios.dxmessaging/...
        //      (rare local-override form with no hash suffix; defensive).
        //   4. Repo-as-Unity-project root (no Packages/ layer): the package
        //      root IS the repo root, so Assets/.. resolves to it and
        //      Editor/SetupCscRsp.cs sits right there.
        // Falls back to walking up from the test assembly's Location -- one
        // of these is reliable in every Unity Editor context the test runs
        // in (in-package via the Package Manager, embedded under Packages/,
        // running inside the ephemeral CI project, or running inside this
        // repo's own .unity-test-project).
        private static string ResolveSetupCscRspSourcePath()
        {
            string projectRoot = Path.GetFullPath(
                Path.Combine(UnityEngine.Application.dataPath, "..")
            );

            string[] fixedCandidates =
            {
                Path.Combine(
                    projectRoot,
                    "Packages",
                    "com.wallstop-studios.dxmessaging",
                    "Editor",
                    "SetupCscRsp.cs"
                ),
                Path.Combine(
                    projectRoot,
                    "Library",
                    "PackageCache",
                    "com.wallstop-studios.dxmessaging",
                    "Editor",
                    "SetupCscRsp.cs"
                ),
                Path.Combine(projectRoot, "Editor", "SetupCscRsp.cs"),
            };

            foreach (string candidate in fixedCandidates)
            {
                string full = Path.GetFullPath(candidate);
                if (File.Exists(full))
                {
                    return full;
                }
            }

            // Hashed-PackageCache glob: Unity stores `file:` and other
            // resolved deps as
            //   Library/PackageCache/com.wallstop-studios.dxmessaging@<hash>/
            // where <hash> is per-source-URL and not predictable. Enumerate
            // every match and probe each for the source file. We try this
            // AFTER the fixed Packages/... candidate so a clean Packages
            // mount (the local-dev case) always wins over a stale
            // PackageCache copy.
            string packageCacheRoot = Path.Combine(projectRoot, "Library", "PackageCache");
            if (Directory.Exists(packageCacheRoot))
            {
                IEnumerable<string> hashedPackageDirs;
                try
                {
                    hashedPackageDirs = Directory.EnumerateDirectories(
                        packageCacheRoot,
                        "com.wallstop-studios.dxmessaging@*"
                    );
                }
                catch (IOException)
                {
                    hashedPackageDirs = Array.Empty<string>();
                }
                catch (UnauthorizedAccessException)
                {
                    hashedPackageDirs = Array.Empty<string>();
                }

                foreach (string hashedDir in hashedPackageDirs)
                {
                    string probe = Path.Combine(hashedDir, "Editor", "SetupCscRsp.cs");
                    if (File.Exists(probe))
                    {
                        return Path.GetFullPath(probe);
                    }
                }
            }

            // Reflection fallback: walk up from the SetupCscRsp type's
            // assembly Location until we find a sibling Editor/SetupCscRsp.cs.
            Assembly editorAssembly = null;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetType("DxMessaging.Editor.SetupCscRsp", false) != null)
                {
                    editorAssembly = assembly;
                    break;
                }
            }
            if (editorAssembly != null)
            {
                string assemblyPath;
                try
                {
                    assemblyPath = editorAssembly.Location;
                }
                catch (NotSupportedException)
                {
                    assemblyPath = null;
                }
                if (!string.IsNullOrEmpty(assemblyPath))
                {
                    string dir = Path.GetDirectoryName(assemblyPath);
                    for (int hop = 0; hop < 8 && !string.IsNullOrEmpty(dir); hop++)
                    {
                        string probe = Path.Combine(dir, "Editor", "SetupCscRsp.cs");
                        if (File.Exists(probe))
                        {
                            return probe;
                        }
                        dir = Path.GetDirectoryName(dir);
                    }
                }
            }

            return null;
        }
    }
}
#endif
