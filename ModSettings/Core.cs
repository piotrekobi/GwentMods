using System.Collections;
using MelonLoader;
using UnityEngine;
using HarmonyLib;
using Il2CppGwentUnity;
using UnityEngine.UI;
using Il2CppCrimsonUI;
using Il2CppTMPro;
using Il2CppLocalization;
using System.Diagnostics;

[assembly: MelonInfo(typeof(ModSettings.ModSettings), "ModSettings", "1.0.0", "piotrekobi")]
[assembly: MelonGame("CDProjektRED", "Gwent")]

namespace ModSettings
{
    public class ModSettings : MelonMod
    {
        private static MelonLogger.Instance staticLogger;

        internal const SettingsCategory TargetOriginalCategory = SettingsCategory.AUDIO;
        internal const string ModsButtonName = "ModsButton";
        internal const string ModCategoryContainerName = "MODS";
        internal const string ModCategoryLocalizationKey = "panel_settings_category_mods";

        private static readonly Dictionary<string, Dictionary<string, string>> AllModTranslations = new(StringComparer.OrdinalIgnoreCase);
        private static bool _initialTranslationsInjected;
        private static GameObject _modCategoryContainerGO;
        private static ButtonControl _submitButtonControl;
        internal static Action<ToggleButtonControl> _persistentToggleListener;
        internal static ToggleButtonControl _addedModsButton;
        internal static UISettingsPanel _panelInstanceForListener;
        private static bool _modUIInitialized = false;
        private static object _configCoroutineHandle;

        private static readonly List<RegisteredModSetting> RegisteredSettings = [];
        private static readonly Dictionary<string, Func<bool>> ModHasPendingChangesCallbacks = [];
        private static readonly Dictionary<string, Action> ModApplyPendingChangesCallbacks = [];
        private static readonly Dictionary<string, Action> ModRevertPendingChangesCallbacks = [];

        private static readonly List<string> RequiredLanguages = ["en-us", "pl-pl", "de-de", "ru-ru", "fr-fr", "it-it", "es-es", "es-mx", "pt-br", "zh-cn", "ja-jp", "ko-kr"];

        private struct RegisteredModSetting
        {
            public string ModId; public string SettingKey; public string DisplayNameKey; public SettingType Type;
            public Func<object> GetCurrentValue; public Action<object> OnValueChanged;
            public List<System.Tuple<string, Func<string>>> SwitcherOptions;
            public UISettingsEntry UIEntry; public Switcher UISwitcher;
        }

        private enum SettingType { Switcher }

        [Conditional("DEBUG")] private static void Log(string m) => staticLogger?.Msg($"[ModSettings] {m}");
        [Conditional("DEBUG")] private static void LogWarning(string m) => staticLogger?.Warning($"[ModSettings] {m}");
        [Conditional("DEBUG")] private static void LogError(string m, Exception e = null) => staticLogger?.Error($"[ModSettings] {m}" + (e == null ? "" : $"\n{e}"));

        public override void OnInitializeMelon()
        {
            staticLogger = LoggerInstance; Log("OnInitializeMelon");
            try { HarmonyInstance.PatchAll(typeof(ModSettings).Assembly); Log("Harmony Patched."); }
            catch (Exception e) { LogError("Harmony PatchAll Error", e); }
            var modCategoryTranslations = new Dictionary<string, string>() {
                { "en-us", "MODS" }, { "pl-pl", "MODY" }, { "de-de", "MODS" }, { "ru-ru", "МОДЫ" }, { "fr-fr", "MODS" }, { "it-it", "MODS" },
                { "es-es", "MODS" }, { "es-mx", "MODS" }, { "pt-br", "MODS" }, { "zh-cn", "模组" }, { "ja-jp", "モジュール" }, { "ko-kr", "모드" }
            };
            RegisterTranslationKey("ModSettings", ModCategoryLocalizationKey, modCategoryTranslations);
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            if (!_initialTranslationsInjected && sceneName == "MainMenu")
            { InjectAllModTranslations(); _initialTranslationsInjected = true; }
        }

