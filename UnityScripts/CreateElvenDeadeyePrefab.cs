using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.IO;

/// <summary>
/// Programmatically creates all assets for the Elven Deadeye premium card from scratch:
///   - 4 quad meshes (one per parallax layer, each with atlas-quadrant UVs)
///   - 1 GwentStandard transparent material
///   - 2 animation clips (Intro + Loop)
///   - 1 animation controller (Entry → Intro → Loop)
///   - 1 prefab with full hierarchy and all Gwent components
///
/// Called automatically by BuildPremiumBundle.WatcherBuildFromScratch().
/// Can also be run manually via Assets > Create Elven Deadeye Premium Prefab.
/// </summary>
public static class CreateElvenDeadeyePrefab
{
    // Output directory inside the Unity project
    private const string OutputDir = "Assets/PremiumCards/Custom/ElvenDeadeye";

    // Gwent component script GUIDs (from prefab YAML analysis)
    private const string GUID_PremiumCardsMeshMaterialHandler = "25093b07d5588bc42961f82eada15aee";
    private const string GUID_CardAppearanceComponent = "423746d7ed4188549bd8df49a6385e62";
    private const string GUID_CameraSettings = "779e3927c97531041bd10c039690ed52";
    private const string GUID_RotationScript = "2ffef72dce217c04e9d29ce88a30d1b9";

    // GwentStandard shader GUID
    private const string GUID_GwentStandard = "24220d20ad4c4754fb208a4668c20708";

    // Square quad so 1024×1024 atlas quadrants map with 1:1 pixel proportions.
    // Per-layer overscan scaling handles camera coverage at each Z-depth.
    private const float QuadHalf = 7f;  // 14×14 base quad

    // Layer definitions: name, Z-depth, UV region (minX, minY, maxX, maxY), overscan scale
    // Overscan scales computed from camera FOV=25°, distance=29.87, plus ~15% margin:
    //   visible_half = tan(12.5°) × (29.87 + layer_z), scale = (visible_half × 1.15) / QuadHalf
    private static readonly (string name, float z, float uvMinX, float uvMinY, float uvMaxX, float uvMaxY, float scale)[] Layers = {
        ("far_background", 10f,   0f,   0.5f, 0.5f, 1f,   1.45f),  // top-left,     vis=8.84 → need ~10.2
        ("tree_trunk",      3f,   0.5f, 0.5f, 1f,   1f,   1.20f),  // top-right,    vis=7.29 → need ~8.4
        ("elf",             0f,   0f,   0f,   0.5f, 0.5f,  1.10f),  // bottom-left,  vis=6.62 → need ~7.6
        ("log",            -5f,   0.5f, 0f,   1f,   0.5f,  1.00f),  // bottom-right, vis=5.52 → 7.0 is plenty
    };

    // Common loop clip duration — all layers use the same period for seamless looping.
    // Visual variety comes from different amplitudes, phases, and number of cycles.
    private const float LoopDuration = 8f;

    // Animation parameters per layer: (sway amplitude, cycles per loop, phase offset in radians)
    // Amplitudes clamped to stay well within each layer's overscan margin.
    // Max safe displacement ≈ (scale - 1) × QuadHalf, e.g. far_bg: (1.45-1)×7 = 3.15
    private static readonly (float amplitude, float cycles, float phase)[] LayerAnimParams = {
        (0.20f, 1f, 0f),               // far_background: overscan 3.15, uses 6%
        (0.10f, 2f, 0.5f),             // tree_trunk: overscan 1.40, uses 7%
        (0.04f, 1f, 0.25f),            // elf: overscan 0.70, uses 6%
        (0.12f, 1f, Mathf.PI),         // log: no overscan needed (closest), subtle sway
    };

