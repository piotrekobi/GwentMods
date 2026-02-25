using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using MelonLoader;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using Il2CppGwentGameplay;
using Il2CppGwentUnity;
using Il2CppGwentVisuals;

[assembly: MelonInfo(typeof(CustomPremiums.CustomPremiumsCore), "CustomPremiums", "5.0.0", "piotrekobi")]
[assembly: MelonGame("CDProjektRED", "Gwent")]

namespace CustomPremiums
{
    /// <summary>
    /// CustomPremiums v5 - Fully self-contained custom premium loading.
    /// NO donor card dependency. Loads entirely custom scene bundles.
    ///
    ///   HOOK 1: Card.SetDefinition            -> force IsPremium = true
    ///   HOOK 2: CardDefinition.IsPremiumDisabled -> force return false
    ///   HOOK 3: CardViewAssetComponent.ShouldLoadPremium -> force true for our cards
    ///   HOOK 4: CardAppearanceRequest.HandleTextureRequestsFinished
    ///           -> take full control: load our custom bundle + scene, skip normal pipeline
    /// </summary>
    public class CustomPremiumsCore : MelonMod
    {
        internal static MelonLogger.Instance Logger;

        // We now dynamically find TemplateIds based on the ArtIds of the custom bundles/textures found on disk
        public static readonly HashSet<int> TargetTemplateIds = new();
        public static readonly Dictionary<int, int> TemplateIdToArtId = new(); // Maps TemplateId to ArtId dynamically

        // ArtId -> absolute path to custom bundle
        public static readonly Dictionary<int, string> CustomBundles = new();
        // ArtId -> absolute path to custom texture
        public static readonly Dictionary<int, string> CustomTextures = new();

        // ArtId of the donor card whose premium audio we borrow (Elven Wardancer)
        public const int DonorArtId = 1222;

        // Caches
        public static readonly Dictionary<int, AssetBundle> LoadedBundles = new();
        public static readonly Dictionary<int, Texture2D> LoadedTextures = new();

        // Game directory (resolved at init)
        private static string GameDir;

        public override void OnInitializeMelon()
        {
            Logger = LoggerInstance;
            Logger.Msg("========================================");
            Logger.Msg("  CustomPremiums v5.0.0 - Custom Bundles");
            Logger.Msg("========================================");

            // Resolve absolute game directory
            GameDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            // Assembly is in Mods/, go up one level to game root
            GameDir = Path.GetDirectoryName(GameDir);
            Logger.Msg($"[Init] Game directory: {GameDir}");

            ScanFiles();

            try
            {
                HarmonyInstance.PatchAll(typeof(CustomPremiumsCore).Assembly);
                Logger.Msg("All Harmony patches applied!");
            }
            catch (Exception e)
            {
                Logger.Error($"Harmony PatchAll failed: {e}");
            }
        }

        private void ScanFiles()
        {
            string bundlesPath = Path.Combine(GameDir, "Mods", "CustomPremiums", "Bundles");
            string texturesPath = Path.Combine(GameDir, "Mods", "CustomPremiums", "Textures");

            Logger.Msg($"[Scan] Bundles dir: {bundlesPath} (exists: {Directory.Exists(bundlesPath)})");
            Logger.Msg($"[Scan] Textures dir: {texturesPath} (exists: {Directory.Exists(texturesPath)})");

            if (!Directory.Exists(bundlesPath)) Directory.CreateDirectory(bundlesPath);
            if (!Directory.Exists(texturesPath)) Directory.CreateDirectory(texturesPath);

            foreach (var file in Directory.GetFiles(bundlesPath))
            {
                // Skip files with extensions (.bak, .manifest, etc) - bundles are extensionless
                if (Path.GetExtension(file) != "")
                {
                    Logger.Msg($"[Scan] Skipping non-bundle file: '{Path.GetFileName(file)}'");
                    continue;
                }
                string name = Path.GetFileName(file);
                Logger.Msg($"[Scan] Found bundle file: '{name}' -> '{file}'");
                if (int.TryParse(name, out int artId))
                {
                    CustomBundles[artId] = file;
                    Logger.Msg($"[Scan] Registered custom bundle: ArtId {artId}");
                }
            }

            foreach (var file in Directory.GetFiles(texturesPath, "*.png"))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                if (int.TryParse(name, out int artId))
                {
                    CustomTextures[artId] = file;
                    Logger.Msg($"[Scan] Registered custom texture: ArtId {artId}");
                }
            }