        internal static void InjectAllModTranslations()
        {
            try
            {
                var lm = LocalizationManager.Instance; if (lm == null) return;
                var cl = lm.CurrentLanguage; var gt = lm.Translations; if (string.IsNullOrEmpty(cl) || gt == null) return;
                int totalInjected = 0;
                if (AllModTranslations.TryGetValue(cl, out var langTranslations))
                    foreach (var kvp in langTranslations)
                        if (!gt.ContainsKey(kvp.Key) || gt[kvp.Key] != kvp.Value) { gt[kvp.Key] = kvp.Value; totalInjected++; }
                if (totalInjected > 0) Log($"Injected {totalInjected} translations for '{cl}'.");
            }
            catch (Exception e) { LogError("InjectAllModTranslations Ex:", e); }
        }

        internal static void RegisterModTranslation(string modId, string languageCode, string key, string value)
        {
            if (string.IsNullOrEmpty(modId) || string.IsNullOrEmpty(languageCode) || string.IsNullOrEmpty(key)) { LogError($"Failed to register translation: invalid arguments (modId: {modId}, lang: {languageCode}, key: {key})"); return; }
            if (!AllModTranslations.TryGetValue(languageCode, out var langDict)) { langDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); AllModTranslations[languageCode] = langDict; }
            if (langDict.ContainsKey(key) && langDict[key] != value) LogWarning($"Translation key '{key}' for lang '{languageCode}' (mod '{modId}') is being overwritten.");
            langDict[key] = value;
            if (_initialTranslationsInjected && LocalizationManager.Instance?.CurrentLanguage == languageCode)
            {
                var gt = LocalizationManager.Instance.Translations;
                if (gt != null && (!gt.ContainsKey(key) || gt[key] != value)) { gt[key] = value; Log($"Dynamically injected translation for '{modId}': '{key}' = '{value}' for lang '{languageCode}'."); }
            }
        }

        public static void RegisterTranslationKey(string modId, string key, Dictionary<string, string> localizedValues)
        {
            if (string.IsNullOrEmpty(modId) || string.IsNullOrEmpty(key) || localizedValues == null) { LogError($"Failed to register translation key: invalid arguments (modId: {modId}, key: {key})"); return; }
            var missingLangs = RequiredLanguages.Where(rl => !localizedValues.ContainsKey(rl) || string.IsNullOrEmpty(localizedValues[rl])).ToList();
            if (missingLangs.Any()) { LogError($"Mod '{modId}' failed to register translations for key '{key}'. Missing: {string.Join(", ", missingLangs)}."); return; }
            foreach (var reqLang in RequiredLanguages) if (localizedValues.TryGetValue(reqLang, out var val) && !string.IsNullOrEmpty(val)) RegisterModTranslation(modId, reqLang, key, val);
            Log($"Successfully registered translations for key '{key}' from mod '{modId}' for all required languages.");
        }

        public static void RegisterSwitcherSetting(string modId, string settingKey, string displayNameKey,
            List<System.Tuple<string, Func<string>>> switcherOptions,
            Func<object> getCurrentValue, Action<object> onValueChangedCallback,
            Func<bool> hasPendingChangesCallback, Action applyPendingChangesCallback, Action revertPendingChangesCallback)
        {
            if (RegisteredSettings.Any(s => s.ModId == modId && s.SettingKey == settingKey)) { LogWarning($"Setting '{settingKey}' for mod '{modId}' already registered. Skipping."); return; }
            RegisteredSettings.Add(new RegisteredModSetting
            {
                ModId = modId,
                SettingKey = settingKey,
                DisplayNameKey = displayNameKey,
                Type = SettingType.Switcher,
                SwitcherOptions = switcherOptions,
                GetCurrentValue = getCurrentValue,
                OnValueChanged = onValueChangedCallback
            });
            string combinedKey = modId + "_" + settingKey;
            ModHasPendingChangesCallbacks[combinedKey] = hasPendingChangesCallback; ModApplyPendingChangesCallbacks[combinedKey] = applyPendingChangesCallback; ModRevertPendingChangesCallbacks[combinedKey] = revertPendingChangesCallback;
            Log($"Registered Switcher: Mod '{modId}', Key '{settingKey}'");
        }

