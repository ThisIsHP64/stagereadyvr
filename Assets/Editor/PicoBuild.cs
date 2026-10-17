#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Command line Android build for the PICO 4, using the scenes enabled in
/// Build Settings and a release (non development) player compressed with
/// LZ4HC, matching the Build Settings window setup.
///
/// Unity -batchmode -nographics -quit -projectPath . -buildTarget Android
///       -executeMethod PicoBuild.BuildAndroid -buildOutput Builds/app.apk
///
/// The process exits 0 on success and non zero on failure so scripts can
/// tell the two apart.
/// </summary>
public static class PicoBuild
{
    const string DefaultOutput = "Builds/StageReadyVR.apk";

    public static void BuildAndroid()
    {
        string output = Path.GetFullPath(GetArg("-buildOutput") ?? DefaultOutput);
        string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();

        if (scenes.Length == 0)
        {
            Debug.LogError("[PicoBuild] no enabled scenes in Build Settings.");
            EditorApplication.Exit(2);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(output));

        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = output,
            target = BuildTarget.Android,
            options = BuildOptions.CompressWithLz4HC
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;
        LogBuildErrors(report);
        Debug.Log($"[PicoBuild] result={summary.result} errors={summary.totalErrors} " +
                  $"warnings={summary.totalWarnings} bytes={summary.totalSize} output={summary.outputPath}");

        EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
    }

    // the summary only counts errors, so print their text to make failures traceable
    static void LogBuildErrors(BuildReport report)
    {
        foreach (BuildStep step in report.steps)
        {
            foreach (BuildStepMessage message in step.messages)
            {
                if (message.type == LogType.Error || message.type == LogType.Exception)
                    Debug.Log($"[PicoBuild] error in step '{step.name}': {message.content}");
            }
        }
    }

    static string GetArg(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
#endif