            Logger.Msg($"[Scan] Total: {CustomBundles.Count} bundles, {CustomTextures.Count} textures");
        }

        // =====================================================================
        // HOOK 0: Map ArtIds to TemplateIds at Initialization
        // =====================================================================
        [HarmonyPatch(typeof(GwentApp), nameof(GwentApp.HandleDefinitionsLoaded))]
        public static class Hook0_DefinitionsLoaded
        {
            public static void Postfix()
            {
                var sharedData = GwentApp.Instance?.SharedData;
                if (sharedData?.SharedRuntimeTemplates == null) return;

                // First pass: find the donor card's AudioId
                int donorAudioId = 0;
                foreach (var kvp in sharedData.SharedRuntimeTemplates)
                {
                    var t = kvp.Value;
                    if (t?.ArtDefinition != null && t.ArtDefinition.ArtId == DonorArtId)
                    {
                        donorAudioId = t.Template.AudioId;
                        Logger.Msg($"[Init] Donor AudioId: {donorAudioId} (from ArtId {DonorArtId})");
                        break;
                    }
                }

                // Second pass: register custom cards and set their AudioId to the donor's
                foreach (var kvp in sharedData.SharedRuntimeTemplates)
                {
                    var template = kvp.Value;
                    if (template != null && template.Template != null && template.ArtDefinition != null)
                    {
                        if (CustomBundles.ContainsKey(template.ArtDefinition.ArtId) || CustomTextures.ContainsKey(template.ArtDefinition.ArtId))
                        {
                            TargetTemplateIds.Add(template.Template.Id);
                            TemplateIdToArtId[template.Template.Id] = template.ArtDefinition.ArtId;
                            Logger.Msg($"[Init] Mapped ArtId {template.ArtDefinition.ArtId} -> TemplateId {template.Template.Id}");

                            if (donorAudioId > 0)
                            {
                                int oldAudioId = template.Template.AudioId;
                                template.Template.AudioId = donorAudioId;
                                Logger.Msg($"[Init] AudioId for ArtId {template.ArtDefinition.ArtId}: {oldAudioId} -> {donorAudioId} (donor)");
                            }
                        }
                    }
                }
            }
        }

        // =====================================================================
        // HOOK 1: Force IsPremium = true
        // =====================================================================
        [HarmonyPatch(typeof(Il2CppGwentGameplay.Card), "SetDefinition")]
        public static class Hook1_SetDefinition
        {
            static void Prefix(ref CardDefinition newDefinition)
            {
                if (newDefinition.TemplateId != 0 && TargetTemplateIds.Contains(newDefinition.TemplateId))
                {
                    if (!newDefinition.IsPremium)
                        Logger.Msg($"[HOOK 1] Forcing IsPremium for TemplateId {newDefinition.TemplateId}");
                    newDefinition.IsPremium = true;
                }
            }
        }

        // =====================================================================
        // HOOK 2: Bypass IsPremiumDisabled (proven struct patch)
        // =====================================================================
        [HarmonyPatch(typeof(Il2CppGwentGameplay.CardDefinition), "IsPremiumDisabled")]
        public static class Hook2_IsPremiumDisabled
        {
            static bool Prefix(ref CardDefinition __instance, ref bool __result)
            {
                if (TargetTemplateIds.Contains(__instance.TemplateId))
                {
                    __result = false;
                    return false;
                }
                return true;
            }
        }