        [HarmonyPatch(typeof(UISettingsPanel), "HandleShowing")]
        public static class UISettingsPanel_HandleShowing_Patch
        {
            static void Postfix(UISettingsPanel __instance)
            {
                Log("HandleShowing Postfix"); if (__instance == null) { LogError("HandleShowing: __instance NULL!"); return; }
                ModSettings.InjectAllModTranslations();
                try
                {
                    _panelInstanceForListener = __instance; FindAndCacheSubmitButton(__instance);
                    if (!_modUIInitialized) InitializeModUIFramework(__instance);
                    if (_addedModsButton?.gameObject != null) { Log("MODS button exists."); EnsureListenerAttached(_addedModsButton); HandleModButtonState(_addedModsButton.IsToggled); return; }
                    if (_addedModsButton != null) { LogWarning("Clearing stale _addedModsButton ref."); _addedModsButton = null; }
                    Log("Creating MODS button..."); ToggleButtonControl generalBtn = null; Transform btnContainer = null;
                    if (__instance.m_CategoryButtons != null) foreach (var entry in __instance.m_CategoryButtons) if (entry?.Category == TargetOriginalCategory && entry?.Button != null) { generalBtn = entry.Button; btnContainer = generalBtn.transform.parent; break; }
                    if (generalBtn == null || btnContainer == null) { LogError("General button/container not found."); return; }
                    ToggleButtonControl newBtn = __instance.AddCategoryButton(ModsButtonName, ModCategoryLocalizationKey);
                    if (newBtn == null) { LogError("AddCategoryButton failed for MODS."); return; }
                    newBtn.gameObject.name = ModsButtonName; newBtn.gameObject.SetActive(true); _addedModsButton = newBtn;
                    if (newBtn.transform.parent != btnContainer) newBtn.transform.SetParent(btnContainer, false);
                    newBtn.transform.SetSiblingIndex(generalBtn.transform.GetSiblingIndex() + 1); EnsureListenerAttached(newBtn);
                    if (btnContainer?.GetComponent<RectTransform>() is RectTransform rt) LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
                    Log($"MODS Button Added: {GetGameObjectPath(newBtn.transform)}");
                    if (!_addedModsButton.IsToggled) ShowOriginalCategory(TargetOriginalCategory);
                }
                catch (Exception ex) { LogError("HandleShowing Postfix Error", ex); _addedModsButton = null; CleanupModSettingsUI(); try { ShowOriginalCategory(TargetOriginalCategory); } catch { } }
            }

