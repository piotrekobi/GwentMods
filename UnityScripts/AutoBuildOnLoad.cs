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
/// Compile handshake:
///   On domain reload (after script recompilation), writes "compile_stamp.txt"
///   with a timestamp. build.py uses this to confirm new code is active before
///   sending a build trigger.
///
/// Uses a background thread timer so it works even when Unity is unfocused.
/// </summary>
[InitializeOnLoad]
public static class AutoBuildOnLoad
{
    private static readonly string ProjectRoot = Path.Combine(Application.dataPath, "..");
    private static readonly string TriggerFile = Path.Combine(ProjectRoot, "build_trigger.txt");
    private static readonly string ResultFile = Path.Combine(ProjectRoot, "build_result.txt");
    private static readonly string CompileStampFile = Path.Combine(ProjectRoot, "compile_stamp.txt");

    // Thread-safe flag: background timer sets it, main thread (EditorApplication.update) reads it
    private static volatile bool buildRequested = false;
    private static volatile string buildArtId = null; // ART_ID read from trigger file
    private static Timer bgTimer;

    static AutoBuildOnLoad()
    {
        // Keep editor update loop running even when Unity is not in focus
        Application.runInBackground = true;

        // Clean up stale result file on domain reload
        if (File.Exists(ResultFile))
            File.Delete(ResultFile);

        // Write compile stamp so build.py knows scripts have been recompiled
        File.WriteAllText(CompileStampFile, System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString());

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
                // Read the ART_ID from the trigger file content
                string content = File.ReadAllText(TriggerFile).Trim();
                File.Delete(TriggerFile);
                Debug.Log($"[BuildWatcher] Trigger detected (background thread). ArtId='{content}'");

                if (File.Exists(ResultFile))
                    File.Delete(ResultFile);

                buildArtId = content;
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
        string content = buildArtId ?? "1832:1349";
        try
        {
            // Parse trigger format: "targetArtId:donorArtId"
            string targetArtId, donorArtId;
            if (content.Contains(":"))
            {
                var parts = content.Split(':');
                targetArtId = parts[0].Trim();
                donorArtId = parts[1].Trim();
            }
            else
            {
                // Legacy format: just artId (no donor specified)
                targetArtId = content.Trim();
                donorArtId = targetArtId;
            }

            Debug.Log($"[BuildWatcher] Building bundle: target={targetArtId}, donor={donorArtId}...");
            BuildPremiumBundle.WatcherBuildGeneric(targetArtId, donorArtId);

            File.WriteAllText(ResultFile, "OK");
            Debug.Log($"[BuildWatcher] Build complete for {targetArtId}! Result: OK");
        }
        catch (System.Exception e)
        {
            string msg = $"FAIL: {e.Message}";
            File.WriteAllText(ResultFile, msg);
            Debug.LogError($"[BuildWatcher] {msg}\n{e.StackTrace}");
        }
    }
}