        // =====================================================================
        // HOOK 3: Force ShouldLoadPremium to return true (belt and suspenders)
        // This is a class method so Harmony can safely patch it.
        // =====================================================================
        [HarmonyPatch(typeof(CardViewAssetComponent), "ShouldLoadPremium")]
        public static class Hook3_ShouldLoadPremium
        {
            static void Postfix(CardViewAssetComponent __instance, ref bool __result)
            {
                if (__result) return; // Already true, no need to override

                try
                {
                    var cardDef = __instance.CardDefinition;
                    if (TargetTemplateIds.Contains(cardDef.TemplateId))
                    {
                        Logger.Msg($"[HOOK 3] Forcing ShouldLoadPremium=true for TemplateId {cardDef.TemplateId}");
                        __result = true;
                    }
                }
                catch { }
            }
        }

        // =====================================================================
        // HOOK 4: Take full control at HandleTextureRequestsFinished
        // This is the CORE hook - we completely bypass the normal asset bundle
        // loading and load our own custom scene bundle.
        // =====================================================================
        [HarmonyPatch(typeof(CardAppearanceRequest), "HandleTextureRequestsFinished")]
        public static class Hook4_HandleTextureRequestsFinished
        {
            static bool Prefix(CardAppearanceRequest __instance)
            {
                Logger.Msg($"[HOOK 4] HandleTextureRequestsFinished called. IsCancelled={__instance.m_IsCancelled}");

                if (__instance.m_IsCancelled)
                    return true; // Let original handle cancellation

                // Check if this is one of our custom cards
                var cardDef = __instance.m_CardDefinition;
                if (!TargetTemplateIds.Contains(cardDef.TemplateId))
                    return true; // Not our card, let game handle normally

                int artId = TemplateIdToArtId[cardDef.TemplateId];
                Logger.Msg($"[HOOK 4] Custom card detected! TemplateId={cardDef.TemplateId}, ArtId={artId}");

                if (!CustomBundles.TryGetValue(artId, out string bundlePath))
                {
                    Logger.Warning($"[HOOK 4] No custom bundle found for ArtId {artId}!");
                    return true; // Fall back to normal pipeline
                }

                Logger.Msg($"[HOOK 4] Loading custom bundle: {bundlePath}");

                try
                {
                    // Unbind the event (same as original method does)
                    __instance.m_EventBinder.UnBind();

                    // Load our custom bundle
                    if (!LoadedBundles.ContainsKey(artId) || LoadedBundles[artId] == null)
                    {
                        Logger.Msg($"[HOOK 4] File exists: {File.Exists(bundlePath)}, size: {(File.Exists(bundlePath) ? new FileInfo(bundlePath).Length : 0)} bytes");
                        
                        // Read bytes and use LoadFromMemory (different Unity code path than LoadFromFile)
                        var loadedBundle = AssetBundle.LoadFromFile(bundlePath);
                        Logger.Msg($"[HOOK 4] LoadFromFile returned: {(loadedBundle != null ? loadedBundle.name : "NULL")}");
                        LoadedBundles[artId] = loadedBundle;
                    }

                    var bundle = LoadedBundles[artId];
                    if (bundle == null)
                    {
                        Logger.Error($"[HOOK 4] Bundle load FAILED!");
                        __instance.Finish();
                        return false;
                    }

                    var scenes = bundle.GetAllScenePaths();
                    if (scenes == null || scenes.Count == 0)
                    {
                        Logger.Error($"[HOOK 4] No scenes in bundle!");
                        __instance.Finish();
                        return false;
                    }

                    string scenePath = scenes[0];
                    Logger.Msg($"[HOOK 4] Loading scene: {scenePath}");

                    __instance.m_AssetPath = scenePath;
                    __instance.m_SceneLoadOperation = SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Additive);

                    if (__instance.m_SceneLoadOperation != null)
                    {
                        Logger.Msg($"[HOOK 4] Scene load started, waiting via coroutine...");
                        MelonCoroutines.Start(WaitForSceneLoad(__instance.m_SceneLoadOperation, __instance, artId));
                    }
                    else
                    {
                        Logger.Error($"[HOOK 4] SceneLoadOperation is null!");
                        __instance.Finish();
                    }
                }
                catch (Exception e)
                {
                    Logger.Error($"[HOOK 4] Exception: {e}");
                    __instance.Finish();
                }