            static void InitializeModUIFramework(UISettingsPanel panelInstance)
            {
                var iPanel = panelInstance?.m_SettingsInnerPanel; var cats = iPanel?.m_CategoriesPlacement; if (iPanel == null || cats == null) { LogError("InitializeModUIFramework: InnerPanel/CatsPlacement Null."); return; }
                GameObject templateCatGO = (iPanel.m_Categories?.TryGetValue(SettingsCategory.GENERAL, out var gc) == true && gc != null) ? gc.gameObject : cats.Find("GENERAL")?.gameObject;
                if (templateCatGO == null) { LogError("InitializeModUIFramework: Template Category GO Null."); return; }
                GameObject templateEntryGO = FindDeepChild(FindDeepChild(templateCatGO.transform, "ScrollView"), "LOCALIZATION")?.gameObject;
                if (templateEntryGO == null) { LogError("InitializeModUIFramework: Template Entry GO Null."); return; }
                try
                {
                    if (_modCategoryContainerGO != null) GameObject.Destroy(_modCategoryContainerGO);
                    _modCategoryContainerGO = GameObject.Instantiate(templateCatGO, cats); if (_modCategoryContainerGO == null) throw new Exception("Instantiate mod category container failed.");
                    _modCategoryContainerGO.name = ModCategoryContainerName;
                    if (_modCategoryContainerGO.GetComponent<UIGeneralSettingsCategory>() is Behaviour b1) b1.enabled = false; if (_modCategoryContainerGO.GetComponent<UISettingsCategory>() is Behaviour b2) b2.enabled = false;
                    var cg = _modCategoryContainerGO.GetComponent<CanvasGroup>() ?? _modCategoryContainerGO.AddComponent<CanvasGroup>(); cg.interactable = true; cg.blocksRaycasts = true; cg.alpha = 1f; SetLayerRecursively(_modCategoryContainerGO, 5);

                    AContainer panelContainer = iPanel.GetComponent<AContainer>();
                    CrimsonScrollRect csr = _modCategoryContainerGO.GetComponentInChildren<CrimsonScrollRect>(true);
                    MinSizeHandleScrollbar vsb = _modCategoryContainerGO.GetComponentInChildren<MinSizeHandleScrollbar>(true);

                    if (csr != null)
                    {
                        csr.AllowMouseScroll = true;
                        if (panelContainer != null && panelContainer.ParentLayer != null)
                        {
                            csr.ParentLayer = panelContainer.ParentLayer;
                            Log($"Set CrimsonScrollRect layer to {csr.ParentLayer.Priority}. AllowMouseScroll=true.");
                        }
                        else
                        {
                            LogWarning("Could not set CrimsonScrollRect ParentLayer (panelContainer or its layer is null).");
                        }
                    }
                    else
                    {
                        LogWarning("CrimsonScrollRect not found in _modCategoryContainerGO.");
                    }

                    if (vsb != null)
                    {
                        vsb.interactable = true;
                        if (panelContainer != null && panelContainer.ParentLayer != null)
                        {
                            vsb.ParentLayer = panelContainer.ParentLayer;
                            Log($"Set VerticalScrollbar layer to {vsb.ParentLayer.Priority}. interactable=true.");
                        }
                        else
                        {
                            LogWarning("Could not set VerticalScrollbar ParentLayer (panelContainer or its layer is null).");
                        }
                    }
                    else
                    {
                        LogWarning("VerticalScrollbar (MinSizeHandleScrollbar) not found in _modCategoryContainerGO.");
                    }

                    Transform scrollT = FindDeepChild(_modCategoryContainerGO.transform, "ScrollView"); Transform contentT = FindDeepChild(scrollT, "PatternHolderFolder") ?? FindDeepChild(scrollT, "Content");
                    if (contentT != null) for (int i = contentT.childCount - 1; i >= 0; i--) GameObject.Destroy(contentT.GetChild(i).gameObject); else { LogError("InitializeModUIFramework: Content/PatternHolderFolder Null."); return; }
                    _modCategoryContainerGO.SetActive(false); _modUIInitialized = true; if (_configCoroutineHandle != null) MelonCoroutines.Stop(_configCoroutineHandle);
                    _configCoroutineHandle = MelonCoroutines.Start(PopulateModSettingsCoroutine(templateEntryGO, iPanel, contentT)); Log("Mod UI Framework Initialized, starting population coroutine.");
                }
                catch (Exception ex) { LogError("Error during Mod UI Framework initialization", ex); CleanupModSettingsUI(); _modUIInitialized = false; }
            }