    /// <summary>
    /// Creates all assets and returns the prefab path.
    /// Called by BuildPremiumBundle.WatcherBuildFromScratch().
    /// </summary>
    public static string Create(string targetArtId)
    {
        Debug.Log($"[CreateElvenDeadeye] Starting asset creation for ArtId {targetArtId}...");

        EnsureDirectory(OutputDir);
        EnsureDirectory(OutputDir + "/Meshes");

        // 1. Create quad meshes
        Debug.Log("[CreateElvenDeadeye] Creating quad meshes...");
        Mesh[] meshes = new Mesh[Layers.Length];
        for (int i = 0; i < Layers.Length; i++)
        {
            var layer = Layers[i];
            var mesh = CreateQuadMesh(layer.name, layer.uvMinX, layer.uvMinY, layer.uvMaxX, layer.uvMaxY);
            string meshPath = $"{OutputDir}/Meshes/{layer.name}.asset";
            CreateOrReplaceAsset(mesh, meshPath);
            // Reload from asset database so prefab references the persisted asset
            meshes[i] = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            Debug.Log($"[CreateElvenDeadeye]   Mesh: {meshPath}");
        }

        // 2. Create material (GwentStandard, transparent)
        Debug.Log("[CreateElvenDeadeye] Creating material...");
        var material = CreateAtlasMaterial();
        string matPath = $"{OutputDir}/ElvenDeadeye-Atlas.mat";
        CreateOrReplaceAsset(material, matPath);
        // Reload material from asset database so prefab references the asset, not the in-memory copy
        material = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        Debug.Log($"[CreateElvenDeadeye]   Material: {matPath}");

        // 3. Create animation clips
        Debug.Log("[CreateElvenDeadeye] Creating animation clips...");
        var introClip = CreateIntroClip();
        string introPath = $"{OutputDir}/ElvenDeadeye_Intro.anim";
        CreateOrReplaceAsset(introClip, introPath);
        introClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(introPath);

        var loopClip = CreateLoopClip();
        string loopPath = $"{OutputDir}/ElvenDeadeye_Loop.anim";
        CreateOrReplaceAsset(loopClip, loopPath);
        loopClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(loopPath);
        Debug.Log($"[CreateElvenDeadeye]   Clips: {introPath}, {loopPath}");

        // 4. Create animation controller
        Debug.Log("[CreateElvenDeadeye] Creating animation controller...");
        string controllerPath = $"{OutputDir}/ElvenDeadeye_AC.controller";
        var controller = CreateAnimatorController(controllerPath, introClip, loopClip);
        Debug.Log($"[CreateElvenDeadeye]   Controller: {controllerPath}");

        // 5. Assemble prefab
        Debug.Log("[CreateElvenDeadeye] Assembling prefab...");
        string prefabPath = $"{OutputDir}/ElvenDeadeye.prefab";
        AssemblePrefab(prefabPath, meshes, material, controller);
        Debug.Log($"[CreateElvenDeadeye]   Prefab: {prefabPath}");

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[CreateElvenDeadeye] All assets created successfully! Prefab at: {prefabPath}");
        return prefabPath;
    }

    [MenuItem("Assets/Create Elven Deadeye Premium Prefab")]
    public static void CreateFromMenu()
    {
        string prefabPath = Create("1832");
        EditorUtility.DisplayDialog("Success",
            $"Elven Deadeye premium prefab created!\n\n{prefabPath}", "OK");
    }

    // =========================================================================
    // Mesh Creation
    // =========================================================================

    private static Mesh CreateQuadMesh(string name, float uvMinX, float uvMinY, float uvMaxX, float uvMaxY)
    {
        var mesh = new Mesh();
        mesh.name = name;

        mesh.vertices = new Vector3[]
        {
            new Vector3(-QuadHalf, -QuadHalf, 0),  // bottom-left
            new Vector3( QuadHalf, -QuadHalf, 0),  // bottom-right
            new Vector3(-QuadHalf,  QuadHalf, 0),  // top-left
            new Vector3( QuadHalf,  QuadHalf, 0),  // top-right
        };

        mesh.uv = new Vector2[]
        {
            new Vector2(uvMinX, uvMinY),  // bottom-left
            new Vector2(uvMaxX, uvMinY),  // bottom-right
            new Vector2(uvMinX, uvMaxY),  // top-left
            new Vector2(uvMaxX, uvMaxY),  // top-right
        };

        mesh.triangles = new int[] { 0, 2, 1, 2, 3, 1 };

        mesh.normals = new Vector3[]
        {
            Vector3.back, Vector3.back, Vector3.back, Vector3.back
        };

        mesh.RecalculateBounds();
        return mesh;
    }

