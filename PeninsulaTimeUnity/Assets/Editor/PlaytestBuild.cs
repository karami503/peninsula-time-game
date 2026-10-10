using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
namespace PeninsulaTime
{
    // Batch build of a macOS player into $PLAYTEST_BUILD_DIR, so QA runs never overwrite Builds/.
    // Unity -batchmode -quit -projectPath . -executeMethod PeninsulaTime.PlaytestBuild.Run
    public static class PlaytestBuild
    {
        public static void Run()
        {
            string dir = Environment.GetEnvironmentVariable("PLAYTEST_BUILD_DIR");
            if (string.IsNullOrEmpty(dir)) { Debug.LogError("PlaytestBuild: set PLAYTEST_BUILD_DIR"); EditorApplication.Exit(1); return; }
            Directory.CreateDirectory(dir);
            var report = BuildPipeline.BuildPlayer(new[] { "Assets/Scenes/Main.unity" }, Path.Combine(dir, "PeninsulaTime.app"), BuildTarget.StandaloneOSX, BuildOptions.None);
            Debug.Log("PlaytestBuild: " + report.summary.result + ", errors " + report.summary.totalErrors);
            EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