            static IEnumerator PopulateModSettingsCoroutine(GameObject entryPrefab, UISettingsInnerPanel innerPanel, Transform contentParent)
            {
                yield return null; if (contentParent == null || entryPrefab == null) { LogError("PopulateModSettingsCoroutine: contentParent or entryPrefab is null."); _configCoroutineHandle = null; yield break; }
                bool modContainerShouldBeActive = _addedModsButton != null && _addedModsButton.IsToggled; bool madeActiveForPopulation = false;
                if (_modCategoryContainerGO != null && !_modCategoryContainerGO.activeSelf) { Log("PopulateModSettingsCoroutine: Activating mod container."); _modCategoryContainerGO.SetActive(true); madeActiveForPopulation = true; }
                for (int i = 0; i < RegisteredSettings.Count; i++)
                {
                    var setting = RegisteredSettings[i]; GameObject entryInstance = null;
                    try
                    {
                        entryInstance = GameObject.Instantiate(entryPrefab, contentParent); if (entryInstance == null) throw new Exception($"Instantiate entry for {setting.ModId}_{setting.SettingKey} failed.");
                        entryInstance.name = $"{setting.ModId}_{setting.SettingKey}_Entry"; entryInstance.SetActive(true); SetLayerRecursively(entryInstance, 5);
                        var uiSettingsEntry = entryInstance.GetComponent<UISettingsEntry>() ?? throw new Exception("Missing UISettingsEntry component.");
                        if (entryInstance.GetComponent<AControl>() is AControl entryCtrl && innerPanel.GetComponent<AContainer>() is AContainer panelAContainer && entryCtrl.Parent != panelAContainer) panelAContainer.AddChild(entryCtrl);
                        TextMeshProUGUI titleLbl = null; LocalizedTextMeshPro locComp = null; Switcher switcher = entryInstance.GetComponentInChildren<Switcher>(true) ?? throw new Exception("Missing Switcher component.");
                        if (!switcher.gameObject.activeSelf) switcher.gameObject.SetActive(true);
                        foreach (var lbl in entryInstance.GetComponentsInChildren<TextMeshProUGUI>(true)) if (lbl != null && !lbl.transform.IsChildOf(switcher.transform)) { titleLbl = lbl; locComp = titleLbl.GetComponent<LocalizedTextMeshPro>(); break; }
                        if (titleLbl != null) { if (locComp != null) locComp.enabled = false; titleLbl.text = LocalizationManager.Instance?.TryGetTranslationText(setting.DisplayNameKey) ?? setting.DisplayNameKey; } else LogWarning($"No title label for {setting.SettingKey}");

                        var localSetting = setting; int localIndex = i;
                        switcher.ClearItems();
                        if (localSetting.SwitcherOptions != null)
                        {
                            foreach (var option in localSetting.SwitcherOptions)
                            {
                                switcher.AddItem(option.Item1, option.Item2(), true);
                            }
                        }

                        switcher.OnArrowClicked?.RemoveAllListeners(); switcher.OnArrowClicked?.AddListener((Il2CppSystem.Action)(() => { }));
                        switcher.OnValueChanged?.RemoveAllListeners();
                        switcher.OnValueChanged?.AddListener((Il2CppSystem.Action<Switcher>)((Switcher s) => {
                            if (s?.Value == null) return;
                            localSetting.OnValueChanged(s.Value.Id);
                            UpdateSubmitButtonState();
                        }));


                        object currentValueObj = localSetting.GetCurrentValue();
                        string currentIdToSelect = null;
                        if (currentValueObj != null)
                        {
                            currentIdToSelect = currentValueObj.ToString();
                        }

                        bool idFound = false;
                        if (currentIdToSelect != null && localSetting.SwitcherOptions != null)
                        {
                            foreach (var opt in localSetting.SwitcherOptions)
                            {
                                if (opt.Item1 == currentIdToSelect)
                                {
                                    idFound = true;
                                    break;
                                }
                            }
                        }

                        if (idFound)
                        {
                            switcher.SelectItemById(currentIdToSelect);
                        }
                        else if (localSetting.SwitcherOptions != null && localSetting.SwitcherOptions.Count > 0)
                        {
                            switcher.SelectItemById(localSetting.SwitcherOptions[0].Item1);
                        }

                        var updatedSetting = RegisteredSettings[localIndex]; updatedSetting.UIEntry = uiSettingsEntry; updatedSetting.UISwitcher = switcher; RegisteredSettings[localIndex] = updatedSetting; Log($"Configured UI: {localSetting.ModId} - {localSetting.SettingKey}");
                    }
                    catch (Exception ex) { LogError($"Error setting up UI for {setting.ModId}_{setting.SettingKey}: {ex.Message}", ex); if (entryInstance != null) GameObject.Destroy(entryInstance); }
                }
                var catPlace = _modCategoryContainerGO?.transform?.parent; var catRect = _modCategoryContainerGO?.GetComponent<RectTransform>(); var contRect = contentParent?.GetComponent<RectTransform>();
                if (contRect != null) LayoutRebuilder.ForceRebuildLayoutImmediate(contRect); if (catRect != null) LayoutRebuilder.ForceRebuildLayoutImmediate(catRect); if (catPlace?.GetComponent<RectTransform>() is RectTransform cpRect) LayoutRebuilder.ForceRebuildLayoutImmediate(cpRect);
                Log("Mod UI Entries Populated."); UpdateSubmitButtonState();
                if (madeActiveForPopulation && !modContainerShouldBeActive && _modCategoryContainerGO?.activeSelf == true) { Log("Deactivating mod container post-population."); _modCategoryContainerGO.SetActive(false); }
                if (_modCategoryContainerGO?.activeSelf == true && _addedModsButton?.IsToggled == false) { LogWarning("Mods button not toggled post-population. Hiding container."); _modCategoryContainerGO.SetActive(false); }
                _configCoroutineHandle = null;
            }

