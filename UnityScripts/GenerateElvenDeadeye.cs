using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Animations;
using System.IO;
using System.Collections.Generic;

using GwentUnity;
using GwentVisuals;

/// <summary>
/// Generates the complete Elven Deadeye (ArtId 1832) premium card scene.
/// Uses 4 separate MeshRenderer quads for parallax layers, each at a different
/// Z depth. Transparent compositing relies on Unity's distance-based sorting
/// (back-to-front) since all quads share the same render queue.
///
/// Architecture:
/// - Root -> Pivot (0,-2,0) -> model -> 4 layer quads (MeshFilter+MeshRenderer)
/// - Each quad has a custom mesh with UVs pointing to its atlas quadrant
/// - Quads animate independently via bone-like transforms for parallax
/// - Single 2048x2048 square atlas texture (matching uber quality)
/// - One GwentStandard material in Transparent mode (_Mode=3, _SrcBlend=1)
/// - Single PremiumTextureAssigment with all 4 renderers
///
/// Usage: Unity menu -> Assets -> Generate Elven Deadeye Premium Scene
///
/// After running, build via: Assets -> Build Premium Bundle - Custom Scene
/// Then run: python patch_bundle_shaders.py
/// </summary>
public class GenerateElvenDeadeye
{
    // =========================================================================
    // CONFIGURATION - adjust these if the card doesn't fill the viewport correctly
    // =========================================================================
    const string ART_ID = "1832";

    // Source files
    static readonly string LAYERS_SOURCE = @"E:\Projekty\GwentMods\layers";
    static readonly string ATLAS_SOURCE = @"E:\Projekty\GwentMods\1832_composite.png";
    const string ATLAS_FILENAME = "atlas.png";

    // Deployment paths
    static readonly string MODS_DIR = @"E:\Projekty\GwentMods";
    static readonly string BUNDLE_OUTPUT_DIR = @"E:\GOG Galaxy\Games\Gwent\Mods\CustomPremiums\Bundles";
    static readonly string PYTHON_EXE = "python"; // must be on PATH

    // Unity project paths
    const string ASSETS_DIR = "Assets/PremiumCards/Custom/ElvenDeadeye";
    const string TEXTURES_DIR = ASSETS_DIR + "/Textures";
    const string MATERIALS_DIR = ASSETS_DIR + "/Materials";
    const string ANIMATIONS_DIR = ASSETS_DIR + "/Animations";
    const string MESHES_DIR = ASSETS_DIR + "/Meshes";
    const string SCENE_DIR = "Assets/BundledAssets/CardAssets/Scenes";
    static readonly string SCENE_PATH = SCENE_DIR + "/" + ART_ID + ".unity";

    // Quad scale: based on Dryad Ranger's coordinate system where
    // leaf quads are ~10.6x10.6 and LensPostFX is ~15.5x15.5.
    // Our layers are ~1:2 aspect ratio (card proportions).
    // Slightly oversized to prevent edge exposure during parallax sway.
    static readonly Vector3 QUAD_SCALE = new Vector3(14f, 26f, 1f);

    // Root position: vanilla premium scenes use y=-10000 as staging area.
    // The game repositions the object when displaying the card.
    static readonly Vector3 ROOT_POSITION = new Vector3(0f, -10000f, 0f);

    // Animation timing
    const float LOOP_DURATION = 13f;
    const float INTRO_DURATION = 0.567f; // matches Dryad Ranger intro length

    // Layer definitions: name, Z depth, parallax amplitudes, UV rect, render queue.
    // Camera looks from -Z toward +Z (based on Dryad Ranger analysis).
    // Background at positive Z (far), foreground at negative Z (near camera).
    //
    // UV coordinates (Unity bottom-left origin) for each quadrant in the
    // 2048x2048 atlas. Each quad mesh has these UVs baked into vertices,
    // so the material uses tiling (1,1) offset (0,0) — same as vanilla.
    //
    // Render queues increase front-to-back so transparent layers composite
    // correctly (background renders first, foreground last).
    static readonly LayerDef[] LAYERS = {
        new LayerDef("far_background",  4.0f,  0.04f,  0.02f,   0.0f, 0.5f, 0.5f, 1.0f,  3000),
        new LayerDef("tree_trunk",      2.0f,  0.07f,  0.035f,  0.5f, 0.5f, 1.0f, 1.0f,  3001),
        new LayerDef("elf",             0.0f,  0.10f,  0.05f,   0.0f, 0.0f, 0.5f, 0.5f,  3002),
        new LayerDef("log",            -2.0f,  0.15f,  0.07f,   0.5f, 0.0f, 1.0f, 0.5f,  3003),
    };

    struct LayerDef
    {
        public string name;
        public float z, parallaxX, parallaxY;
        public float uvMinX, uvMinY, uvMaxX, uvMaxY;
        public int renderQueue;
        public LayerDef(string n, float z, float px, float py,
                        float uMinX, float uMinY, float uMaxX, float uMaxY, int rq)
        {
            name = n; this.z = z; parallaxX = px; parallaxY = py;
            uvMinX = uMinX; uvMinY = uMinY; uvMaxX = uMaxX; uvMaxY = uMaxY;
            renderQueue = rq;
        }
    }

    // =========================================================================
    // ENTRY POINTS
    // =========================================================================

