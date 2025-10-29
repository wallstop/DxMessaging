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

        private static readonly string AnalyzerPathRelative =
            "Packages/com.wallstop-studios.dxmessaging/Editor/Analyzers/";

        private static readonly string LibraryPathRelative =
            "Library/PackageCache/com.wallstop-studios.dxmessaging/Editor/Analyzers/";

        private static readonly string SourceGeneratorDllName =
            "WallstopStudios.DxMessaging.SourceGenerators.dll";

        private static readonly string[] RequiredDllNames =
        {
            SourceGeneratorDllName,
            "Microsoft.CodeAnalysis.dll",
            "Microsoft.CodeAnalysis.CSharp.dll",
            "System.Reflection.Metadata.dll",
            "System.Runtime.CompilerServices.Unsafe.dll",
            "System.Collections.Immutable.dll",
        };

        private static readonly string LibraryArgument = $"-a:\"{LibraryPathRelative}\"";

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

            string[] dllRelativeDirectories = { LibraryPathRelative, AnalyzerPathRelative };

            bool anyFound = false;
            foreach (
                string requiredDllName in RequiredDllNames.Where(dllName =>
                    !DllNames.Contains(dllName)
                )
            )
            {
                bool found = false;
                foreach (string relativeDirectory in dllRelativeDirectories)
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
                        string sourceAsset = $"{relativeDirectory}{requiredDllName}";
                        if (!Directory.Exists(pluginsDirectory))
                        {
                            Directory.CreateDirectory(pluginsDirectory);
                            AssetDatabase.Refresh();
                        }
                        bool needsCopy = FilesDiffer(sourceAsset, outputAsset);
                        if (needsCopy)
                        {
                            File.Copy(sourceAsset, outputAsset, true);
                            AssetDatabase.ImportAsset(outputAsset);
                            found = true;
                        }
                        else
                        {
                            found = true;
                        }

                        if (requiredDllName == SourceGeneratorDllName)
                        {
                            Object loadedDll = AssetDatabase.LoadMainAssetAtPath(outputAsset);
                            AssetDatabase.SetLabels(loadedDll, new[] { "RoslynAnalyzer" });
                        }

                        if (
                            needsCopy
                            && AssetImporter.GetAtPath(outputAsset) is PluginImporter importer
                        )
                        {
                            importer.SetCompatibleWithAnyPlatform(false);
                            importer.SetExcludeFromAnyPlatform("Editor", false);
                            importer.SetExcludeFromAnyPlatform("Standalone", false);
                            importer.SaveAndReimport();
                        }

                        break;
                    }
                    catch (Exception e)
                    {
                        Debug.LogError(
                            $"Failed to copy {requiredDllName} to Assets, failed with {e}."
                        );
                    }
                }

                anyFound |= found;
            }

            if (anyFound)
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
                if (rspContent.Contains(LibraryArgument, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                File.AppendAllText(RspFilePath, $"{LibraryArgument}{Environment.NewLine}");
                AssetDatabase.ImportAsset("csc.rsp");
                Debug.Log("Updated csc.rsp.");
            }
            catch (IOException ex)
            {
                Debug.LogError($"Failed to modify csc.rsp: {ex}");
            }
        }
    }
}
#endif