            static void HandleModButtonState(bool isToggled)
            {
                Log($"MODS button toggled: {isToggled}");
                if (!_modUIInitialized || _modCategoryContainerGO == null) { if (isToggled) LogError("Mod UI not ready."); if (!isToggled) ShowOriginalCategory(TargetOriginalCategory); return; }
                if (isToggled)
                {
                    if (_configCoroutineHandle != null) LogWarning("Mod button ON during config coroutine."); HideOriginalCategories();
                    foreach (var setting in RegisteredSettings)
                    {
                        if (setting.Type == SettingType.Switcher && setting.UISwitcher != null)
                        {
                            object currentValue = setting.GetCurrentValue();
                            if (currentValue != null)
                            {
                                setting.UISwitcher.SelectItemById(currentValue.ToString());
                            }
                            else if (setting.SwitcherOptions != null && setting.SwitcherOptions.Count > 0)
                            {
                                setting.UISwitcher.SelectItemById(setting.SwitcherOptions[0].Item1);
                            }
                        }
                    }
                    if (!_modCategoryContainerGO.activeSelf) _modCategoryContainerGO.SetActive(true);
                }
                else { if (_modCategoryContainerGO.activeSelf) _modCategoryContainerGO.SetActive(false); ShowOriginalCategory(TargetOriginalCategory); }
                UpdateSubmitButtonState();
            }

            static void EnsureListenerAttached(ToggleButtonControl btnCtrl)
            {
                if (btnCtrl == null) return; if (ModSettings._addedModsButton == null && btnCtrl.name == ModsButtonName) ModSettings._addedModsButton = btnCtrl;
                try
                {
                    var onToggle = btnCtrl.OnToggle; if (onToggle == null) return;
                    ModSettings._persistentToggleListener ??= (ToggleButtonControl tBtn) => { if (tBtn != null) HandleModButtonState(tBtn.GetInstanceID() == ModSettings._addedModsButton?.GetInstanceID() && tBtn.IsToggled); };
                    onToggle.RemoveListener(ModSettings._persistentToggleListener); onToggle.AddListener(ModSettings._persistentToggleListener);
                }
                catch (Exception e) { LogError($"EnsureListenerAttached Error: {e}"); }
            }
        }

        private static string GetGameObjectPath(Transform t) { if (t == null) return "null"; string p = t.name; while (t.parent != null) { t = t.parent; p = t.name + "/" + p; } return p; }
        private static Transform FindDeepChild(Transform parent, string childName) { if (parent == null) return null; var q = new Queue<Transform>(); q.Enqueue(parent); while (q.Count > 0) { var c = q.Dequeue(); if (c.name == childName) return c; for (int i = 0; i < c.childCount; i++) q.Enqueue(c.GetChild(i)); } return null; }