    // =========================================================================
    // Material Creation
    // =========================================================================

    private static Material CreateAtlasMaterial()
    {
        // Load GwentStandard shader by GUID
        string shaderPath = AssetDatabase.GUIDToAssetPath(GUID_GwentStandard);
        Shader shader = null;

        if (!string.IsNullOrEmpty(shaderPath))
            shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);

        // Fallback: try finding by name
        if (shader == null)
            shader = Shader.Find("GwentStandard");
        if (shader == null)
            shader = Shader.Find("ShaderLibrary/Generic/GwentStandard");
        if (shader == null)
            shader = Shader.Find("Standard"); // Last resort

        var mat = new Material(shader);
        mat.name = "ElvenDeadeye-Atlas";

        // Transparent mode (matching Dryad Ranger atlas material)
        mat.SetFloat("_Mode", 3f);
        mat.SetFloat("_SrcBlend", 1f);       // One
        mat.SetFloat("_DstBlend", 10f);      // OneMinusSrcAlpha
        mat.SetFloat("_ZWrite", 0f);         // Off
        mat.SetFloat("_Glossiness", 0f);
        mat.SetFloat("_Metallic", 0f);
        mat.EnableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = -1;                 // Default

        return mat;
    }

    // =========================================================================
    // Animation Clip Creation
    // =========================================================================

    private static AnimationClip CreateIntroClip()
    {
        var clip = new AnimationClip();
        clip.name = "ElvenDeadeye_Intro";
        clip.frameRate = 60;

        // Each layer slides from an offset position to its final position over ~1 second.
        // Offsets clamped to 70% of each layer's overscan margin to prevent edge showing.
        float duration = 1f;
        float[] offsets = { 1.5f, 0.7f, 0.3f, -0.8f }; // x-offset at start, within overscan

        for (int i = 0; i < Layers.Length; i++)
        {
            string path = Layers[i].name;

            // localPosition.x: slide from offset to 0
            var curveX = new AnimationCurve();
            curveX.AddKey(new Keyframe(0f, offsets[i], 0f, 0f));
            curveX.AddKey(new Keyframe(duration, 0f, 0f, 0f));
            clip.SetCurve(path, typeof(Transform), "localPosition.x", curveX);

            // localPosition.y: constant 0
            var curveY = AnimationCurve.Constant(0f, duration, 0f);
            clip.SetCurve(path, typeof(Transform), "localPosition.y", curveY);

            // localPosition.z: constant at layer depth
            var curveZ = AnimationCurve.Constant(0f, duration, Layers[i].z);
            clip.SetCurve(path, typeof(Transform), "localPosition.z", curveZ);
        }

        return clip;
    }

    private static AnimationClip CreateLoopClip()
    {
        var clip = new AnimationClip();
        clip.name = "ElvenDeadeye_Loop";
        clip.frameRate = 60;
        clip.wrapMode = WrapMode.Loop;

        // Set loop time via AnimationClipSettings
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        for (int i = 0; i < Layers.Length; i++)
        {
            string path = Layers[i].name;
            var anim = LayerAnimParams[i];

            // localPosition.x: sinusoidal sway
            // All layers use the same clip duration for seamless looping.
            // Different cycles/phase create visual variety.
            float A = anim.amplitude;
            float C = anim.cycles;  // number of full sine cycles in LoopDuration
            float P = anim.phase;

            var curveX = new AnimationCurve();
            int steps = Mathf.Max(8, (int)(C * 8)); // more keyframes for more cycles
            for (int s = 0; s <= steps; s++)
            {
                float t = (float)s / steps * LoopDuration;
                float value = A * Mathf.Sin(2f * Mathf.PI * C * t / LoopDuration + P);
                var keyframe = new Keyframe(t, value);
                curveX.AddKey(keyframe);
            }

            // Smooth all tangent modes to auto for natural curves
            for (int k = 0; k < curveX.keys.Length; k++)
                AnimationUtility.SetKeyLeftTangentMode(curveX, k, AnimationUtility.TangentMode.Auto);

            clip.SetCurve(path, typeof(Transform), "localPosition.x", curveX);

            // localPosition.y: constant 0
            var curveY = AnimationCurve.Constant(0f, LoopDuration, 0f);
            clip.SetCurve(path, typeof(Transform), "localPosition.y", curveY);

            // localPosition.z: constant at layer depth
            var curveZ = AnimationCurve.Constant(0f, LoopDuration, Layers[i].z);
            clip.SetCurve(path, typeof(Transform), "localPosition.z", curveZ);
        }

        return clip;
    }

    // =========================================================================
    // Animation Controller Creation
    // =========================================================================

    private static AnimatorController CreateAnimatorController(string path, AnimationClip introClip, AnimationClip loopClip)
    {
        // Delete existing controller if present (AnimatorController.CreateAnimatorControllerAtPath
        // doesn't overwrite cleanly)
        if (File.Exists(Path.Combine(Application.dataPath, "..", path)))
            AssetDatabase.DeleteAsset(path);

        var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
        controller.name = "ElvenDeadeye_AC";

        // Get the root state machine
        var rootSM = controller.layers[0].stateMachine;

        // Add Intro state (default)
        var introState = rootSM.AddState("Intro", new Vector3(264, 72, 0));
        introState.motion = introClip;
        introState.writeDefaultValues = true;

        // Add Loop state
        var loopState = rootSM.AddState("Loop", new Vector3(539, 163, 0));
        loopState.motion = loopClip;
        loopState.writeDefaultValues = true;

        // Transition from Intro to Loop (auto after Intro finishes)
        var transition = introState.AddTransition(loopState);
        transition.hasExitTime = true;
        transition.exitTime = 0.75f;
        transition.duration = 0.25f;
        transition.hasFixedDuration = true;

        // Set Intro as default state
        rootSM.defaultState = introState;

        AssetDatabase.SaveAssets();
        return controller;
    }

    // =========================================================================
    // Prefab Assembly
    // =========================================================================

    private static void AssemblePrefab(string prefabPath, Mesh[] meshes, Material material, RuntimeAnimatorController controller)
    {
        // ---- Root GameObject ----
        var root = new GameObject("ElvenDeadeye");
        root.layer = 8; // Same layer as all CDPR premium cards

        // Add Gwent components to root
        AddGwentComponent(root, GUID_CameraSettings, so =>
        {
            SetProperty(so, "fov", 25f);
            SetProperty(so, "camDistance", -29.87103f);
            SetProperty(so, "nearClippingPlane", 20f);
            SetProperty(so, "farClippingPlane", 110f);
        });

        var cardAppearanceComp = AddGwentComponent(root, GUID_CardAppearanceComponent, null);

        // ---- Pivot ----
        var pivot = new GameObject("Pivot");
        pivot.layer = 8;
        pivot.transform.SetParent(root.transform);
        pivot.transform.localPosition = new Vector3(0, -2, 0);

        // Animator on Pivot (no avatar needed for transform-only animation)
        var animator = pivot.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.applyRootMotion = false;

        // Rotation script on Pivot
        AddGwentComponent(pivot, GUID_RotationScript, so =>
        {
            SetProperty(so, "XRotationStart", -6f);
            SetProperty(so, "YRotationStart", -2f);
            SetProperty(so, "XRotationEnd", 6f);
            SetProperty(so, "YRotationEnd", 2f);
        });

        // ---- Layer quads ----
        var renderers = new Renderer[Layers.Length];
        for (int i = 0; i < Layers.Length; i++)
        {
            var layer = Layers[i];
            var layerGO = new GameObject(layer.name);
            layerGO.layer = 8;
            layerGO.transform.SetParent(pivot.transform);
            layerGO.transform.localPosition = new Vector3(0, 0, layer.z);
            layerGO.transform.localScale = new Vector3(layer.scale, layer.scale, 1f);

            var meshFilter = layerGO.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = meshes[i];

            var meshRenderer = layerGO.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            renderers[i] = meshRenderer;
        }

        // ---- Configure PremiumCardsMeshMaterialHandler ----
        // This component maps materials to texture slots so Hook 5 can assign the atlas texture
        AddGwentComponent(root, GUID_PremiumCardsMeshMaterialHandler, so =>
        {
            // Set up PremiumTextureAssigments array — one entry per renderer
            var assignments = so.FindProperty("PremiumTextureAssigments");
            if (assignments != null)
            {
                assignments.arraySize = renderers.Length;
                for (int i = 0; i < renderers.Length; i++)
                {
                    var element = assignments.GetArrayElementAtIndex(i);

                    // Set Material reference
                    var matProp = element.FindPropertyRelative("Material");
                    if (matProp != null)
                        matProp.objectReferenceValue = material;

                    // Set Assigments array (texture property names)
                    var assignmentsProp = element.FindPropertyRelative("Assigments");
                    if (assignmentsProp != null)
                    {
                        assignmentsProp.arraySize = 1;
                        assignmentsProp.GetArrayElementAtIndex(0).stringValue = "_MainTex";
                    }

                    // Set Renderers array if the field exists (newer prefab format)
                    var renderersProp = element.FindPropertyRelative("Renderers");
                    if (renderersProp != null)
                    {
                        renderersProp.arraySize = 1;
                        renderersProp.GetArrayElementAtIndex(0).objectReferenceValue = renderers[i];
                    }
                }
            }
            else
            {
                Debug.LogWarning("[CreateElvenDeadeye] PremiumTextureAssigments property not found!");
            }
        });

        // ---- Configure CardAppearanceComponent ----
        if (cardAppearanceComp != null)
        {
            var so = new SerializedObject(cardAppearanceComp);

            // Register all renderers
            var allRenderers = so.FindProperty("m_AllRenderers");
            if (allRenderers != null)
            {
                allRenderers.arraySize = renderers.Length;
                for (int i = 0; i < renderers.Length; i++)
                    allRenderers.GetArrayElementAtIndex(i).objectReferenceValue = renderers[i];
            }

            // Register animator
            var allAnimators = so.FindProperty("m_AllAnimators");
            if (allAnimators != null)
            {
                allAnimators.arraySize = 1;
                allAnimators.GetArrayElementAtIndex(0).objectReferenceValue = animator;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---- Save as prefab ----
        // Delete existing prefab if present
        if (File.Exists(Path.Combine(Application.dataPath, "..", prefabPath)))
            AssetDatabase.DeleteAsset(prefabPath);

        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    /// <summary>
    /// Adds a Gwent MonoBehaviour component by its script GUID, then configures it
    /// via SerializedObject. Returns the added component (or null if script not found).
    /// </summary>
    private static Component AddGwentComponent(GameObject go, string guid, System.Action<SerializedObject> configure)
    {
        string scriptPath = AssetDatabase.GUIDToAssetPath(guid);
        if (string.IsNullOrEmpty(scriptPath))
        {
            Debug.LogWarning($"[CreateElvenDeadeye] Script GUID not found: {guid}");
            return null;
        }

        var script = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
        if (script == null)
        {
            Debug.LogWarning($"[CreateElvenDeadeye] MonoScript not found at: {scriptPath}");
            return null;
        }

        var scriptType = script.GetClass();
        if (scriptType == null)
        {
            Debug.LogWarning($"[CreateElvenDeadeye] Script class not found for: {scriptPath}");
            return null;
        }

        var component = go.AddComponent(scriptType);
        if (component == null)
        {
            Debug.LogWarning($"[CreateElvenDeadeye] Failed to add component: {scriptType.Name}");
            return null;
        }

        if (configure != null)
        {
            var so = new SerializedObject(component);
            configure(so);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        Debug.Log($"[CreateElvenDeadeye] Added component: {scriptType.Name} to {go.name}");
        return component;
    }

    private static void SetProperty(SerializedObject so, string name, float value)
    {
        var prop = so.FindProperty(name);
        if (prop != null)
            prop.floatValue = value;
    }

    private static void EnsureDirectory(string assetPath)
    {
        string fullPath = Path.Combine(Application.dataPath, "..", assetPath);
        if (!Directory.Exists(fullPath))
        {
            Directory.CreateDirectory(fullPath);
            AssetDatabase.Refresh();
        }
    }

    private static void CreateOrReplaceAsset(Object asset, string path)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Object>(path);
        if (existing != null)
            AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(asset, path);
    }
}
