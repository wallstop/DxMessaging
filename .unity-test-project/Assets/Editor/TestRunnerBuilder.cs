// =============================================================================
// TestRunnerBuilder.cs
// =============================================================================
// Editor-only entry points used by scripts/unity/run-tests.sh (--platform
// standalone) to build and run an IL2CPP standalone test player. Invoked from
// the Unity command line via:
//
//     -executeMethod
//     WallstopStudios.DxMessaging.TestHarness.Editor.TestRunnerBuilder.BuildIL2CPPTestPlayer
//
// The harness lives entirely outside the package (under .unity-test-project/)
// and is not shipped to consumers.
// =============================================================================
#if UNITY_EDITOR
namespace WallstopStudios.DxMessaging.TestHarness.Editor
{
    using System;
    using System.IO;
    using UnityEditor;
    using UnityEditor.Build.Reporting;
    using UnityEngine;

    public static class TestRunnerBuilder
    {
        private const string MenuRoot = "DxMessaging/Test Harness/";

        // Output path for the IL2CPP test player. Resolved relative to
        // .unity-test-project/. Kept in sync with scripts/unity/run-tests.sh
        // and run-tests.ps1, which launch the produced binary as a separate
        // step (we no longer use BuildOptions.AutoRunPlayer because the
        // unityci/editor Linux IL2CPP container has no X server, and Unity's
        // build-report does not propagate the player's test exit code).
        //
        // CI override: the GitHub Actions workflow (.github/workflows/
        // unity-il2cpp.yml) sets DXM_IL2CPP_BUILD_PATH to game-ci's expected
        // output convention ($GITHUB_WORKSPACE/builds/StandaloneLinux64/
        // IL2CPPTests/Tests.x86_64). The local docker driver in
        // scripts/unity/run-tests.sh{,ps1} sets it to the in-container
        // equivalent. When unset (e.g. interactive Editor builds), the
        // default below is used.
        private const string DefaultBuildPathRelative = "Builds/IL2CPPTests/Tests.x86_64";
        private const string BuildPathEnvVar = "DXM_IL2CPP_BUILD_PATH";

        [MenuItem(MenuRoot + "Build IL2CPP Test Player (Linux64)")]
        public static void BuildIL2CPPTestPlayer()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string envOverride = Environment.GetEnvironmentVariable(BuildPathEnvVar);
            string buildPath;
            if (!string.IsNullOrEmpty(envOverride))
            {
                // Treat the env var as authoritative. If a relative path was
                // supplied, anchor it at the project root so callers don't
                // have to know the editor's CWD.
                buildPath = Path.IsPathRooted(envOverride)
                    ? Path.GetFullPath(envOverride)
                    : Path.GetFullPath(Path.Combine(projectRoot, envOverride));
            }
            else
            {
                buildPath = Path.GetFullPath(Path.Combine(projectRoot, DefaultBuildPathRelative));
            }
            string buildDir = Path.GetDirectoryName(buildPath);
            if (!string.IsNullOrEmpty(buildDir) && !Directory.Exists(buildDir))
            {
                Directory.CreateDirectory(buildDir);
            }

            // IncludeTestAssemblies pulls all *.Tests.* asmdefs into the build.
            // Development is required for Unity Test Framework's command-line
            // test runner to be embedded in IL2CPP players (the runner uses
            // development-only diagnostic hooks). The shell driver launches
            // the produced binary with `-runTests -testResults <xml>` and
            // captures its exit code separately.
            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = Array.Empty<string>(),
                locationPathName = buildPath,
                target = BuildTarget.StandaloneLinux64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.IncludeTestAssemblies | BuildOptions.Development,
            };

            PlayerSettings.SetScriptingBackend(
                BuildTargetGroup.Standalone,
                ScriptingImplementation.IL2CPP
            );
            PlayerSettings.SetApiCompatibilityLevel(
                BuildTargetGroup.Standalone,
                ApiCompatibilityLevel.NET_Standard
            );

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            Debug.Log(
                $"[DxMessaging] IL2CPP test player build: result={summary.result}, "
                    + $"output={summary.outputPath}, totalErrors={summary.totalErrors}, "
                    + $"totalWarnings={summary.totalWarnings}"
            );

            if (summary.result != BuildResult.Succeeded)
            {
                EditorApplication.Exit(1);
            }
            else
            {
                EditorApplication.Exit(0);
            }
        }

        [MenuItem(MenuRoot + "Run PlayMode Tests Via Command Line")]
        public static void RunPlayModeTestsViaCommandLine()
        {
            // Placeholder entry point: the canonical PlayMode invocation goes
            // through `Unity -runTests -testPlatform PlayMode`. This method is
            // exposed so future callers can opt into a programmatic launcher
            // (for example UnityEditor.TestTools.TestRunner.Api.TestRunnerApi)
            // without changing the runner script's flag surface.
            Debug.Log(
                "[DxMessaging] RunPlayModeTestsViaCommandLine invoked. "
                    + "Use `Unity -runTests -testPlatform PlayMode` for the canonical run path."
            );
            EditorApplication.Exit(0);
        }
    }
}
#endif
