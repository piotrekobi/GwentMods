using HarmonyLib;
using Il2CppGwentGameplay;
using Il2CppGwentGameplay.Audio;
using Il2CppGwentUnity;
using Il2CppGwentUnity.Audio;
using Il2CppGwentVisuals;
using MelonLoader;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

[assembly: MelonInfo(typeof(CustomPremiums.CustomPremiumsMod), "CustomPremiums", "5.0.0", "piotrekobi")]
[assembly: MelonGame("CDProjektRED", "Gwent")]

namespace CustomPremiums;

/// <summary>
/// CustomPremiums v5 - Config-driven custom premium loading.
/// Donor card provides premium animation bundle; target card keeps its own voicelines/audio.
///
///   HOOK 0: GwentApp.HandleDefinitionsLoaded -> map ArtIds to TemplateIds
///   HOOK 1: Card.SetDefinition            -> force IsPremium = true
///   HOOK 2: CardDefinition.IsPremiumDisabled -> force return false
///   HOOK 3: CardViewAssetComponent.ShouldLoadPremium -> force true for our cards
///   HOOK 4: CardAppearanceRequest.HandleTextureRequestsFinished
///           -> load custom bundle + scene, skip normal pipeline
///   HOOK 5: CardAppearanceRequest.OnAppearanceObjectLoaded
///           -> swap texture with custom art, fix broken shaders
/// </summary>
public class CustomPremiumsMod : MelonMod
{
    internal static MelonLogger.Instance Logger;

    // We now dynamically find TemplateIds based on the ArtIds of the custom bundles/textures found on disk
    public static readonly HashSet<int> TargetTemplateIds = new();
    public static readonly Dictionary<int, int> TemplateIdToArtId = new(); // Maps TemplateId to ArtId dynamically

    // ArtId -> absolute path to custom bundle
    public static readonly Dictionary<int, string> CustomBundles = new();
    // ArtId -> absolute path to custom texture
    public static readonly Dictionary<int, string> CustomTextures = new();

    // ArtId -> donor ArtId (loaded from donor_config.json, written by build.py)
    public static readonly Dictionary<int, int> DonorConfig = new();