    /// <summary>
    /// One-click: generates atlas, scene, builds bundle, patches shaders, deploys.
    /// </summary>
    [MenuItem("Assets/Elven Deadeye - Build && Deploy")]
    static void BuildAndDeploy()
    {
        if (!EditorUtility.DisplayDialog("Elven Deadeye — Build & Deploy",
            "This will:\n" +
            "1. Create texture atlas\n" +
            "2. Generate premium scene\n" +
            "3. Build asset bundle\n" +
            "4. Patch shaders\n" +
            "5. Deploy to game folder\n\nProceed?",
            "Build & Deploy", "Cancel"))
            return;

        var log = new List<string>();
        bool ok = true;

        try
        {
            // Step 1: Create atlas via Python
            EditorUtility.DisplayProgressBar("Elven Deadeye", "Creating texture atlas...", 0.05f);
            ok = RunPython("create_composite.py", log);
            if (!ok) { ShowError("Atlas creation failed", log); return; }

            // Step 2: Clean old assets
            EditorUtility.DisplayProgressBar("Elven Deadeye", "Cleaning old assets...", 0.10f);
            if (AssetDatabase.IsValidFolder(ASSETS_DIR))
            {
                AssetDatabase.DeleteAsset(ASSETS_DIR);
                log.Add("Deleted old assets: " + ASSETS_DIR);
            }
            if (File.Exists(Path.Combine(Application.dataPath, "..", SCENE_PATH)))
            {
                AssetDatabase.DeleteAsset(SCENE_PATH);
                log.Add("Deleted old scene: " + SCENE_PATH);
            }
            AssetDatabase.Refresh();

            // Step 3: Generate scene
            EditorUtility.DisplayProgressBar("Elven Deadeye", "Generating scene...", 0.20f);
            GenerateScene(log);

            // Step 4: Build asset bundle
            EditorUtility.DisplayProgressBar("Elven Deadeye", "Building asset bundle...", 0.60f);
            ok = BuildBundle(log);
            if (!ok) { ShowError("Bundle build failed", log); return; }

            // Step 5: Patch shaders via Python
            EditorUtility.DisplayProgressBar("Elven Deadeye", "Patching shaders...", 0.85f);
            string bundlePath = Path.Combine(BUNDLE_OUTPUT_DIR, ART_ID);
            ok = RunPython($"patch_bundle_shaders.py \"{bundlePath}\"", log);
            if (!ok) { ShowError("Shader patching failed", log); return; }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        string summary = string.Join("\n", log);
        UnityEngine.Debug.Log($"[ElvenDeadeye] Build & Deploy complete:\n{summary}");
        EditorUtility.DisplayDialog("Build & Deploy Complete",
            "Elven Deadeye deployed successfully!\n\nLaunch Gwent to test.",
            "OK");
    }

    /// <summary>
    /// Generate scene only (no build/deploy). Useful for iterating on scene setup.
    /// </summary>
    [MenuItem("Assets/Elven Deadeye - Generate Scene Only")]
    static void GenerateOnly()
    {
        if (!EditorUtility.DisplayDialog("Generate Elven Deadeye",
            $"This will create the premium scene at:\n{SCENE_PATH}\n\nProceed?",
            "Generate", "Cancel"))
            return;

        var log = new List<string>();
        try
        {
            GenerateScene(log);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        string summary = string.Join("\n", log);
        UnityEngine.Debug.Log($"[ElvenDeadeye] Generation complete:\n{summary}");
        EditorUtility.DisplayDialog("Done", "Scene generated at:\n" + SCENE_PATH, "OK");
    }

    // =========================================================================
    // CORE PIPELINE
    // =========================================================================

    static void GenerateScene(List<string> log)
    {
        EditorUtility.DisplayProgressBar("Elven Deadeye", "Creating folders...", 0.20f);
        CreateFolderHierarchy(TEXTURES_DIR);
        CreateFolderHierarchy(MATERIALS_DIR);
        CreateFolderHierarchy(ANIMATIONS_DIR);
        CreateFolderHierarchy(MESHES_DIR);
        CreateFolderHierarchy(SCENE_DIR);

        EditorUtility.DisplayProgressBar("Elven Deadeye", "Importing textures...", 0.25f);
        CopyAndImportTextures();

        EditorUtility.DisplayProgressBar("Elven Deadeye", "Creating layer meshes...", 0.30f);
        var layerMeshes = CreateLayerMeshes();

        EditorUtility.DisplayProgressBar("Elven Deadeye", "Creating materials...", 0.35f);
        var materials = CreateAllMaterials();

        EditorUtility.DisplayProgressBar("Elven Deadeye", "Creating animations...", 0.40f);
        var controller = CreateAnimatorController();

        EditorUtility.DisplayProgressBar("Elven Deadeye", "Building scene...", 0.50f);
        CreateScene(materials, layerMeshes, controller);

        EditorUtility.DisplayProgressBar("Elven Deadeye", "Setting asset bundle tags...", 0.55f);
        SetAssetBundleTags();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        log.Add("Scene generated: " + SCENE_PATH);
    }

    static bool BuildBundle(List<string> log)
    {
        if (!Directory.Exists(BUNDLE_OUTPUT_DIR))
            Directory.CreateDirectory(BUNDLE_OUTPUT_DIR);

        // Main scene bundle
        var build = new AssetBundleBuild();
        build.assetBundleName = ART_ID;
        build.assetNames = new string[] { SCENE_PATH };

        // Dependency bundles (shader externals)
        var allBuilds = new List<AssetBundleBuild>();
        allBuilds.Add(build);

        string[] allAssetPaths = AssetDatabase.GetAllAssetPaths();
        var depBundles = new Dictionary<string, List<string>>();
        foreach (string p in allAssetPaths)
        {
            var importer = AssetImporter.GetAtPath(p);
            if (importer != null && !string.IsNullOrEmpty(importer.assetBundleName)
                && importer.assetBundleName.StartsWith("bundledassets/dependencies/"))
            {
                if (!depBundles.ContainsKey(importer.assetBundleName))
                    depBundles[importer.assetBundleName] = new List<string>();
                depBundles[importer.assetBundleName].Add(p);
            }
        }

        foreach (var kvp in depBundles)
        {
            var depBuild = new AssetBundleBuild();
            depBuild.assetBundleName = kvp.Key;
            depBuild.assetNames = kvp.Value.ToArray();
            allBuilds.Add(depBuild);
        }

        var manifest = BuildPipeline.BuildAssetBundles(
            BUNDLE_OUTPUT_DIR,
            allBuilds.ToArray(),
            BuildAssetBundleOptions.UncompressedAssetBundle,
            BuildTarget.StandaloneWindows64);

        if (manifest == null)
        {
            log.Add("ERROR: Bundle build returned null manifest");
            return false;
        }

        // Clean up Unity's extra files
        string bundlePath = Path.Combine(BUNDLE_OUTPUT_DIR, ART_ID);
        string manifestFile = bundlePath + ".manifest";
        string dirBundle = Path.Combine(BUNDLE_OUTPUT_DIR, Path.GetFileName(BUNDLE_OUTPUT_DIR));
        string dirManifest = dirBundle + ".manifest";

        if (File.Exists(manifestFile)) File.Delete(manifestFile);
        if (File.Exists(dirBundle)) File.Delete(dirBundle);
        if (File.Exists(dirManifest)) File.Delete(dirManifest);

        // Also clean dependency bundle artifacts
        foreach (var kvp in depBundles)
        {
            string depPath = Path.Combine(BUNDLE_OUTPUT_DIR, kvp.Key);
            string depManifest = depPath + ".manifest";
            string depDir = Path.GetDirectoryName(depPath);
            if (File.Exists(depPath)) File.Delete(depPath);
            if (File.Exists(depManifest)) File.Delete(depManifest);
            // Clean empty directories left by dependency bundles
            if (Directory.Exists(depDir) && Directory.GetFiles(depDir).Length == 0
                && Directory.GetDirectories(depDir).Length == 0)
                Directory.Delete(depDir);
        }

        long size = new FileInfo(bundlePath).Length;
        log.Add($"Bundle built: {bundlePath} ({size} bytes)");
        return true;
    }

    static bool RunPython(string scriptAndArgs, List<string> log)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = PYTHON_EXE,
            Arguments = scriptAndArgs,
            WorkingDirectory = MODS_DIR,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        try
        {
            var proc = System.Diagnostics.Process.Start(psi);
            string stdout = proc.StandardOutput.ReadToEnd();
            string stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit();

            if (!string.IsNullOrEmpty(stdout))
                UnityEngine.Debug.Log($"[Python] {stdout}");
            if (!string.IsNullOrEmpty(stderr))
                UnityEngine.Debug.LogWarning($"[Python stderr] {stderr}");

            if (proc.ExitCode != 0)
            {
                log.Add($"Python error (exit {proc.ExitCode}): {stderr}");
                return false;
            }

            log.Add($"Python OK: {scriptAndArgs.Split(' ')[0]}");
            return true;
        }
        catch (System.Exception ex)
        {
            log.Add($"Python failed to start: {ex.Message}");
            UnityEngine.Debug.LogError($"[ElvenDeadeye] Failed to run Python: {ex}");
            return false;
        }
    }

    static void ShowError(string title, List<string> log)
    {
        EditorUtility.ClearProgressBar();
        string details = string.Join("\n", log);
        UnityEngine.Debug.LogError($"[ElvenDeadeye] {title}:\n{details}");
        EditorUtility.DisplayDialog("Error: " + title,
            "Check the Unity console for details.\n\n" + details, "OK");
    }

    // =========================================================================
    // FOLDER CREATION
    // =========================================================================
    static void CreateFolderHierarchy(string path)
    {
        string[] parts = path.Split('/');
        string current = parts[0]; // "Assets"
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

    // =========================================================================
    // TEXTURE IMPORT
    // =========================================================================
    static void CopyAndImportTextures()
    {
        string dstDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", TEXTURES_DIR));
        if (!Directory.Exists(dstDir)) Directory.CreateDirectory(dstDir);

        // Copy atlas (2x2 grid of all layer textures — matches CDPR's UV atlas approach)
        if (File.Exists(ATLAS_SOURCE))
        {
            File.Copy(ATLAS_SOURCE, Path.Combine(dstDir, ATLAS_FILENAME), true);
            Debug.Log($"[ElvenDeadeye] Copied atlas from {ATLAS_SOURCE}");
        }
        else
        {
            Debug.LogError($"[ElvenDeadeye] Atlas not found: {ATLAS_SOURCE}\n" +
                "Run 'python create_composite.py' first to generate the atlas.");
        }

        // Copy leaves texture (particle system, separate from atlas)
        string leavesSrc = Path.Combine(LAYERS_SOURCE, "leaves.png");
        if (File.Exists(leavesSrc))
            File.Copy(leavesSrc, Path.Combine(dstDir, "leaves.png"), true);
        else
            Debug.LogWarning($"[ElvenDeadeye] Leaves texture not found: {leavesSrc}");

        AssetDatabase.Refresh();

        // Configure atlas import (2048x2048 square, matching vanilla uber quality)
        ConfigureTextureImport(TEXTURES_DIR + "/" + ATLAS_FILENAME, 2048);

        // Configure leaves import (particle sprites, smaller)
        ConfigureTextureImport(TEXTURES_DIR + "/leaves.png", 512);
    }

    static void ConfigureTextureImport(string assetPath, int maxSize)
    {
        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null)
        {
            Debug.LogWarning($"[ElvenDeadeye] Could not get importer for {assetPath}");
            return;
        }

        importer.textureType = TextureImporterType.Default;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.mipmapEnabled = false;
        importer.isReadable = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = maxSize;
        importer.SaveAndReimport();
        Debug.Log($"[ElvenDeadeye] Imported texture: {assetPath} (max {maxSize})");
    }

    // =========================================================================
    // MESH CREATION — one quad mesh per parallax layer
    // =========================================================================

    /// <summary>
    /// Creates one quad mesh per layer. Each mesh has 4 vertices at Z=0
    /// (the Z position comes from the parent transform) with UVs pointing
    /// to the correct atlas quadrant. Separate MeshRenderers guarantee
    /// compatibility with the game's Il2Cpp rendering pipeline.
    /// </summary>
    static Mesh[] CreateLayerMeshes()
    {
        Mesh[] meshes = new Mesh[LAYERS.Length];
        float halfW = QUAD_SCALE.x / 2f;  // 7
        float halfH = QUAD_SCALE.y / 2f;  // 13

        for (int i = 0; i < LAYERS.Length; i++)
        {
            LayerDef layer = LAYERS[i];
            Mesh mesh = new Mesh();
            mesh.name = $"ElvenDeadeye_{layer.name}";

            // Quad vertices at Z=0 (Z position comes from parent transform)
            mesh.vertices = new Vector3[] {
                new Vector3(-halfW, -halfH, 0f),
                new Vector3( halfW, -halfH, 0f),
                new Vector3( halfW,  halfH, 0f),
                new Vector3(-halfW,  halfH, 0f),
            };

            // UVs for this layer's atlas quadrant
            mesh.uv = new Vector2[] {
                new Vector2(layer.uvMinX, layer.uvMinY),
                new Vector2(layer.uvMaxX, layer.uvMinY),
                new Vector2(layer.uvMaxX, layer.uvMaxY),
                new Vector2(layer.uvMinX, layer.uvMaxY),
            };

            // Normals facing -Z (toward camera)
            mesh.normals = new Vector3[] {
                -Vector3.forward, -Vector3.forward, -Vector3.forward, -Vector3.forward,
            };

            // Front face toward camera (-Z)
            mesh.triangles = new int[] { 0, 2, 1, 0, 3, 2 };

            mesh.RecalculateBounds();

            AssetDatabase.CreateAsset(mesh, MESHES_DIR + $"/ElvenDeadeye_{layer.name}.asset");
            meshes[i] = mesh;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[ElvenDeadeye] Created {meshes.Length} layer meshes (4 verts each)");
        return meshes;
    }

    // =========================================================================
    // MATERIAL CREATION
    // =========================================================================
    static Dictionary<string, Material> CreateAllMaterials()
    {
        var materials = new Dictionary<string, Material>();

        // Find dummy shaders (must already exist in project from previous pipeline work)
        Shader gwentStandard = Shader.Find("ShaderLibrary/Generic/GwentStandard");
        Shader alphaBlended = Shader.Find("VFX/Common/AlphaBlended");
        Shader additiveMask = Shader.Find("VFX/Effects/FakePostEffect/Additive_Mask");

        if (gwentStandard == null)
        {
            Debug.LogError("[ElvenDeadeye] GwentStandard dummy shader not found! " +
                "Ensure Assets/DummyShaders/ contains the GUID-spoofed shaders.");
            return materials;
        }

        // --- Load atlas texture ---
        Texture2D atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(
            TEXTURES_DIR + "/" + ATLAS_FILENAME);
        if (atlas == null)
        {
            Debug.LogError("[ElvenDeadeye] Atlas texture not found! " +
                "Run 'python create_composite.py' and re-run generation.");
            return materials;
        }
        Debug.Log($"[ElvenDeadeye] Loaded atlas: {atlas.width}x{atlas.height}");

        // --- Atlas material (GwentStandard, Transparent mode) ---
        // Matches Dryad Ranger's [13490300]DryadRanger-Atlas material EXACTLY.
        // Single material on the SkinnedMeshRenderer. Mesh UV coordinates handle
        // atlas quadrant mapping; tiling/offset stay at (1,1)/(0,0).
        // Draw order is deterministic: index buffer is ordered back-to-front.
        {
            Material mat = new Material(gwentStandard);
            mat.name = "ElvenDeadeye-Atlas";

            // Atlas texture — tiling/offset at default (1,1)/(0,0)
            mat.SetTexture("_MainTex", atlas);

            // Fade mode (Mode 2) with SrcAlpha/OneMinusSrcAlpha — matches the
            // vanilla ElvenWardancer's Alpha_lend transparent overlay materials.
            // Mode 2 uses standard alpha blending (SrcAlpha, not premultiplied One).
            // No shader keywords needed — the vanilla materials work without any.
            mat.SetFloat("_BumpScale", 1f);
            mat.SetFloat("_Cutoff", 0.5f);
            mat.SetFloat("_DetailNormalMapScale", 1f);
            mat.SetFloat("_DstBlend", 10f);           // OneMinusSrcAlpha
            mat.SetFloat("_GlossMapScale", 1f);
            mat.SetFloat("_Glossiness", 0f);
            mat.SetFloat("_GlossyReflections", 1f);
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_Mode", 2f);                // Fade (standard alpha blend)
            mat.SetFloat("_OcclusionStrength", 1f);
            mat.SetFloat("_Parallax", 0.02f);
            mat.SetFloat("_SmoothnessTextureChannel", 0f);
            mat.SetFloat("_SpecularHighlights", 1f);
            mat.SetFloat("_SrcBlend", 5f);            // SrcAlpha
            mat.SetFloat("_UVSec", 0f);
            mat.SetFloat("_ZWrite", 0f);

            mat.SetColor("_Color", Color.white);
            mat.SetColor("_EmissionColor", Color.black);

            // Use shader default render queue (matches Dryad Ranger's -1)
            mat.renderQueue = -1;

            string matPath = MATERIALS_DIR + "/" + mat.name + ".mat";
            AssetDatabase.CreateAsset(mat, matPath);
            materials["atlas"] = mat;
            Debug.Log($"[ElvenDeadeye] Created atlas material: {mat.name}");
        }

        // --- Leaf particle material (AlphaBlended) ---
        if (alphaBlended != null)
        {
            Material leafMat = new Material(alphaBlended);
            leafMat.name = "ElvenDeadeye_leaf_all";

            Texture2D leafTex = AssetDatabase.LoadAssetAtPath<Texture2D>(
                TEXTURES_DIR + "/leaves.png");
            if (leafTex != null)
                leafMat.SetTexture("_MainTex", leafTex);

            leafMat.renderQueue = 3100;
            AssetDatabase.CreateAsset(leafMat, MATERIALS_DIR + "/" + leafMat.name + ".mat");
            materials["leaf_particles"] = leafMat;
            Debug.Log($"[ElvenDeadeye] Created material: {leafMat.name}");
        }
        else
        {
            Debug.LogWarning("[ElvenDeadeye] AlphaBlended dummy shader not found, skipping leaf material");
        }

        // --- Lens post-effect material (Additive_Mask) ---
        if (additiveMask != null)
        {
            Material lensMat = new Material(additiveMask);
            lensMat.name = "ElvenDeadeye_LensPostFX";

            // Warm forest tint matching Dryad Ranger: (0.612, 0.618, 0.522, 1.0)
            lensMat.SetColor("_TintColor", new Color(0.612f, 0.618f, 0.522f, 1.0f));
            lensMat.renderQueue = 3000;

            AssetDatabase.CreateAsset(lensMat, MATERIALS_DIR + "/" + lensMat.name + ".mat");
            materials["lens_postfx"] = lensMat;
            Debug.Log($"[ElvenDeadeye] Created material: {lensMat.name}");
        }
        else
        {
            Debug.LogWarning("[ElvenDeadeye] Additive_Mask dummy shader not found, skipping lens material");
        }

        AssetDatabase.SaveAssets();
        return materials;
    }

    // =========================================================================
    // ANIMATION
    // =========================================================================
    static AnimatorController CreateAnimatorController()
    {
        string controllerPath = ANIMATIONS_DIR + "/ElvenDeadeye_AC.controller";

        // Create clips first
        AnimationClip introClip = CreateIntroClip();
        AnimationClip loopClip = CreateLoopClip();

        AssetDatabase.CreateAsset(introClip, ANIMATIONS_DIR + "/ElvenDeadeye_Intro.anim");
        AssetDatabase.CreateAsset(loopClip, ANIMATIONS_DIR + "/ElvenDeadeye_Loop.anim");

        // Create controller
        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        AnimatorStateMachine sm = controller.layers[0].stateMachine;

        // Intro state (plays once, then auto-transitions to Loop)
        AnimatorState introState = sm.AddState("Intro");
        introState.motion = introClip;

        // Loop state (loops forever)
        AnimatorState loopState = sm.AddState("Loop");
        loopState.motion = loopClip;

        // Auto-transition: Intro -> Loop after clip finishes
        AnimatorStateTransition transition = introState.AddTransition(loopState);
        transition.hasExitTime = true;
        transition.exitTime = 1.0f;
        transition.duration = 0f;
        transition.hasFixedDuration = true;

        sm.defaultState = introState;
        AssetDatabase.SaveAssets();

        Debug.Log("[ElvenDeadeye] Created animator controller with Intro -> Loop");
        return controller;
    }

    static AnimationClip CreateIntroClip()
    {
        AnimationClip clip = new AnimationClip();
        clip.name = "ElvenDeadeye_Intro";

        // During intro: all layer quads are in their final positions (no movement).
        // VFX elements start disabled and enable partway through.
        // Paths are relative to Pivot (where the Animator lives).
        foreach (var layer in LAYERS)
        {
            string path = "model/" + layer.name;
            clip.SetCurve(path, typeof(Transform), "localPosition.x",
                AnimationCurve.Constant(0f, INTRO_DURATION, 0f));
            clip.SetCurve(path, typeof(Transform), "localPosition.y",
                AnimationCurve.Constant(0f, INTRO_DURATION, 0f));
            clip.SetCurve(path, typeof(Transform), "localPosition.z",
                AnimationCurve.Constant(0f, INTRO_DURATION, layer.z));
        }

        // VFX: enable after brief delay (leaf particles activate at 0.2s)
        clip.SetCurve("VFX/leaf_particles", typeof(GameObject), "m_IsActive",
            CreateActivationCurve(0.2f, INTRO_DURATION));

        // VFX: LensPostFX enables at 0.3s
        clip.SetCurve("VFX/ElvenDeadeye_LensPostFX", typeof(GameObject), "m_IsActive",
            CreateActivationCurve(0.3f, INTRO_DURATION));

        return clip;
    }

    static AnimationClip CreateLoopClip()
    {
        AnimationClip clip = new AnimationClip();
        clip.name = "ElvenDeadeye_Loop";

        // Mark as looping
        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        // Parallax sway: each layer oscillates on X and Y with different amplitudes.
        // Layers further from the focal plane (elf) move more, creating depth illusion.
        // Paths are relative to Pivot (where the Animator lives).
        foreach (var layer in LAYERS)
        {
            string path = "model/" + layer.name;

            // X sway: full sine cycle over LOOP_DURATION
            clip.SetCurve(path, typeof(Transform), "localPosition.x",
                CreateSineCurve(LOOP_DURATION, layer.parallaxX, 0f));

            // Y sway: slower cycle, phase-offset for organic feel
            clip.SetCurve(path, typeof(Transform), "localPosition.y",
                CreateSineCurve(LOOP_DURATION, layer.parallaxY, Mathf.PI * 0.4f));

            // Z stays constant
            clip.SetCurve(path, typeof(Transform), "localPosition.z",
                AnimationCurve.Constant(0f, LOOP_DURATION, layer.z));
        }

        // Keep VFX active during loop
        clip.SetCurve("VFX/leaf_particles", typeof(GameObject), "m_IsActive",
            AnimationCurve.Constant(0f, LOOP_DURATION, 1f));
        clip.SetCurve("VFX/ElvenDeadeye_LensPostFX", typeof(GameObject), "m_IsActive",
            AnimationCurve.Constant(0f, LOOP_DURATION, 1f));

        return clip;
    }

    /// <summary>
    /// Creates a smooth sine wave AnimationCurve with proper tangents for seamless looping.
    /// </summary>
    static AnimationCurve CreateSineCurve(float duration, float amplitude, float phaseOffset)
    {
        AnimationCurve curve = new AnimationCurve();
        int segments = 32; // enough keyframes for smooth sine

        for (int i = 0; i <= segments; i++)
        {
            float t = (float)i / segments * duration;
            float angle = 2f * Mathf.PI * ((float)i / segments) + phaseOffset;
            float value = amplitude * Mathf.Sin(angle);

            // Analytical derivative of sin for smooth tangents
            float tangent = amplitude * (2f * Mathf.PI / duration) * Mathf.Cos(angle);

            Keyframe key = new Keyframe(t, value);
            key.inTangent = tangent;
            key.outTangent = tangent;
            curve.AddKey(key);
        }

        // Ensure seamless loop: first and last keyframe values match
        curve.preWrapMode = WrapMode.Loop;
        curve.postWrapMode = WrapMode.Loop;

        return curve;
    }

    /// <summary>
    /// Creates an activation curve: off until activateTime, then on.
    /// Uses step keys (no interpolation) for boolean-like behavior.
    /// </summary>
    static AnimationCurve CreateActivationCurve(float activateTime, float clipDuration)
    {
        AnimationCurve curve = new AnimationCurve();

        Keyframe k0 = new Keyframe(0f, 0f);
        k0.outTangent = 0f;
        k0.inTangent = 0f;
        curve.AddKey(k0);

        Keyframe k1 = new Keyframe(activateTime - 0.001f, 0f);
        k1.outTangent = 0f;
        k1.inTangent = 0f;
        curve.AddKey(k1);

        Keyframe k2 = new Keyframe(activateTime, 1f);
        k2.outTangent = 0f;
        k2.inTangent = 0f;
        curve.AddKey(k2);

        Keyframe k3 = new Keyframe(clipDuration, 1f);
        k3.outTangent = 0f;
        k3.inTangent = 0f;
        curve.AddKey(k3);

        return curve;
    }

    // =========================================================================
    // SCENE CONSTRUCTION
    // =========================================================================
    static void CreateScene(Dictionary<string, Material> materials,
                            Mesh[] layerMeshes,
                            AnimatorController controller)
    {
        // Create new empty scene
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // Collect all components for CardAppearance registration
        List<Renderer> allRenderers = new List<Renderer>();
        List<Animator> allAnimators = new List<Animator>();
        List<ParticleSystem> allParticles = new List<ParticleSystem>();

        // Layer renderers for PremiumCardsMeshMaterialHandler
        List<Renderer> layerRenderers = new List<Renderer>();

        // --- Root GameObject ---
        // Named after ArtId. Placed at y=-10000 (vanilla convention for staging).
        // Layer 8: the game's premium card camera renders this layer.
        GameObject root = new GameObject(ART_ID);
        root.transform.position = ROOT_POSITION;
        root.layer = 8;

        // --- Pivot node (matches Dryad Ranger hierarchy) ---
        // Pivot is offset by (0,-2,0) matching vanilla.
        // Animator on Pivot drives all child animations.
        GameObject pivot = new GameObject("Pivot");
        pivot.layer = 8;
        pivot.transform.SetParent(root.transform, false);
        pivot.transform.localPosition = new Vector3(0f, -2f, 0f);

        Animator animator = pivot.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        allAnimators.Add(animator);

        // --- model node (container for layer quads) ---
        GameObject model = new GameObject("model");
        model.layer = 8;
        model.transform.SetParent(pivot.transform, false);
        model.transform.localPosition = Vector3.zero;

        Material atlasMat = materials.ContainsKey("atlas") ? materials["atlas"] : null;

        // --- Layer quad GameObjects (one per parallax layer) ---
        // Each layer is a child of model with its own MeshFilter + MeshRenderer.
        // Positioned at its Z depth; animations move X/Y for parallax.
        // Transparent compositing handled by Unity's distance-based sorting
        // (all share the same render queue, sorted back-to-front by Z distance).
        for (int i = 0; i < LAYERS.Length; i++)
        {
            LayerDef layer = LAYERS[i];

            GameObject layerObj = new GameObject(layer.name);
            layerObj.layer = 8;
            layerObj.transform.SetParent(model.transform, false);
            layerObj.transform.localPosition = new Vector3(0f, 0f, layer.z);

            MeshFilter mf = layerObj.AddComponent<MeshFilter>();
            mf.sharedMesh = layerMeshes[i];

            MeshRenderer mr = layerObj.AddComponent<MeshRenderer>();
            mr.sharedMaterial = atlasMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            allRenderers.Add(mr);
            layerRenderers.Add(mr);
        }

        // --- VFX Container (under Pivot, matching Dryad Ranger) ---
        GameObject vfx = new GameObject("VFX");
        vfx.layer = 8;
        vfx.transform.SetParent(pivot.transform, false);
        vfx.transform.localPosition = Vector3.zero;

        // Leaf particle system
        if (materials.ContainsKey("leaf_particles"))
        {
            var leafResult = CreateLeafParticles(vfx.transform, materials["leaf_particles"]);
            allParticles.Add(leafResult.ps);
            allRenderers.Add(leafResult.renderer);
        }

        // Lens post-effect overlay
        if (materials.ContainsKey("lens_postfx"))
        {
            Renderer lensRend = CreateLensPostFX(vfx.transform, materials["lens_postfx"]);
            allRenderers.Add(lensRend);
        }

        // --- matanim (empty, for compatibility with CDPR's material animation system) ---
        GameObject matanim = new GameObject("matanim");
        matanim.layer = 8;
        matanim.transform.SetParent(pivot.transform, false);
        matanim.transform.localPosition = Vector3.zero;

        // =================================================================
        // GAME COMPONENTS (critical for the game to recognize this premium)
        // =================================================================

        // CardAppearance: registers all renderers, animators, and particles
        // with the game's card display system. Without this, the game shows
        // the flat card art instead of the premium 3D scene.
        var cardAppearance = root.AddComponent<CardAppearance>();

        // CameraValuesChanger: tells the game how to position the camera
        // to render this premium card. Values based on Dryad Ranger reference.
        var cameraChanger = root.AddComponent<CameraValuesChanger>();
        // Matching Dryad Ranger camera values exactly
        cameraChanger.fov = 25f;
        cameraChanger.camDistance = -29.871f;
        cameraChanger.nearClippingPlane = 20f;
        cameraChanger.farClippingPlane = 110f;

        // PremiumCardsMeshMaterialHandler: handles runtime texture assignment.
        // Single assignment entry with all 4 layer renderers and the atlas material.
        var matHandler = root.AddComponent<PremiumCardsMeshMaterialHandler>();

        // Populate CardAppearance via SerializedObject (fields are private)
        SetupCardAppearance(cardAppearance, cameraChanger, allRenderers, allAnimators, allParticles);

        // Populate PremiumCardsMeshMaterialHandler — single assignment, all layer renderers
        SetupMaterialHandler(matHandler, cardAppearance, layerRenderers, atlasMat);

        // Save scene
        EditorSceneManager.SaveScene(scene, SCENE_PATH);
        Debug.Log($"[ElvenDeadeye] Scene saved to {SCENE_PATH}");
    }

    static void SetupCardAppearance(
        CardAppearance ca,
        CameraValuesChanger cameraChanger,
        List<Renderer> renderers,
        List<Animator> animators,
        List<ParticleSystem> particles)
    {
        var so = new SerializedObject(ca);

        // m_ActiveComponents: AAppearanceComponent[] - includes CameraValuesChanger
        var activeField = so.FindProperty("m_ActiveComponents");
        activeField.arraySize = 1;
        activeField.GetArrayElementAtIndex(0).objectReferenceValue = cameraChanger;

        // m_AllAnimators
        var animField = so.FindProperty("m_AllAnimators");
        animField.arraySize = animators.Count;
        for (int i = 0; i < animators.Count; i++)
            animField.GetArrayElementAtIndex(i).objectReferenceValue = animators[i];

        // m_AllParticles
        var partField = so.FindProperty("m_AllParticles");
        partField.arraySize = particles.Count;
        for (int i = 0; i < particles.Count; i++)
            partField.GetArrayElementAtIndex(i).objectReferenceValue = particles[i];

        // m_AllRenderers
        var rendField = so.FindProperty("m_AllRenderers");
        rendField.arraySize = renderers.Count;
        for (int i = 0; i < renderers.Count; i++)
            rendField.GetArrayElementAtIndex(i).objectReferenceValue = renderers[i];

        so.ApplyModifiedProperties();
        Debug.Log($"[ElvenDeadeye] CardAppearance: {renderers.Count} renderers, " +
                  $"{animators.Count} animators, {particles.Count} particles");
    }

    /// <summary>
    /// Sets up PremiumCardsMeshMaterialHandler with a SINGLE assignment entry
    /// containing all 4 layer MeshRenderers and one atlas material.
    /// At runtime, AssignTexture creates one material copy with the custom texture
    /// and assigns it to all renderers. Each quad's mesh UVs handle atlas quadrant
    /// selection, so the same material works for all layers.
    /// </summary>
    static void SetupMaterialHandler(
        PremiumCardsMeshMaterialHandler handler,
        CardAppearance ca,
        List<Renderer> layerRenderers,
        Material atlasMaterial)
    {
        var so = new SerializedObject(handler);

        // Set the inherited CardAppearance reference (protected [SerializeField])
        var caField = so.FindProperty("CardAppearance");
        if (caField != null)
            caField.objectReferenceValue = ca;

        // Single PremiumTextureAssigment entry with all layer renderers
        var assignments = so.FindProperty("PremiumTextureAssigments");
        assignments.arraySize = 1;

        var elem = assignments.GetArrayElementAtIndex(0);

        // CardAppearance back-reference
        var elemCA = elem.FindPropertyRelative("m_CardAppearance");
        if (elemCA != null)
            elemCA.objectReferenceValue = ca;

        // All layer renderers in one assignment
        var rendProp = elem.FindPropertyRelative("Renderers");
        rendProp.arraySize = layerRenderers.Count;
        for (int i = 0; i < layerRenderers.Count; i++)
            rendProp.GetArrayElementAtIndex(i).objectReferenceValue = layerRenderers[i];

        // Material index 0 for each renderer (each quad has one material)
        var matIdxProp = elem.FindPropertyRelative("MaterialIndex");
        matIdxProp.arraySize = layerRenderers.Count;
        for (int i = 0; i < layerRenderers.Count; i++)
            matIdxProp.GetArrayElementAtIndex(i).intValue = 0;

        // Single shared atlas material
        var matProp = elem.FindPropertyRelative("Material");
        matProp.objectReferenceValue = atlasMaterial;

        // Texture property name to assign
        var assigProp = elem.FindPropertyRelative("Assigments");
        assigProp.arraySize = 1;
        assigProp.GetArrayElementAtIndex(0).stringValue = "_MainTex";

        so.ApplyModifiedProperties();
        Debug.Log($"[ElvenDeadeye] PremiumCardsMeshMaterialHandler: 1 assignment, {layerRenderers.Count} renderers");
    }

    struct LeafParticleResult
    {
        public ParticleSystem ps;
        public ParticleSystemRenderer renderer;
    }

    static LeafParticleResult CreateLeafParticles(Transform parent, Material material)
    {
        GameObject obj = new GameObject("leaf_particles");
        obj.layer = 8;
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = new Vector3(0f, 0f, -1f);

        ParticleSystem ps = obj.AddComponent<ParticleSystem>();

        // Main module: gentle floating leaves
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.08f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.4f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, 2f * Mathf.PI);
        main.maxParticles = 12;
        main.loop = true;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.gravityModifier = 0.01f;

        // Emission: sparse, natural
        var emission = ps.emission;
        emission.rateOverTime = 1.5f;

        // Shape: box spanning the card area
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(8f, 14f, 4f);

        // Slow rotation over lifetime
        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);

        // Gentle drift: slight horizontal wandering + slow downward fall
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.x = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
        vel.y = new ParticleSystem.MinMaxCurve(-0.04f, -0.01f);

        // Fade out near end of life
        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient fadeGrad = new Gradient();
        fadeGrad.SetKeys(
            new GradientColorKey[] {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.white, 1f)
            },
            new GradientAlphaKey[] {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(0.8f, 0.15f),
                new GradientAlphaKey(0.8f, 0.75f),
                new GradientAlphaKey(0f, 1f)
            }
        );
        col.color = new ParticleSystem.MinMaxGradient(fadeGrad);

