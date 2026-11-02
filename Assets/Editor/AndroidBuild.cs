using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Builds the Android APK from the command line:
/// <c>Unity -batchmode -quit -projectPath . -buildTarget Android -executeMethod AndroidBuild.Build [-buildOutput path.apk]</c>.
/// The build uses the enabled scenes of the build settings and the project's player settings.
/// </summary>
public static class AndroidBuild
{
    const string DefaultOutput = "Builds/Android/ARClient.apk";

    public static void Build()
    {
        EditorUserBuildSettings.buildAppBundle = false;
        var options = new BuildPlayerOptions
        {
            scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
            locationPathName = Argument("-buildOutput") ?? DefaultOutput,
            target = BuildTarget.Android,
            options = BuildOptions.None,
        };
        var summary = BuildPipeline.BuildPlayer(options).summary;
        Debug.Log($"AndroidBuild: {summary.result}, {summary.totalSize} bytes, {summary.totalErrors} errors, {summary.totalWarnings} warnings, at {summary.outputPath}");
        if (Application.isBatchMode)
            EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
    }

    static string Argument(string name)
    {
        var args = Environment.GetCommandLineArgs();
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