                return false; // Skip the original HandleTextureRequestsFinished entirely
            }

            private static IEnumerator WaitForSceneLoad(AsyncOperation op, CardAppearanceRequest request, int artId)
            {
                while (!op.isDone)
                    yield return null;

                Logger.Msg($"[HOOK 4] Scene load complete! Processing scene ourselves...");

                try
                {
                    // Find the loaded scene
                    string scenePath = request.m_AssetPath;
                    Scene scene = SceneManager.GetSceneByPath(scenePath);
                    
                    Logger.Msg($"[HOOK 4] GetSceneByPath('{scenePath}'): valid={scene.IsValid()}, name='{scene.name}', rootCount={scene.rootCount}");

                    if (!scene.IsValid() || scene.rootCount == 0)
                    {
                        Logger.Msg($"[HOOK 4] Path lookup failed, searching {SceneManager.sceneCount} loaded scenes...");
                        for (int i = 0; i < SceneManager.sceneCount; i++)
                        {
                            var s = SceneManager.GetSceneAt(i);
                            if (s.isLoaded && s.rootCount > 0 && s.path.Contains("CardAssets/Scenes"))
                            {
                                scene = s;
                                Logger.Msg($"[HOOK 4] Found scene: '{s.name}' at index {i}");
                                break;
                            }
                        }
                    }

                    if (scene.IsValid() && scene.rootCount > 0)
                    {
                        var rootObjects = scene.GetRootGameObjects();
                        var appearanceRoot = rootObjects[0];
                        Logger.Msg($"[HOOK 4] Root object: '{appearanceRoot.name}'");

                        CardAssetManager.Instance.MoveObjectToGlobalScene(appearanceRoot);
                        request.OnAppearanceObjectLoaded(appearanceRoot);
                    }
                    else
                    {
                        Logger.Error($"[HOOK 4] No valid scene found!");
                        request.Finish();
                    }

                    // Unload the scene and call Finish() when done
                    // (original game flow: UnloadScene -> OnSceneUnloaded -> Finish())
                    if (scene.IsValid())
                    {
                        var unloadOp = SceneManager.UnloadSceneAsync(scene);
                        if (unloadOp != null)
                        {
                            MelonCoroutines.Start(WaitForUnloadThenFinish(unloadOp, request));
                        }
                        else
                        {
                            request.Finish();
                        }
                    }
                    else
                    {
                        request.Finish();
                    }

                    // CRITICAL: Unload the AssetBundle to free the internal name slot!
                    // Without this, the bundle name (12220101) stays registered and blocks
                    // both future custom loads AND the game's normal Wardancer premium.
                    if (LoadedBundles.TryGetValue(artId, out var bundle) && bundle != null)
                    {
                        bundle.Unload(false); // false = don't destroy loaded objects
                        LoadedBundles.Remove(artId);
                        Logger.Msg($"[HOOK 4] AssetBundle unloaded (freed name slot)");
                    }
                }
                catch (Exception e)
                {
                    Logger.Error($"[HOOK 4] Scene processing failed: {e}");
                    request.Finish();
                }
            }

