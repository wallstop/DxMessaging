#if UNITY_EDITOR

namespace DxMessaging.Editor
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Security.Cryptography;
    using UnityEditor;
    using UnityEngine;
    using Object = UnityEngine.Object;

    [InitializeOnLoad]
    public static class SetupCscRsp
    {
        private static readonly string RspFilePath = Path.Combine(
                Application.dataPath,
                "..",
                "csc.rsp"
            )
            .Replace("\\", "/");

        private static readonly string[] AnalyzerDirectories =
        {
            "Packages/com.wallstop-studios.dxmessaging/Editor/Analyzers/",
            "Library/PackageCache/com.wallstop-studios.dxmessaging/Editor/Analyzers/",
        };

        private static readonly string SourceGeneratorDllName =
            "WallstopStudios.DxMessaging.SourceGenerators.dll";

        private static readonly string[] RequiredDllNames =
        {
            SourceGeneratorDllName,
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
            "Microsoft.Bcl.AsyncInterfaces.dll",
        };

        private static readonly HashSet<string> DllNames = new(StringComparer.OrdinalIgnoreCase);

        static SetupCscRsp()
        {
            EditorApplication.delayCall += EnsureDLLsExistInAssets;
            EditorApplication.delayCall += EnsureCscRsp;
        }

        private static void EnsureDLLsExistInAssets()
        {
            DllNames.Clear();
            foreach (
                string dllGuid in AssetDatabase.FindAssets("t:DefaultAsset", new[] { "Assets" })
            )
            {
                string dllPath = AssetDatabase.GUIDToAssetPath(dllGuid);
                if (!dllPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!dllPath.Contains("Assets/Plugins", StringComparison.OrdinalIgnoreCase))
                {
                    string dllName = Path.GetFileName(dllPath);
                    DllNames.Add(dllName);
                }
            }

            foreach (string requiredDllName in RequiredDllNames)
            {
                if (DllNames.Contains(requiredDllName))
                {
                    continue;
                }

                foreach (string relativeDirectory in AnalyzerDirectories)
                {
                    try
                    {
                        string sourceFile = $"{relativeDirectory}{requiredDllName}";
                        if (!File.Exists(sourceFile))
                        {
                            continue;
                        }

                        const string pluginsDirectory =
                            "Assets/Plugins/Editor/WallstopStudios.DxMessaging/";
                        string outputAsset = $"{pluginsDirectory}{requiredDllName}";
                        if (!Directory.Exists(pluginsDirectory))
                        {
                            Directory.CreateDirectory(pluginsDirectory);
                            AssetDatabase.Refresh();
                        }
                        bool needsCopy = FilesDiffer(sourceFile, outputAsset);
                        if (needsCopy)
                        {
                            File.Copy(sourceFile, outputAsset, true);
                            AssetDatabase.ImportAsset(outputAsset);
                        }

                        if (requiredDllName == SourceGeneratorDllName)
                        {
                            Object loadedDll = AssetDatabase.LoadMainAssetAtPath(outputAsset);
                            if (loadedDll != null)
                            {
                                string[] existingLabels = AssetDatabase.GetLabels(loadedDll);
                                if (!existingLabels.Contains("RoslynAnalyzer"))
                                {
                                    List<string> newLabels = existingLabels.ToList();
                                    newLabels.Add("RoslynAnalyzer");
                                    AssetDatabase.SetLabels(loadedDll, newLabels.ToArray());
                                }
                            }
                        }

                        if (AssetImporter.GetAtPath(outputAsset) is PluginImporter importer)
                        {
                            bool importerDirty = false;

                            if (importer.GetCompatibleWithAnyPlatform())
                            {
                                importer.SetCompatibleWithAnyPlatform(false);
                                importerDirty = true;
                            }

                            if (importer.GetExcludeFromAnyPlatform("Editor"))
                            {
                                importer.SetExcludeFromAnyPlatform("Editor", false);
                                importerDirty = true;
                            }

                            if (!importer.GetExcludeFromAnyPlatform("Standalone"))
                            {
                                importer.SetExcludeFromAnyPlatform("Standalone", true);
                                importerDirty = true;
                            }

                            if (importerDirty || needsCopy)
                            {
                                importer.SaveAndReimport();
                            }
                        }

                        DllNames.Add(requiredDllName);
                        break;
                    }
                    catch (Exception e)
                    {
                        Debug.LogError(
                            $"Failed to copy {requiredDllName} to Assets, failed with {e}."
                        );
                    }
                }
            }

            if (DllNames.Count > 0)
            {
                AssetDatabase.Refresh();
            }
        }

        private static bool FilesDiffer(string sourcePath, string destinationPath)
        {
            if (!File.Exists(destinationPath))
            {
                return true;
            }

            FileInfo sourceInfo = new(sourcePath);
            FileInfo destinationInfo = new(destinationPath);
            if (sourceInfo.Length != destinationInfo.Length)
            {
                return true;
            }

            using FileStream sourceStream = File.OpenRead(sourcePath);
            using FileStream destinationStream = File.OpenRead(destinationPath);
            using SHA256 sha256 = SHA256.Create();
            byte[] sourceHash = sha256.ComputeHash(sourceStream);
            byte[] destinationHash = sha256.ComputeHash(destinationStream);
            return !sourceHash.AsSpan().SequenceEqual(destinationHash);
        }

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

        private static IEnumerable<string> GetAnalyzerArguments()
        {
            HashSet<string> yielded = new(StringComparer.OrdinalIgnoreCase);
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

            foreach (string directory in AnalyzerDirectories)
            {
                foreach (string dllName in RequiredDllNames)
                {
                    string absoluteDirectory = Path.IsPathRooted(directory)
                        ? directory
                        : Path.GetFullPath(Path.Combine(projectRoot, directory));

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
