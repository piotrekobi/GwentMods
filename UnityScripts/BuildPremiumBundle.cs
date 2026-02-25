using UnityEngine;
using UnityEditor;
using System.IO;

public class BuildPremiumBundle
{
    [MenuItem("Assets/Build Premium Bundle - Rivian Broadsword")]
    public static void BuildRivianBroadsword()
    {
        BuildSceneBundle("Assets/BundledAssets/CardAssets/Scenes/14850101.unity", "14850101");
    }

    [MenuItem("Assets/Build Premium Bundle - Elven Wardancer")]
    public static void BuildElvenWardancer()
    {
        // To avoid "another AssetBundle with the same files is already loaded" error,
        // we copy the scene to a unique name.
        string originalPath = "Assets/BundledAssets/CardAssets/Scenes/12220101.unity";
        string uniquePath = "Assets/BundledAssets/CardAssets/Scenes/1832.unity";
        
        AssetDatabase.CopyAsset(originalPath, uniquePath);
        
        try
        {
            BuildSceneBundle(uniquePath, "1832");
        }
        finally
        {
            AssetDatabase.DeleteAsset(uniquePath);
        }
    }

    // Batch-mode build for Elven Deadeye (1832)
    // Called via: Unity.exe -projectPath ... -executeMethod BuildPremiumBundle.BatchBuildElvenDeadeye
    // Uses the generated 1832.unity scene (from GenerateElvenDeadeye). If it doesn't exist,
    // run Assets -> Generate Elven Deadeye Premium Scene first.
    public static void BatchBuildElvenDeadeye()
    {
        string outputDir = @"E:\GOG Galaxy\Games\Gwent\Mods\CustomPremiums\Bundles";
        string scenePath = "Assets/BundledAssets/CardAssets/Scenes/1832.unity";

        if (!Directory.Exists(outputDir))
            Directory.CreateDirectory(outputDir);

        if (!File.Exists(Path.Combine(Application.dataPath, "..", scenePath)))
        {
            Debug.LogError($"[BatchBuild] Scene not found: {scenePath}. Run 'Assets -> Generate Elven Deadeye Premium Scene' first.");
            EditorApplication.Exit(1);
            return;
        }

        BatchBuildSceneBundle(scenePath, "1832", outputDir);
    }

    private static void BatchBuildSceneBundle(string scenePath, string bundleName, string outputDir)
    {
        if (!File.Exists(Path.Combine(Application.dataPath, "..", scenePath)))
        {
            Debug.LogError($"Scene not found: {scenePath}");
            EditorApplication.Exit(1);
            return;
        }

        string outputPath = Path.Combine(outputDir, bundleName);
        Debug.Log($"[BatchBuild] Building: {scenePath} -> {outputPath}");

        var build = new AssetBundleBuild();
        build.assetBundleName = bundleName;
        build.assetNames = new string[] { scenePath };

        var depBundles = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>>();
        string[] allAssetPaths = AssetDatabase.GetAllAssetPaths();
        foreach (string p in allAssetPaths)
        {
            var importer = AssetImporter.GetAtPath(p);
            if (importer != null && !string.IsNullOrEmpty(importer.assetBundleName)
                && importer.assetBundleName.StartsWith("bundledassets/dependencies/"))
            {
                if (!depBundles.ContainsKey(importer.assetBundleName))
                    depBundles[importer.assetBundleName] = new System.Collections.Generic.List<string>();
                depBundles[importer.assetBundleName].Add(p);
            }
        }

        var allBuilds = new System.Collections.Generic.List<AssetBundleBuild>();
        allBuilds.Add(build);

        foreach (var kvp in depBundles)
        {
            var depBuild = new AssetBundleBuild();
            depBuild.assetBundleName = kvp.Key;
            depBuild.assetNames = kvp.Value.ToArray();
            allBuilds.Add(depBuild);
            Debug.Log($"[BatchBuild]   Dependency: {kvp.Key} ({kvp.Value.Count} assets)");
        }

        var manifest = BuildPipeline.BuildAssetBundles(
            outputDir,
            allBuilds.ToArray(),
            BuildAssetBundleOptions.UncompressedAssetBundle,
            BuildTarget.StandaloneWindows64);

        if (manifest != null)
        {
            string manifestFile = outputPath + ".manifest";
            string dirBundle = Path.Combine(outputDir, Path.GetFileName(outputDir));
            string dirManifest = dirBundle + ".manifest";

            if (File.Exists(manifestFile)) File.Delete(manifestFile);
            if (File.Exists(dirBundle)) File.Delete(dirBundle);
            if (File.Exists(dirManifest)) File.Delete(dirManifest);

            // Also clean up dependency bundle manifests
            foreach (var kvp in depBundles)
            {
                string depManifest = Path.Combine(outputDir, kvp.Key + ".manifest");
                if (File.Exists(depManifest)) File.Delete(depManifest);
            }

            long size = new FileInfo(outputPath).Length;
            Debug.Log($"[BatchBuild] SUCCESS! {size} bytes -> {outputPath}");
        }
        else
        {
            Debug.LogError("[BatchBuild] FAILED!");
            EditorApplication.Exit(1);
        }
    }

