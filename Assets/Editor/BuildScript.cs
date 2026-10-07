using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// CLI: Unity -batchmode -quit -projectPath . -buildTarget Android -executeMethod BuildScript.BuildAndroid
public static class BuildScript
{
    [MenuItem("Quest3Bridge/Build Android APK")]
    public static void BuildAndroid()
    {
        var options = new BuildPlayerOptions
        {
            scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
            locationPathName = "Build/Quest3Depth.apk",
            target = BuildTarget.Android,
            options = BuildOptions.None
        };
        var report = BuildPipeline.BuildPlayer(options);
        Debug.Log($"BuildScript: {report.summary.result} ({report.summary.totalSize / 1048576} MB)");
        if (Application.isBatchMode && report.summary.result != BuildResult.Succeeded)
            EditorApplication.Exit(1);
    }
}
