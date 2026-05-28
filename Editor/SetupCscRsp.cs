#if UNITY_EDITOR

namespace DxMessaging.Editor
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using DxMessaging.Editor.Settings;
    using UnityEditor;
    using UnityEngine;

    [InitializeOnLoad]
    public static class SetupCscRsp
    {
        private static readonly string RspFilePath = Path.Combine(
                Application.dataPath,
                "..",
                "csc.rsp"
            )
            .Replace("\\", "/");

        // The package ships both the analyzer + source-generator DLLs and their
        // Roslyn runtime deps from this directory. csc consumes them via the
        // generated -a:"..." entries in csc.rsp; the asset-label-driven
        // RoslynAnalyzer pipeline consumes them via their .meta labels (the
        // shipped .meta files carry the label directly -- this script no longer
        // copies the DLLs into Assets/ to dodge a Unity 2021 duplicate-precompiled-
        // assembly error).
        //
        // The static entry covers the common cases that route the package through
        // the virtualized `Packages/` namespace: `file:` references, `git:`
        // references, the registry (UPM proper), and an in-tree embedded
        // checkout. The rare case where Unity's UPM cache resolution surfaces the
        // package only under `Library/PackageCache/<id>@<hash>/...` is handled
        // dynamically by <see cref="EnumerateResolvedAnalyzerDirectories"/>, which
        // probes for the HASHED PackageCache directory name (the non-hashed form
        // `Library/PackageCache/com.wallstop-studios.dxmessaging/...` does NOT
        // exist on disk -- Unity always appends the @<lockHash> suffix).
        private static readonly string[] AnalyzerDirectories =
        {
            "Packages/com.wallstop-studios.dxmessaging/Editor/Analyzers/",
        };

        // Package identifier used to discover the HASHED UPM PackageCache
        // directory at runtime (Unity rewrites the source to
        // `Library/PackageCache/<id>@<lockHash>/...`, where <lockHash> changes
        // when the package's resolved version/tarball changes -- so we cannot
        // hardcode the directory name and must enumerate it instead).
        private const string PackageId = "com.wallstop-studios.dxmessaging";

        // Marker file that signals the one-shot legacy-copy cleanup
        // (CleanUpLegacyAnalyzerCopyPath) has already run for this project.
        // Lives under Library/ (which Unity treats as machine-local, NOT
        // version-controlled) so the cleanup runs once per workspace and never
        // re-runs on subsequent editor loads.
        private static readonly string LegacyCleanupMarkerPath = Path.Combine(
                Application.dataPath,
                "..",
                "Library",
                ".dxm-cleanup-done"
            )
            .Replace("\\", "/");

        // Asset path Unity used to consume when SetupCscRsp auto-copied the
        // analyzer DLLs out of Editor/Analyzers/ on editor load. The auto-copy
        // is gone (the shipped .dll.meta carries the RoslynAnalyzer label
        // directly), but projects upgrading past that era may still have the
        // duplicated DLLs sitting under this path and would hit Unity 2021's
        // duplicate-precompiled-assembly error until they are removed.
        private const string LegacyAnalyzerCopyAssetDir =
            "Assets/Plugins/Editor/WallstopStudios.DxMessaging";

        /// <summary>
        /// Canonical list of analyzer DLL filenames the package may ship in
        /// <c>Editor/Analyzers/</c>. Used by <see cref="GetAnalyzerArguments"/>
        /// (to build <c>-a:</c> entries when the file is present on disk) and
        /// by <see cref="CleanUpLegacyAnalyzerCopyPath"/> (to identify which
        /// stale copies under <see cref="LegacyAnalyzerCopyAssetDir"/> belong
        /// to the package and may be safely deleted). The list intentionally
        /// references a few transitive Roslyn deps that may or may not
        /// physically ship with the package; consumers silently skip any name
        /// that isn't on disk.
        /// </summary>
        private static readonly string[] AnalyzerDllRoster =
        {
            "WallstopStudios.DxMessaging.SourceGenerators.dll",
            "WallstopStudios.DxMessaging.Analyzer.dll",
            "Microsoft.CodeAnalysis.dll",
            "Microsoft.CodeAnalysis.CSharp.dll",
            "System.Text.Encodings.Web.dll",
            "System.Reflection.Metadata.dll",
            "System.Runtime.CompilerServices.Unsafe.dll",
            "System.Collections.Immutable.dll",
            "System.Memory.dll",
            "System.Buffers.dll",
            "System.Threading.Tasks.Extensions.dll",
            "System.Numerics.Vectors.dll",
            "System.Text.Encoding.CodePages.dll",
        };

        static SetupCscRsp()
        {
            EditorApplication.delayCall += EnsureCscRsp;
            EditorApplication.delayCall += EnsureAdditionalFileForIgnoreList;
            EditorApplication.delayCall += CleanUpLegacyAnalyzerCopyPath;
        }

        /// <summary>
        /// Synchronizes <c>csc.rsp</c> with the current set of analyzer arguments derived from the
        /// on-disk DLL roster.
        /// </summary>
        private static void EnsureCscRsp()
        {
            try
            {
                if (!File.Exists(RspFilePath))
                {
                    File.WriteAllText(RspFilePath, string.Empty);
                    AssetDatabase.ImportAsset("csc.rsp");
                }

                string rspContent = File.ReadAllText(RspFilePath);

                // Get current valid analyzer arguments
                HashSet<string> currentAnalyzerArgs = new(
                    GetAnalyzerArguments(),
                    StringComparer.OrdinalIgnoreCase
                );

                // Parse existing lines and filter out stale DxMessaging analyzer entries
                List<string> newLines = new();
                bool foundStaleEntries = false;

                foreach (
                    string line in rspContent.Split(
                        new[] { '\r', '\n' },
                        StringSplitOptions.RemoveEmptyEntries
                    )
                )
                {
                    string trimmedLine = line.Trim();

                    // Check if this is a DxMessaging analyzer line
                    bool isDxMessagingAnalyzer =
                        trimmedLine.StartsWith("-a:", StringComparison.OrdinalIgnoreCase)
                        && (
                            trimmedLine.Contains(
                                "com.wallstop-studios.dxmessaging",
                                StringComparison.OrdinalIgnoreCase
                            )
                            || trimmedLine.Contains(
                                "WallstopStudios.DxMessaging",
                                StringComparison.OrdinalIgnoreCase
                            )
                        );

                    if (isDxMessagingAnalyzer)
                    {
                        // Only keep if it's in the current valid set
                        if (currentAnalyzerArgs.Contains(trimmedLine))
                        {
                            newLines.Add(trimmedLine);
                        }
                        else
                        {
                            foundStaleEntries = true;
                        }
                    }
                    else
                    {
                        // Keep all non-DxMessaging lines as-is
                        newLines.Add(trimmedLine);
                    }
                }

                // Add any new analyzer arguments that aren't already present
                bool foundNewEntries = false;
                foreach (string analyzerArgument in currentAnalyzerArgs)
                {
                    if (!newLines.Contains(analyzerArgument, StringComparer.OrdinalIgnoreCase))
                    {
                        newLines.Add(analyzerArgument);
                        foundNewEntries = true;
                    }
                }

                bool modified = foundStaleEntries || foundNewEntries;

                if (modified)
                {
                    // Write the cleaned up content
                    string newContent = string.Join(Environment.NewLine, newLines);
                    if (!string.IsNullOrEmpty(newContent))
                    {
                        newContent += Environment.NewLine;
                    }
                    File.WriteAllText(RspFilePath, newContent);
                    AssetDatabase.ImportAsset("csc.rsp");
                    Debug.Log("Updated csc.rsp.");
                }
            }
            catch (IOException ex)
            {
                Debug.LogError($"Failed to modify csc.rsp: {ex}");
            }
        }

        /// <summary>
        /// Ensures <c>csc.rsp</c> contains a single <c>-additionalfile:</c> line pointing at the
        /// base-call ignore sidecar, when (and only when) that sidecar physically exists. Stale
        /// entries pointing at moved or deleted sidecar paths are removed.
        /// </summary>
        /// <remarks>
        /// The sidecar is generated by <see cref="DxMessagingBaseCallIgnoreSync"/> only when there
        /// is content to write. csc happily runs without it, so this method does NOT auto-create.
        /// </remarks>
        private static void EnsureAdditionalFileForIgnoreList()
        {
            try
            {
                if (!File.Exists(RspFilePath))
                {
                    File.WriteAllText(RspFilePath, string.Empty);
                    AssetDatabase.ImportAsset("csc.rsp");
                }

                string sidecarRelativePath = DxMessagingBaseCallIgnoreSync.SidecarAssetPath;
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."))
                    .Replace("\\", "/");
                string sidecarAbsolutePath = Path.Combine(projectRoot, sidecarRelativePath)
                    .Replace("\\", "/");

                bool sidecarExists = File.Exists(sidecarAbsolutePath);
                string desiredLine = $"-additionalfile:\"{sidecarRelativePath}\"";

                string rspContent = File.ReadAllText(RspFilePath);
                List<string> newLines = new();
                bool foundDesired = false;
                bool foundStale = false;

                foreach (
                    string line in rspContent.Split(
                        new[] { '\r', '\n' },
                        StringSplitOptions.RemoveEmptyEntries
                    )
                )
                {
                    string trimmedLine = line.Trim();

                    bool isDxMessagingAdditionalFile =
                        trimmedLine.StartsWith(
                            "-additionalfile:",
                            StringComparison.OrdinalIgnoreCase
                        )
                        && trimmedLine.Contains("DxMessaging.", StringComparison.OrdinalIgnoreCase)
                        && trimmedLine.Contains(
                            "BaseCallIgnore",
                            StringComparison.OrdinalIgnoreCase
                        );

                    if (isDxMessagingAdditionalFile)
                    {
                        if (
                            sidecarExists
                            && string.Equals(
                                trimmedLine,
                                desiredLine,
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                        {
                            if (!foundDesired)
                            {
                                newLines.Add(trimmedLine);
                                foundDesired = true;
                            }
                            else
                            {
                                // Drop duplicate.
                                foundStale = true;
                            }
                        }
                        else
                        {
                            // Stale entry pointing at a moved/renamed/deleted sidecar; drop it.
                            foundStale = true;
                        }
                    }
                    else
                    {
                        newLines.Add(trimmedLine);
                    }
                }

                bool needsAppend = sidecarExists && !foundDesired;
                if (needsAppend)
                {
                    newLines.Add(desiredLine);
                }

                bool modified = foundStale || needsAppend;

                if (modified)
                {
                    string newContent = string.Join(Environment.NewLine, newLines);
                    if (!string.IsNullOrEmpty(newContent))
                    {
                        newContent += Environment.NewLine;
                    }
                    File.WriteAllText(RspFilePath, newContent);
                    AssetDatabase.ImportAsset("csc.rsp");
                    Debug.Log("Updated csc.rsp additionalfile entries.");
                }
            }
            catch (IOException ex)
            {
                Debug.LogError($"Failed to update csc.rsp additionalfile entry: {ex}");
            }
        }

        /// <summary>
        /// One-shot cleanup pass: removes any duplicate analyzer DLLs that an
        /// older version of this package auto-copied into
        /// <see cref="LegacyAnalyzerCopyAssetDir"/> at editor load. The
        /// auto-copy has been removed (the shipped .dll.meta carries the
        /// <c>RoslynAnalyzer</c> label directly), but projects upgrading past
        /// that era still have the duplicated DLLs on disk and would hit
        /// Unity 2021's
        /// <c>PrecompiledAssemblyException: Multiple precompiled assemblies
        /// with the same name</c> until they are removed.
        /// </summary>
        /// <remarks>
        /// Runs at most ONCE per project (gated by the
        /// <c>Library/.dxm-cleanup-done</c> marker, which lives outside
        /// version control by Unity convention). Deletes ONLY DLLs whose
        /// names match the shared <see cref="AnalyzerDllRoster"/> -- never
        /// anything else the user may have placed under the legacy path --
        /// and uses <see cref="AssetDatabase.DeleteAsset"/> so Unity also
        /// removes the matching .meta files. Best-effort; throws are swallowed
        /// so a cleanup failure can never block editor startup.
        /// </remarks>
        private static void CleanUpLegacyAnalyzerCopyPath()
        {
            try
            {
                // Skip if the marker already exists -- the cleanup ran on a
                // previous editor load and we never need to scan again.
                if (File.Exists(LegacyCleanupMarkerPath))
                {
                    return;
                }

                // Delete only DLLs whose names match the shared
                // AnalyzerDllRoster so an unrelated user-authored file under
                // the legacy directory is never touched.
                string legacyAssetDir = LegacyAnalyzerCopyAssetDir;
                string absoluteLegacyDir = Path.GetFullPath(
                    Path.Combine(Application.dataPath, "..", legacyAssetDir)
                );

                bool dirExists = Directory.Exists(absoluteLegacyDir);
                int deleted = 0;

                if (dirExists)
                {
                    foreach (string dllName in AnalyzerDllRoster)
                    {
                        string assetPath = $"{legacyAssetDir}/{dllName}";
                        string absoluteDllPath = Path.Combine(absoluteLegacyDir, dllName);
                        if (!File.Exists(absoluteDllPath))
                        {
                            continue;
                        }

                        // Unity-managed deletion: AssetDatabase.DeleteAsset
                        // also removes the .meta file and updates the
                        // database, which is exactly what we want for a
                        // legacy-copy cleanup.
                        if (AssetDatabase.DeleteAsset(assetPath))
                        {
                            deleted++;
                        }
                    }

                    // If the directory is empty after the DLL sweep (no
                    // user-authored siblings left), delete it too. We rely on
                    // a fresh existence check after the sweep because
                    // AssetDatabase.DeleteAsset removes the file on disk
                    // synchronously.
                    if (
                        Directory.Exists(absoluteLegacyDir)
                        && Directory
                            .GetFiles(absoluteLegacyDir, "*", SearchOption.AllDirectories)
                            .Length == 0
                    )
                    {
                        AssetDatabase.DeleteAsset(legacyAssetDir);
                    }

                    if (deleted > 0)
                    {
                        // Use Debug.LogWarning so the migration shows up
                        // visibly in the Unity console (a regular Debug.Log
                        // is easily lost in the spam at editor load). Users
                        // upgrading past the auto-copy era should know the
                        // cleanup ran and why.
                        Debug.LogWarning(
                            $"DxMessaging: cleaned up {deleted} legacy analyzer DLL copy(s) at {legacyAssetDir}/. This was a one-shot migration; the package no longer auto-copies analyzer DLLs and the legacy directory is no longer needed. See CHANGELOG.md for context."
                        );
                    }
                }

                // Always write the marker so the cleanup is idempotent: even
                // when nothing existed to clean (the common case for new
                // projects), a future package update that introduces another
                // duplicate-DLL bug must use a NEW marker name -- this marker
                // pins THIS migration step.
                string markerDir = Path.GetDirectoryName(LegacyCleanupMarkerPath);
                if (!string.IsNullOrEmpty(markerDir) && !Directory.Exists(markerDir))
                {
                    Directory.CreateDirectory(markerDir);
                }
                File.WriteAllText(
                    LegacyCleanupMarkerPath,
                    $"dxm legacy-copy cleanup completed at {DateTime.UtcNow:O}{Environment.NewLine}"
                );
            }
            catch (IOException ex)
            {
                // Best-effort: skip on failure. A future editor load sees the
                // missing marker and re-attempts the cleanup. Log a warning so
                // the attempted-and-failed migration is visible (vs. silent).
                Debug.LogWarning(
                    $"DxMessaging: legacy-copy cleanup pass hit an IO error and was skipped. Delete Library/.dxm-cleanup-done and reopen the project to re-run. Details: {ex.Message}"
                );
            }
            catch (UnityException ex)
            {
                // AssetDatabase.DeleteAsset and friends can throw UnityException
                // (NOT a subclass of IOException) when the editor is in an
                // unexpected state (e.g. the asset database is mid-refresh).
                // Same best-effort posture as the IOException path: don't let
                // a cleanup hiccup surface as an editor startup error.
                Debug.LogWarning(
                    $"DxMessaging: legacy-copy cleanup pass hit an unexpected Unity error and was skipped. Delete Library/.dxm-cleanup-done and reopen the project to re-run. Details: {ex.Message}"
                );
            }
        }

        /// <summary>
        /// Enumerates every analyzer directory that should be probed for
        /// <see cref="AnalyzerDllRoster"/> entries on this editor load.
        /// Combines the static <see cref="AnalyzerDirectories"/> entries
        /// (resolved against the project root) with the dynamically resolved
        /// HASHED UPM PackageCache directory, when one exists.
        /// </summary>
        /// <param name="projectRoot">Absolute path of the Unity project root.</param>
        /// <remarks>
        /// The HASHED PackageCache probe matters because Unity rewrites a
        /// resolved registry/git package to
        /// <c>Library/PackageCache/&lt;id&gt;@&lt;lockHash&gt;/</c>, and the
        /// previous hardcoded fallback
        /// <c>Library/PackageCache/com.wallstop-studios.dxmessaging/</c> never
        /// matched on disk (no <c>@&lt;hash&gt;</c> suffix). We take the FIRST
        /// directory that matches the <c>com.wallstop-studios.dxmessaging@*</c>
        /// glob -- in practice there is at most one resolved version per editor
        /// load.
        /// </remarks>
        private static IEnumerable<string> EnumerateResolvedAnalyzerDirectories(string projectRoot)
        {
            foreach (string directory in AnalyzerDirectories)
            {
                yield return Path.IsPathRooted(directory)
                    ? directory
                    : Path.GetFullPath(Path.Combine(projectRoot, directory));
            }

            // HASHED PackageCache probe. We use Directory.EnumerateDirectories
            // (not GetDirectories) so a missing parent (no Library/PackageCache,
            // e.g. on a fresh project) is silently skipped instead of throwing.
            string packageCacheRoot = Path.GetFullPath(
                Path.Combine(projectRoot, "Library", "PackageCache")
            );
            if (!Directory.Exists(packageCacheRoot))
            {
                yield break;
            }

            // EnumerateDirectories' pattern argument matches against directory
            // NAMES, not full paths. The hashed-form name is
            // `com.wallstop-studios.dxmessaging@<hash>` -- the `@*` glob covers
            // every possible hash suffix.
            string hashedGlob = $"{PackageId}@*";
            IEnumerator<string> enumerator;
            try
            {
                enumerator = Directory
                    .EnumerateDirectories(
                        packageCacheRoot,
                        hashedGlob,
                        SearchOption.TopDirectoryOnly
                    )
                    .GetEnumerator();
            }
            catch (IOException)
            {
                // Best-effort: any IO error (locked dir, etc.) silently skips the
                // hashed-cache fallback. The static `Packages/` entry still
                // covers the `file:` / `git:` / embedded cases.
                yield break;
            }
            catch (UnauthorizedAccessException)
            {
                yield break;
            }

            using (enumerator)
            {
                // In practice there is at most one resolved hash for a given
                // package per editor load -- if Unity has more than one
                // (mid-upgrade transient), the first wins. Yielded directories
                // include the `Editor/Analyzers/` suffix so the DLL probe below
                // works uniformly with the static entries.
                if (enumerator.MoveNext())
                {
                    yield return Path.Combine(enumerator.Current, "Editor", "Analyzers");
                }
            }
        }

        private static IEnumerable<string> GetAnalyzerArguments()
        {
            // The analyzer DLLs and shared Roslyn surface ship unconditionally;
            // they're light enough and required for DXMSG002-DXMSG009 to
            // function at all. The roster lives at type scope (see
            // AnalyzerDllRoster) so the cleanup pass can reuse it; the
            // discovery loop below silently skips any name that isn't on disk.
            HashSet<string> yielded = new(StringComparer.OrdinalIgnoreCase);
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

            foreach (string absoluteDirectory in EnumerateResolvedAnalyzerDirectories(projectRoot))
            {
                foreach (string dllName in AnalyzerDllRoster)
                {
                    string absoluteAnalyzerPath = Path.Combine(absoluteDirectory, dllName);
                    if (!File.Exists(absoluteAnalyzerPath))
                    {
                        continue;
                    }

                    string projectRelativePath = FileUtil.GetProjectRelativePath(
                        absoluteAnalyzerPath
                    );
                    if (string.IsNullOrEmpty(projectRelativePath))
                    {
                        continue;
                    }

                    string normalizedRelativePath = projectRelativePath.Replace("\\", "/");
                    if (!yielded.Add(normalizedRelativePath))
                    {
                        continue;
                    }

                    yield return $"-a:\"{normalizedRelativePath}\"";
                }
            }
        }
    }
}
#endif
