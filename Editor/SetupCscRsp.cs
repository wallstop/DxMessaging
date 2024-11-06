#if UNITY_EDITOR

namespace DxMessaging.Editor
{
    using System.IO;
    using UnityEditor;
    using UnityEngine;

    [InitializeOnLoad]
    public static class SetupCscRsp
    {
        private static readonly string RspFilePath =
            Path.Combine(Application.dataPath, "..", "csc.rsp").Replace("\\", "/");

        private static readonly string AnalyzerPathRelative =
            "Packages/com.wallstop-studios.dxmessaging/Editor/RoslynAnalyzers/WallstopStudios.DxMessaging.SourceGenerators.dll";

        private static readonly string LibraryPathRelative =
            "Library/PackageCache/com.wallstop-studios.dxmessaging/Editor/RoslynAnalyzers/WallstopStudios.DxMessaging.SourceGenerators.dll";

        private static readonly string AnalyzerArgument = $"-a:\"{AnalyzerPathRelative}\"";

        private static readonly string LibraryArgument = $"-a:\"{LibraryPathRelative}\"";
        
        static SetupCscRsp()
        {
            EditorApplication.delayCall += EnsureCscRsp;
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

                string[] paths = { AnalyzerArgument, LibraryArgument };
                bool changed = false;
                foreach (string path in paths)
                {
                    if (!rspContent.Contains(path))
                    {
                        // Append the analyzer argument to csc.rsp
                        File.AppendAllText(RspFilePath, $"{path}\n");
                        changed = true;
                    }
                }

                if (changed)
                {
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