        private static void UpdateSubmitButtonState()
        {
            if (_panelInstanceForListener == null) return; if (_submitButtonControl == null) FindAndCacheSubmitButton(_panelInstanceForListener); if (_submitButtonControl == null) return;
            bool gameChanged = false; try { gameChanged = _panelInstanceForListener.AreSettingsChanged(); } catch { }
            bool modChanged = ModHasPendingChangesCallbacks.Values.Any(cb => cb != null && cb()); bool enable = gameChanged || modChanged;
            if (_submitButtonControl.IsEnabled != enable) try { _submitButtonControl.SetEnabled(enable); } catch { try { _submitButtonControl.gameObject.SetActive(enable); } catch { } }
        }

        private static void FindAndCacheSubmitButton(UISettingsPanel panel)
        {
            if (panel == null || _submitButtonControl != null) return; InputButtonContainer btnContainer = panel.m_ButtonsContainerInstance; if (btnContainer == null) return;
            string saveKey = UISettingsPanel.SAVE_SETTINGS_BUTTON_KEY, translatedSave = LocalizationManager.Instance?.TryGetTranslationText(saveKey);
            foreach (var btn in btnContainer.GetComponentsInChildren<ButtonControl>(true))
            {
                if (btn == null) continue; if (btn.name == saveKey) { _submitButtonControl = btn; return; }
                if (!string.IsNullOrEmpty(translatedSave)) foreach (var lbl in btn.GetComponentsInChildren<TextMeshProUGUI>(true)) if (lbl?.text == translatedSave) { _submitButtonControl = btn; return; }
            }
            if (_submitButtonControl == null) LogWarning("Could not find Submit/Save button.");
        }

        private static void CleanupModSettingsUI()
        {
            if (_configCoroutineHandle != null) { MelonCoroutines.Stop(_configCoroutineHandle); _configCoroutineHandle = null; }
            for (int i = 0; i < RegisteredSettings.Count; i++) { var s = RegisteredSettings[i]; if (s.UIEntry?.gameObject != null) GameObject.Destroy(s.UIEntry.gameObject); s.UIEntry = null; s.UISwitcher = null; RegisteredSettings[i] = s; }
            if (_modCategoryContainerGO != null) { GameObject.Destroy(_modCategoryContainerGO); _modCategoryContainerGO = null; }
            _modUIInitialized = false; Log("Mod Settings UI Cleaned Up.");
        }

        private static void HideOriginalCategories()
        {
            var iPanel = _panelInstanceForListener?.m_SettingsInnerPanel; if (iPanel == null) return; var catsPlacement = iPanel.m_CategoriesPlacement;
            var iPanelCG = iPanel.GetComponent<CanvasGroup>() ?? iPanel.gameObject.AddComponent<CanvasGroup>(); iPanelCG.interactable = true; iPanelCG.blocksRaycasts = true; iPanelCG.alpha = 1f;
            if (catsPlacement != null) for (int i = 0; i < catsPlacement.childCount; i++)
                {
                    var child = catsPlacement.GetChild(i); if (child == null) continue;
                    bool isOurModContainer = _modCategoryContainerGO != null && child.gameObject.GetInstanceID() == _modCategoryContainerGO.GetInstanceID();
                    if (!isOurModContainer && child.gameObject.activeSelf) child.gameObject.SetActive(false); else if (isOurModContainer && !child.gameObject.activeSelf) child.gameObject.SetActive(true);
                }
        }

        private static void ShowOriginalCategory(SettingsCategory catToShow)
        {
            var iPanel = _panelInstanceForListener?.m_SettingsInnerPanel; if (iPanel == null) return;
            if (_modCategoryContainerGO?.activeSelf == true) _modCategoryContainerGO.SetActive(false);
            var iPanelCG = iPanel.GetComponent<CanvasGroup>() ?? iPanel.gameObject.AddComponent<CanvasGroup>(); iPanelCG.interactable = true; iPanelCG.blocksRaycasts = true; iPanelCG.alpha = 1f;
            try { iPanel.ShowCategory(catToShow); }
            catch (Exception ex)
            {
                LogError($"ShowCategory for {catToShow} failed. Fallback...", ex); var catsPlacement = iPanel.m_CategoriesPlacement;
                if (catsPlacement != null) for (int i = 0; i < catsPlacement.childCount; i++)
                    {
                        var child = catsPlacement.GetChild(i); if (child == null) continue;
                        bool shouldBeActive = child.name.Equals(catToShow.ToString(), StringComparison.OrdinalIgnoreCase); if (child.gameObject.activeSelf != shouldBeActive) child.gameObject.SetActive(shouldBeActive);
                    }
            }
        }