    // donorAudioId -> originalAudioId (for voiceline redirection in Hook 6)
    public static readonly Dictionary<int, int> AudioIdRedirectMap = new();

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
            HarmonyInstance.PatchAll(typeof(CustomPremiumsMod).Assembly);
            Logger.Msg("All Harmony patches applied!");
        }
        catch (Exception e)
        {
            Logger.Error($"Harmony PatchAll failed: {e}");
        }
    }

    private static void ScanFiles()
    {
        string modDir = Path.Combine(GameDir, "Mods", "CustomPremiums");
        string bundlesPath = Path.Combine(modDir, "Bundles");
        string texturesPath = Path.Combine(modDir, "Textures");
        string donorConfigPath = Path.Combine(modDir, "donor_config.json");

        Logger.Msg($"[Scan] Bundles dir: {bundlesPath} (exists: {Directory.Exists(bundlesPath)})");
        Logger.Msg($"[Scan] Textures dir: {texturesPath} (exists: {Directory.Exists(texturesPath)})");

        if (!Directory.Exists(bundlesPath)) Directory.CreateDirectory(bundlesPath);
        if (!Directory.Exists(texturesPath)) Directory.CreateDirectory(texturesPath);

        // Load donor config (maps artId -> donorArtId for audio)
        if (File.Exists(donorConfigPath))
        {
            try
            {
                string json = File.ReadAllText(donorConfigPath);
                // Simple JSON parsing: {"1832": 1349, "1833": 1191}
                // MelonLoader doesn't ship Newtonsoft, so parse manually
                json = json.Trim().TrimStart('{').TrimEnd('}');
                foreach (var pair in json.Split(','))
                {
                    var kv = pair.Split(':');
                    if (kv.Length == 2)
                    {
                        string key = kv[0].Trim().Trim('"');
                        string val = kv[1].Trim().Trim('"');
                        if (int.TryParse(key, out int artId) && int.TryParse(val, out int donorId))
                        {
                            DonorConfig[artId] = donorId;
                            Logger.Msg($"[Scan] Donor config: ArtId {artId} -> donor {donorId}");
                        }
                    }
                }
                Logger.Msg($"[Scan] Loaded {DonorConfig.Count} donor mapping(s) from donor_config.json");
            }
            catch (Exception e)
            {
                Logger.Warning($"[Scan] Failed to parse donor_config.json: {e.Message}");
            }
        }
        else
        {
            Logger.Msg("[Scan] No donor_config.json found (audio will use card's own AudioId)");
        }

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
            // Guard: HandleDefinitionsLoaded fires twice during init
            if (TargetTemplateIds.Count > 0)
            {
                Logger.Msg("[Init] HandleDefinitionsLoaded fired again — skipping (already initialized)");
                return;
            }

            var sharedData = GwentApp.Instance?.SharedData;
            if (sharedData?.SharedRuntimeTemplates == null) return;

            // Collect unique donor ArtIds we need AudioIds for
            var donorArtIds = new HashSet<int>();
            foreach (var donorId in DonorConfig.Values)
                donorArtIds.Add(donorId);

            // First pass: find AudioIds for all donor cards
            var donorAudioIds = new Dictionary<int, int>(); // donorArtId -> audioId
            foreach (var kvp in sharedData.SharedRuntimeTemplates)
            {
                var t = kvp.Value;
                if (t?.ArtDefinition != null && donorArtIds.Contains(t.ArtDefinition.ArtId))
                {
                    donorAudioIds[t.ArtDefinition.ArtId] = t.Template.AudioId;
                    Logger.Msg($"[Init] Donor AudioId: {t.Template.AudioId} (from ArtId {t.ArtDefinition.ArtId})");
                }
            }

            // Second pass: register custom cards, swap AudioId to donor's for premium SFX
            foreach (var kvp in sharedData.SharedRuntimeTemplates)
            {
                var template = kvp.Value;
                if (template != null && template.Template != null && template.ArtDefinition != null)
                {
                    int artId = template.ArtDefinition.ArtId;
                    if (CustomBundles.ContainsKey(artId) || CustomTextures.ContainsKey(artId))
                    {
                        TargetTemplateIds.Add(template.Template.Id);
                        TemplateIdToArtId[template.Template.Id] = artId;

                        // Swap AudioId to donor's for premium SFX/soundbank loading
                        // Voicelines are redirected back to original in Hook 6
                        if (DonorConfig.TryGetValue(artId, out int donorArtId)
                            && donorAudioIds.TryGetValue(donorArtId, out int donorAudioId))
                        {
                            int originalAudioId = template.Template.AudioId;
                            template.Template.AudioId = donorAudioId;
                            AudioIdRedirectMap[donorAudioId] = originalAudioId;
                            Logger.Msg($"[Init] ArtId {artId} -> TemplateId {template.Template.Id}: AudioId {originalAudioId} -> {donorAudioId} (voicelines redirect back via Hook 6)");
                        }
                        else
                        {
                            Logger.Msg($"[Init] Mapped ArtId {artId} -> TemplateId {template.Template.Id}");
                        }
                    }
                }
            }

            Logger.Msg($"[Init] Registered {TargetTemplateIds.Count} custom card(s), {AudioIdRedirectMap.Count} voiceline redirect(s)");
        }
    }

    // =====================================================================
    // HOOK 1: Force IsPremium = true
    // =====================================================================
    [HarmonyPatch(typeof(Card), "SetDefinition")]
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
    [HarmonyPatch(typeof(CardDefinition), "IsPremiumDisabled")]
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
                    Texture2D tex = new(2, 2);
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

                // Generic fallback: if any material still has InternalErrorShader after
                // the build-time patcher ran, try to fix it with a working shader at runtime.
                string[] fallbackNames = [
                    "ShaderLibrary/Generic/GwentStandard",
                    "GwentStandard",
                    "VFX/Common/AlphaBlended",
                ];
                Shader fallbackShader = null;
                foreach (var name in fallbackNames)
                {
                    fallbackShader = Shader.Find(name);
                    if (fallbackShader != null) break;
                }

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
                            if (fallbackShader != null)
                            {
                                m.shader = fallbackShader;
                                Logger.Warning($"[HOOK 5] Runtime shader fix: {m.name} -> {fallbackShader.name}");
                            }
                            else
                            {
                                Logger.Warning($"[HOOK 5] Broken shader on '{m.name}' — no fallback shader found");
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
                        if (assignment?.Material == null) continue;
                        try
                        {
                            assignment.AssignTexture(customTex);
                            Logger.Msg($"[HOOK 5] Texture applied via Handler on '{assignment.Material.name}'");
                        }
                        catch
                        {
                            // AssignTexture calls GetMaterialCopy which needs renderer context
                            // that isn't available on freshly-loaded prefab instances.
                            // Fall back to setting _MainTex directly on the material.
                            assignment.Material.mainTexture = customTex;
                            Logger.Msg($"[HOOK 5] Texture applied directly on '{assignment.Material.name}'");
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

    // =====================================================================
    // HOOK 6: Redirect voicelines back to original AudioId
    // We swapped AudioId to donor's for premium SFX (Hook 0), but voicelines
    // should use the original card's AudioId. This hooks the int overload of
    // GenerateVoiceover which has real logic (not inlined by Il2Cpp, unlike
    // the Card overload which is a one-liner wrapper).
    // =====================================================================
    [HarmonyPatch(typeof(VoiceDuplicateFilter), "GenerateVoiceover", [typeof(int), typeof(ECardAudioTriggerType)])]
    public static class Hook6_VoicelineRedirect
    {
        static void Prefix(ref int cardAudioId)
        {
            if (AudioIdRedirectMap.TryGetValue(cardAudioId, out int originalAudioId))
            {
                Logger.Msg($"[HOOK 6] Voiceline redirect: AudioId {cardAudioId} -> {originalAudioId}");
                cardAudioId = originalAudioId;
            }
        }
    }
}
