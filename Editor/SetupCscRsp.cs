#if UNITY_EDITOR

namespace DxMessaging.Editor
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using UnityEditor;
    using UnityEngine;
    using Object = UnityEngine.Object;

    [InitializeOnLoad]
    public static class SetupCscRsp
    {
        private static readonly string RspFilePath =
            Path.Combine(Application.dataPath, "..", "csc.rsp").Replace("\\", "/");

        private static readonly string AnalyzerPathRelative =
            "Packages/com.wallstop-studios.dxmessaging/Editor/Analyzers/";

        private static readonly string LibraryPathRelative =
            "Library/PackageCache/com.wallstop-studios.dxmessaging/Editor/Analyzers/";

        private static readonly string SourceGeneratorDllName = "WallstopStudios.DxMessaging.SourceGenerators.dll";

        private static readonly string[] RequiredDllNames =
        {
            SourceGeneratorDllName,
            "Microsoft.CodeAnalysis.dll",
            "Microsoft.CodeAnalysis.CSharp.dll",
            "System.Reflection.Metadata.dll"
        };

        private static readonly string LibraryArgument = $"-a:\"{LibraryPathRelative}\"";

        static SetupCscRsp()
        {
            EditorApplication.delayCall += EnsureCscRsp;
            EditorApplication.delayCall += EnsureDLLsExistInAssets;
        }

        private static void EnsureDLLsExistInAssets()
        {
            HashSet<string> dllNames = new();
            foreach (string dllGuid in AssetDatabase.FindAssets("t:DefaultAsset", new[] { "Assets" }))
            {
                string dllPath = AssetDatabase.GUIDToAssetPath(dllGuid);
                if (!dllPath.EndsWith(".dll"))
                {
                    continue;
                }

                string dllName = Path.GetFileName(dllPath);
                dllNames.Add(dllName);
            }

            string[] dllRelativeDirectories = { LibraryPathRelative, AnalyzerPathRelative };

            bool anyFound = false;
            foreach (string requiredDllName in RequiredDllNames.Where(dllName => !dllNames.Contains(dllName)))
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

                        const string pluginsDirectory = "Assets/Plugins/WallstopStudios.DxMessaging/";
                        string outputAsset = $"{pluginsDirectory}{requiredDllName}";
                        Directory.CreateDirectory(pluginsDirectory);
                        if (!File.Exists(outputAsset))
                        {
                            File.Copy($"{relativeDirectory}{requiredDllName}", outputAsset);
                            AssetDatabase.ImportAsset(outputAsset);
                            found = true;
                        }

                        if (requiredDllName == SourceGeneratorDllName)
                        {
                            Object loadedDll = AssetDatabase.LoadMainAssetAtPath(outputAsset);
                            AssetDatabase.SetLabels(loadedDll, new[] { "RoslynAnalyzer" });
                        }
                        
                        PluginImporter importer = AssetImporter.GetAtPath(outputAsset) as PluginImporter;
                        if (importer != null)
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
                        Debug.LogError($"Failed to copy {requiredDllName} to Assets, failed with {e}.");
                    }
                }

                anyFound |= found;
                Debug.Log(
                    $"Missing required dll '{requiredDllName}', " +
                    $"{(found ? "creation successful." : "WARNING! Manual creation required.")}");
            }

            if (anyFound)
            {
                AssetDatabase.Refresh();
            }
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
                if (!rspContent.Contains(LibraryArgument))
                {
                    // Append the analyzer argument to csc.rsp
                    File.AppendAllText(RspFilePath, $"{LibraryArgument}\n");
                    AssetDatabase.ImportAsset("csc.rsp");
                    Debug.Log("Updated csc.rsp.");
                }
            }
            catch (IOException ex)
            {
                Debug.LogError($"Failed to modify csc.rsp: {ex}");
            }
        }
    }
}
#endif