        private static void SetLayerRecursively(GameObject obj, int layer)
        {
            if (obj == null) return; obj.layer = layer;
            for (int i = 0; i < obj.transform.childCount; i++) { Transform childTransform = obj.transform.GetChild(i); if (childTransform != null) SetLayerRecursively(childTransform.gameObject, layer); }
        }

        [HarmonyPatch(typeof(UISettingsPanel), "OnCategoryButtonToggled")]
        public static class UISettingsPanel_OnCategoryButtonToggled_Patch
        {
            static bool Prefix(AControl control)
            {
                Log($"OnCategoryButtonToggled Prefix: Control '{control?.name ?? "NULL"}'");
                try { if (ModSettings._addedModsButton != null && control?.GetInstanceID() == ModSettings._addedModsButton.GetInstanceID()) return false; if (_modCategoryContainerGO?.activeSelf == true) { _modCategoryContainerGO.SetActive(false); Log("Hiding MODS container."); } return true; }
                catch (Exception ex) { LogError("OnCategoryButtonToggled Prefix Error", ex); if (_modCategoryContainerGO?.activeSelf == true) _modCategoryContainerGO.SetActive(false); return true; }
            }
        }

        private static void ApplyAllPendingModChanges(string source)
        {
            Log($"ApplyAllPendingModChanges by: {source}"); bool madeChanges = false;
            foreach (var kvp in ModApplyPendingChangesCallbacks)
                if (ModHasPendingChangesCallbacks.TryGetValue(kvp.Key, out var hasCb) && hasCb?.Invoke() == true && kvp.Value != null)
                    try { kvp.Value(); madeChanges = true; Log($"Applied for {kvp.Key}"); } catch (Exception e) { LogError($"Error applying for {kvp.Key}", e); }
            if (madeChanges) MelonPreferences.Save(); UpdateSubmitButtonState();
        }

        private static void RevertAllPendingModChanges(string source)
        {
            Log($"RevertAllPendingModChanges by: {source}");
            foreach (var kvp in ModRevertPendingChangesCallbacks)
                if (ModHasPendingChangesCallbacks.TryGetValue(kvp.Key, out var hasCb) && hasCb?.Invoke() == true && kvp.Value != null)
                    try { kvp.Value(); Log($"Reverted for {kvp.Key}"); } catch (Exception e) { LogError($"Error reverting for {kvp.Key}", e); }
            UpdateSubmitButtonState();
        }

        [HarmonyPatch(typeof(UISettingsPanel), "SaveSettings")] public static class UISettingsPanel_SaveSettings_Patch { static void Postfix() => ApplyAllPendingModChanges("SaveSettings"); }
        [HarmonyPatch(typeof(UISettingsPanel), "OnSave")] public static class UISettingsPanel_OnSave_Patch { static void Postfix() => ApplyAllPendingModChanges("OnSave"); }
        [HarmonyPatch(typeof(UISettingsPanel), "HandleHiding")]
        public static class UISettingsPanel_HandleHiding_Patch
        {
            static void Postfix()
            {
                Log("HandleHiding Postfix - Reverting & Cleaning."); RevertAllPendingModChanges("HandleHiding"); CleanupModSettingsUI();
                _panelInstanceForListener = null; _addedModsButton = null; _submitButtonControl = null;
            }
        }
        [HarmonyPatch(typeof(UISettingsPanel), "OnBack")]
        public static class UISettingsPanel_OnBack_Patch { static void Prefix() { Log("OnBack Prefix - Reverting."); RevertAllPendingModChanges("OnBack"); } }
    }
}