    [MenuItem("Assets/Build Premium Bundle - Custom Scene")]
    public static void BuildCustomScene()
    {
        // Open a file dialog to select which scene to build
        string scenePath = EditorUtility.OpenFilePanel(
            "Select Premium Scene to Build",
            "Assets/BundledAssets/CardAssets/Scenes",
            "unity");

        if (string.IsNullOrEmpty(scenePath)) return;

        // Convert absolute path to project-relative path
        string projectPath = Application.dataPath;
        if (scenePath.StartsWith(projectPath))
        {
            scenePath = "Assets" + scenePath.Substring(projectPath.Length);
        }

        string bundleName = Path.GetFileNameWithoutExtension(scenePath);
        BuildSceneBundle(scenePath, bundleName);
    }

    private static void BuildSceneBundle(string scenePath, string bundleName)
    {
        // Verify scene exists
        if (!File.Exists(Path.Combine(Application.dataPath, "..", scenePath)))
        {
            Debug.LogError($"Scene not found: {scenePath}");
            EditorUtility.DisplayDialog("Error", $"Scene not found:\n{scenePath}", "OK");
            return;
        }

        // Ask where to save
        string outputDir = EditorUtility.OpenFolderPanel(
            "Select Output Directory for Bundle",
            @"E:\GOG Galaxy\Games\Gwent\Mods\CustomPremiums\Bundles",
            "");

        if (string.IsNullOrEmpty(outputDir)) return;

        string outputPath = Path.Combine(outputDir, bundleName);

        Debug.Log($"Building scene bundle: {scenePath} -> {outputPath}");

        // Create the AssetBundle build
        var build = new AssetBundleBuild();
        build.assetBundleName = bundleName;
        build.assetNames = new string[] { scenePath };

        // Construct dependency bundles for external shader references.
        // Scans all assets for unique assetBundleNames that look like dependencies,
        // and builds a separate (empty) bundle for each so Unity creates external refs.
        var depBundles = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>>();
        
        string[] allAssetPaths = AssetDatabase.GetAllAssetPaths();
        foreach (string p in allAssetPaths)
        {
            var importer = AssetImporter.GetAtPath(p);
            if (importer != null && !string.IsNullOrEmpty(importer.assetBundleName) 
                && importer.assetBundleName.StartsWith("bundledassets/dependencies/"))
            {
                if (!depBundles.ContainsKey(importer.assetBundleName))
                    depBundles[importer.assetBundleName] = new System.Collections.Generic.List<string>();
                depBundles[importer.assetBundleName].Add(p);
            }
        }

        // Build the array: main bundle + all dependency bundles
        var allBuilds = new System.Collections.Generic.List<AssetBundleBuild>();
        allBuilds.Add(build);

        foreach (var kvp in depBundles)
        {
            var depBuild = new AssetBundleBuild();
            depBuild.assetBundleName = kvp.Key;
            depBuild.assetNames = kvp.Value.ToArray();
            allBuilds.Add(depBuild);
            Debug.Log($"  Dependency bundle: {kvp.Key} ({kvp.Value.Count} assets)");
        }

        // Build for Windows Standalone (matching Gwent's target)
        var manifest = BuildPipeline.BuildAssetBundles(
            outputDir,
            allBuilds.ToArray(),
            BuildAssetBundleOptions.UncompressedAssetBundle,
            BuildTarget.StandaloneWindows64);

        if (manifest != null)
        {
            // Clean up extra files Unity generates
            string manifestFile = outputPath + ".manifest";
            string dirBundle = Path.Combine(outputDir, Path.GetFileName(outputDir));
            string dirManifest = dirBundle + ".manifest";

            if (File.Exists(manifestFile)) File.Delete(manifestFile);
            if (File.Exists(dirBundle)) File.Delete(dirBundle);
            if (File.Exists(dirManifest)) File.Delete(dirManifest);

            long size = new FileInfo(outputPath).Length;
            Debug.Log($"Bundle built successfully! {size} bytes -> {outputPath}");
            EditorUtility.DisplayDialog("Success",
                $"Bundle built!\n\n{outputPath}\n{size} bytes", "OK");
        }
        else
        {
            Debug.LogError("Bundle build failed!");
            EditorUtility.DisplayDialog("Error", "Bundle build failed! Check console for errors.", "OK");
        }
    }
}
