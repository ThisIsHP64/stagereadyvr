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
/// Release signing is read from the environment so no secret touches the repo:
/// STAGEREADY_KEYSTORE_PATH, STAGEREADY_KEYSTORE_PASS, STAGEREADY_KEY_ALIAS and
/// STAGEREADY_KEY_PASS. When none are set the build falls back to the Android
/// debug keystore and logs a warning.
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

        if (!ApplyReleaseSigning(out SigningState previous))
        {
            EditorApplication.Exit(3);
            return;
        }

        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = output,
            target = BuildTarget.Android,
            options = BuildOptions.CompressWithLz4HC
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        previous.Restore();
        BuildSummary summary = report.summary;
        LogBuildErrors(report);
        Debug.Log($"[PicoBuild] result={summary.result} errors={summary.totalErrors} " +
                  $"warnings={summary.totalWarnings} bytes={summary.totalSize} output={summary.outputPath}");

        EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
    }

    // keystore settings are restored after the build so machine specific paths never reach ProjectSettings
    readonly struct SigningState
    {
        readonly bool custom;
        readonly string keystoreName;
        readonly string aliasName;

        public SigningState(bool custom, string keystoreName, string aliasName)
        {
            this.custom = custom;
            this.keystoreName = keystoreName;
            this.aliasName = aliasName;
        }

        public void Restore()
        {
            PlayerSettings.Android.useCustomKeystore = custom;
            PlayerSettings.Android.keystoreName = keystoreName;
            PlayerSettings.Android.keyaliasName = aliasName;
            PlayerSettings.Android.keystorePass = string.Empty;
            PlayerSettings.Android.keyaliasPass = string.Empty;
        }
    }

    // returns false only for a partial or broken signing setup, so a release build never silently ships debug signed
    static bool ApplyReleaseSigning(out SigningState previous)
    {
        previous = new SigningState(PlayerSettings.Android.useCustomKeystore,
            PlayerSettings.Android.keystoreName, PlayerSettings.Android.keyaliasName);

        string path = Environment.GetEnvironmentVariable("STAGEREADY_KEYSTORE_PATH");
        string storePass = Environment.GetEnvironmentVariable("STAGEREADY_KEYSTORE_PASS");
        string alias = Environment.GetEnvironmentVariable("STAGEREADY_KEY_ALIAS");
        string keyPass = Environment.GetEnvironmentVariable("STAGEREADY_KEY_PASS");

        bool anySet = !string.IsNullOrEmpty(path) || !string.IsNullOrEmpty(storePass) ||
                      !string.IsNullOrEmpty(alias) || !string.IsNullOrEmpty(keyPass);
        if (!anySet)
        {
            Debug.LogWarning("[PicoBuild] no release signing variables set, using the debug keystore.");
            return true;
        }

        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(storePass) ||
            string.IsNullOrEmpty(alias) || string.IsNullOrEmpty(keyPass))
        {
            Debug.LogError("[PicoBuild] release signing is partially configured, set all four STAGEREADY_* variables.");
            return false;
        }

        if (!File.Exists(path))
        {
            Debug.LogError($"[PicoBuild] keystore not found at {path}");
            return false;
        }

        PlayerSettings.Android.useCustomKeystore = true;
        PlayerSettings.Android.keystoreName = path;
        PlayerSettings.Android.keystorePass = storePass;
        PlayerSettings.Android.keyaliasName = alias;
        PlayerSettings.Android.keyaliasPass = keyPass;
        Debug.Log($"[PicoBuild] signing with release keystore, alias {alias}");
        return true;
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