        // Renderer
        ParticleSystemRenderer rend = obj.GetComponent<ParticleSystemRenderer>();
        rend.sharedMaterial = material;

        // Starts inactive (intro animation enables it)
        obj.SetActive(false);

        Debug.Log("[ElvenDeadeye] Created leaf particle system");
        return new LeafParticleResult { ps = ps, renderer = rend };
    }

    static Renderer CreateLensPostFX(Transform parent, Material material)
    {
        // Matching Dryad Ranger's LensPostFX placement:
        // Position (-2.41, 3.73, -7.49), Scale ~15.5, Rotation Z=-162.8°
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "ElvenDeadeye_LensPostFX";
        quad.layer = 8;
        quad.transform.SetParent(parent, false);
        quad.transform.localPosition = new Vector3(-2.4f, 3.7f, -7.5f);
        quad.transform.localRotation = Quaternion.Euler(0f, 0f, -163f);
        quad.transform.localScale = new Vector3(15.5f, 15.5f, 15.5f);

        Object.DestroyImmediate(quad.GetComponent<Collider>());

        MeshRenderer rend = quad.GetComponent<MeshRenderer>();
        rend.sharedMaterial = material;
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows = false;

        // Starts inactive (intro animation enables it)
        quad.SetActive(false);

        Debug.Log("[ElvenDeadeye] Created LensPostFX quad");
        return rend;
    }

    // =========================================================================
    // ASSET BUNDLE TAGS
    // =========================================================================
    static void SetAssetBundleTags()
    {
        // Tag the scene for asset bundle building
        AssetImporter sceneImporter = AssetImporter.GetAtPath(SCENE_PATH);
        if (sceneImporter != null)
        {
            sceneImporter.assetBundleName = "bundledassets/cardassets/scenes/" + ART_ID;
            sceneImporter.SaveAndReimport();
            Debug.Log($"[ElvenDeadeye] Scene bundle tag set: {sceneImporter.assetBundleName}");
        }
        else
        {
            Debug.LogError($"[ElvenDeadeye] Could not find scene asset at {SCENE_PATH}");
        }

        // Ensure textures/materials/animations/meshes DON'T have bundle tags
        // (they must be embedded in the scene bundle, not separate)
        ClearBundleTagsInFolder(TEXTURES_DIR);
        ClearBundleTagsInFolder(MATERIALS_DIR);
        ClearBundleTagsInFolder(ANIMATIONS_DIR);
        ClearBundleTagsInFolder(MESHES_DIR);
    }

    static void ClearBundleTagsInFolder(string folder)
    {
        string[] guids = AssetDatabase.FindAssets("", new[] { folder });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            AssetImporter imp = AssetImporter.GetAtPath(path);
            if (imp != null && !string.IsNullOrEmpty(imp.assetBundleName))
            {
                imp.assetBundleName = "";
                imp.SaveAndReimport();
            }
        }
    }
}
