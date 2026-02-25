using UnityEngine;
using UnityEditor;
using System.IO;
using System.Threading;

/// <summary>
/// Persistent build watcher for the Custom Premiums pipeline.
/// Runs inside the already-open Unity Editor via [InitializeOnLoad].
///
/// Protocol:
///   1. External script creates "build_trigger.txt" in the project root
///   2. This watcher detects it, deletes it, runs the AssetBundle build
///   3. Writes "build_result.txt" with "OK" or "FAIL: reason"
///   4. External script polls for build_result.txt and reads the outcome
///
/// Uses a background thread timer so it works even when Unity is unfocused.
/// </summary>
[InitializeOnLoad]
public static class AutoBuildOnLoad
{
    private static readonly string ProjectRoot = Path.Combine(Application.dataPath, "..");
    private static readonly string TriggerFile = Path.Combine(ProjectRoot, "build_trigger.txt");
    private static readonly string ResultFile = Path.Combine(ProjectRoot, "build_result.txt");

    // Thread-safe flag: background timer sets it, main thread (EditorApplication.update) reads it
    private static volatile bool buildRequested = false;
    private static Timer bgTimer;

    static AutoBuildOnLoad()
    {
        // Keep editor update loop running even when Unity is not in focus
        Application.runInBackground = true;

        // Clean up stale result file on domain reload
        if (File.Exists(ResultFile))
            File.Delete(ResultFile);

        // Register main-thread update to execute builds
        EditorApplication.update += OnEditorUpdate;

        // Background timer that polls for the trigger file every 500ms
        // This fires on a thread-pool thread, independent of Unity's focus state
        bgTimer = new Timer(BackgroundPoll, null, 500, 500);

        Debug.Log("[BuildWatcher] Watching for build triggers...");
    }

    /// <summary>
    /// Runs on a thread-pool thread — always fires regardless of Unity focus.
    /// Only sets a flag; actual build runs on the main thread via EditorApplication.update.
    /// </summary>
    private static void BackgroundPoll(object state)
    {
        if (buildRequested)
            return;

        try
        {
            if (File.Exists(TriggerFile))
            {
                File.Delete(TriggerFile);
                Debug.Log("[BuildWatcher] Trigger detected (background thread).");

                if (File.Exists(ResultFile))
                    File.Delete(ResultFile);

                buildRequested = true;
            }
        }
        catch (System.Exception) { }
    }

    /// <summary>
    /// Runs on the main thread. Checks the flag and executes the build.
    /// </summary>
    private static void OnEditorUpdate()
    {
        if (!buildRequested)
            return;

        buildRequested = false;
        RunBuild();
    }

    private static void RunBuild()
    {
        try
        {
            Debug.Log("[BuildWatcher] Building 1832 bundle...");
            BuildPremiumBundle.WatcherBuild1832();
            File.WriteAllText(ResultFile, "OK");
            Debug.Log("[BuildWatcher] Build complete! Result: OK");
        }
        catch (System.Exception e)
        {
            string msg = $"FAIL: {e.Message}";
            File.WriteAllText(ResultFile, msg);
            Debug.LogError($"[BuildWatcher] {msg}\n{e.StackTrace}");
        }
    }
}