            private static IEnumerator WaitForUnloadThenFinish(AsyncOperation unloadOp, CardAppearanceRequest request)
            {
                while (!unloadOp.isDone)
                    yield return null;
                request.Finish();
                Logger.Msg("[HOOK 4] Request Finish() called after scene unload");
            }
        }

        // =====================================================================
        // HOOK 5: After scene loads, swap texture with our custom art
        // =====================================================================
        [HarmonyPatch(typeof(CardAppearanceRequest), "OnAppearanceObjectLoaded")]
        public static class Hook5_SwapTexture
        {
            static void Prefix(CardAppearanceRequest __instance, GameObject appearanceObject)
            {
                Logger.Msg($"[HOOK 5] OnAppearanceObjectLoaded called! TemplateId={__instance.m_CardDefinition.TemplateId}, appearanceObject={(appearanceObject != null ? appearanceObject.name : "NULL")}");

                var cardDef = __instance.m_CardDefinition;
                if (!TemplateIdToArtId.TryGetValue(cardDef.TemplateId, out int artId)) return;
                if (!CustomTextures.TryGetValue(artId, out string texPath)) return;

                Logger.Msg($"[HOOK 5] Swapping texture for ArtId={artId}");

                try
                {
                    // Load custom texture
                    if (!LoadedTextures.ContainsKey(artId))
                    {
                        byte[] fileData = File.ReadAllBytes(texPath);
                        Texture2D tex = new Texture2D(2, 2);
                        ImageConversion.LoadImage(tex, fileData);
                        LoadedTextures[artId] = tex;
                        Logger.Msg($"[HOOK 5] Loaded texture from disk ({fileData.Length} bytes)");
                    }

                    // DEBUG: Dump shader status for all renderers
                    var allRenderers = appearanceObject.GetComponentsInChildren<Renderer>(true);
                    Logger.Msg($"[HOOK 5] Shader diagnostic: {allRenderers.Count} renderers found");
                    foreach (var r in allRenderers)
                    {
                        if (r == null) continue;
                        var mats = r.sharedMaterials;
                        foreach (var m in mats)
                        {
                            if (m == null)
                            {
                                Logger.Msg($"[HOOK 5]   {r.gameObject.name}: NULL material");
                                continue;
                            }
                            var shader = m.shader;
                            string shaderName = shader != null ? shader.name : "NULL_SHADER";
                            Logger.Msg($"[HOOK 5]   {r.gameObject.name}: mat='{m.name}' shader='{shaderName}'");
                        }
                    }

                    // Fix VFX materials that couldn't resolve shaders from the 'shaders' dependency bundle.
                    // The main mesh uses GwentStandard from 'shaderlibrary' (resolved via bundle dependencies).
                    // VFX shaders are in a separate 'shaders' bundle already loaded by the game.
                    foreach (var r in allRenderers)
                    {
                        if (r == null) continue;
                        var mats = r.sharedMaterials;
                        for (int i = 0; i < mats.Count; i++)
                        {
                            var m = mats[i];
                            if (m == null) continue;
                            if (m.shader != null && m.shader.name == "Hidden/InternalErrorShader")
                            {
                                // Map material names to their correct shader paths
                                string correctShader = null;
                                string matName = m.name;
                                if (matName.Contains("flash") || matName.Contains("glow") || matName.Contains("flare"))
                                    correctShader = "VFX/Common/AdditiveAlpha";
                                else if (matName.Contains("introAll"))
                                    correctShader = "VFX/Common/AlphaBlended";
                                else if (matName.Contains("LensPostFX"))
                                    correctShader = "VFX/Effects/FakePostEffect/Additive_Mask";

                                if (correctShader != null)
                                {
                                    var found = Shader.Find(correctShader);
                                    if (found != null)
                                    {
                                        m.shader = found;
                                        Logger.Msg($"[HOOK 5] Fixed shader: {matName} -> {correctShader}");
                                    }
                                    else
                                    {
                                        Logger.Warning($"[HOOK 5] Shader.Find failed for: {correctShader}");
                                    }
                                }
                                else
                                {
                                    Logger.Warning($"[HOOK 5] Unknown broken material: {matName}");
                                }
                            }
                        }
                    }

                    var customTex = LoadedTextures[artId];
                    var matHandler = appearanceObject.GetComponentInChildren<PremiumCardsMeshMaterialHandler>(true);
                    
                    if (matHandler?.PremiumTextureAssigments != null)
                    {
                        foreach (var assignment in matHandler.PremiumTextureAssigments)
                        {
                            if (assignment?.Material != null)
                            {
                                assignment.AssignTexture(customTex);
                                Logger.Msg("[HOOK 5] Custom texture applied via Handlers!");
                            }
                        }
                    }
                    else
                    {
                        Logger.Warning("[HOOK 5] PremiumCardsMeshMaterialHandler is missing from the prefab!");
                    }
                }
                catch (Exception e)
                {
                    Logger.Error($"[HOOK 5] Exception: {e}");
                }
            }
        }
    }
}
