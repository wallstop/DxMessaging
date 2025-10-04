using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DxMessaging.Editor
{
    /// <summary>
    /// Exports the DxMessaging package as a .unitypackage file
    /// </summary>
    public static class PackageExporter
    {
        private const string PackageName = "com.wallstop-studios.dxmessaging";

        [MenuItem("DxMessaging/Export Package")]
        public static void ExportPackageMenu()
        {
            ExportPackage();
        }

        public static void Export()
        {
            string[] args = Environment.GetCommandLineArgs();
            string packageName = "DxMessaging.unitypackage";
            string exportPath = "./";

            // Parse command line arguments
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-packageName" && i + 1 < args.Length)
                {
                    packageName = args[i + 1];
                }
                if (args[i] == "-exportPath" && i + 1 < args.Length)
                {
                    exportPath = args[i + 1];
                }
            }

            ExportPackage(packageName, exportPath);

            // Exit Unity in batch mode
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(0);
            }
        }

        private static void ExportPackage(string packageName = "DxMessaging.unitypackage", string exportPath = "./")
        {
            try
            {
                // Find the package path
                string packagePath = Path.GetFullPath(".");
                Debug.Log($"Package path: {packagePath}");

                // Define what to include in the export
                string[] assetPaths = new[]
                {
                    "Runtime",
                    "Editor",
                    "Tests",
                    "SourceGenerators",
                    "package.json",
                    "README.md",
                    "LICENSE.md",
                    "CHANGELOG.md",
                    "Third Party Notices.md"
                };

                // Create the full export path
                string fullExportPath = Path.Combine(exportPath, packageName);
                Debug.Log($"Exporting package to: {fullExportPath}");

                // Export the package
                AssetDatabase.ExportPackage(
                    assetPaths,
                    fullExportPath,
                    ExportPackageOptions.Recurse
                );

                Debug.Log($"Successfully exported package: {fullExportPath}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to export package: {ex.Message}");
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(1);
                }
                throw;
            }
        }
    }
}
