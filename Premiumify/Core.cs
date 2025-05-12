using SystemCollectionsGeneric = System.Collections.Generic;
using Il2CppGwentGameplay;
using Il2CppGwentVisuals;
using MelonLoader;
using HarmonyLib;
using System.Diagnostics;

[assembly: MelonInfo(typeof(Premiumify.PremiumifyCore), "Premiumify", "1.0.0", "piotrekobi")]
[assembly: MelonGame("CDProjektRED", "Gwent")]

namespace Premiumify
{
    public class PremiumifyCore : MelonMod
    {
        private static MelonLogger.Instance staticLogger;
        internal static MelonPreferences_Category premiumifyCategory;
        internal static MelonPreferences_Entry<int> enablePremiumifyPref;
        private static int? _pendingEnablePremiumifyValue;
        private static bool _isGameplaySceneCurrentlyActive = false;

        internal const string ModId = "Premiumify";
        internal const string PremiumifySettingKey = "EnablePremiumify";
        internal const string PremiumifySettingLocKey = "panel_settings_entry_mods_premiumify_setting";
        internal const string EnabledLocKey = "panel_settings_entry_mods_premiumify_enabled";
        internal const string DisabledLocKey = "panel_settings_entry_mods_premiumify_disabled";

        [Conditional("DEBUG")] internal static void Log(string m) => staticLogger?.Msg($"[{ModId}] {m}");
        [Conditional("DEBUG")] private static void LogError(string m, Exception e = null) => staticLogger?.Error($"[{ModId}] {m}" + (e == null ? "" : $"\n{e}"));

        public override void OnInitializeMelon()
        {
            staticLogger = LoggerInstance;
            Log("Init");
            try
            {
                premiumifyCategory = MelonPreferences.CreateCategory(ModId);
                enablePremiumifyPref = premiumifyCategory.CreateEntry(PremiumifySettingKey, 1);
                Log("Prefs Loaded");
            }
            catch (Exception e) { LogError("Prefs Init Error", e); }

            RegisterPremiumifyTranslations();

            var switcherOptions = new SystemCollectionsGeneric.List<System.Tuple<string, Func<string>>>
            {
                System.Tuple.Create("enabled", (Func<string>)(() => EnabledLocKey)),
                System.Tuple.Create("disabled", (Func<string>)(() => DisabledLocKey)),
            };

            ModSettings.ModSettings.RegisterSwitcherSetting(ModId, PremiumifySettingKey, PremiumifySettingLocKey,
                switcherOptions,
                GetCurrentEnablePremiumifyValue,
                OnPremiumifySettingChangedInUI,
                HasPendingPremiumifyChanges,
                ApplyPendingPremiumifyChanges,
                RevertPendingPremiumifyChanges);
            Log("ModSettings Registered");

            try { HarmonyInstance.PatchAll(typeof(PremiumifyCore).Assembly); Log("Harmony Patched"); }
            catch (Exception e) { LogError("Harmony PatchAll Error", e); }
            Log("Init Complete");
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            base.OnSceneWasLoaded(buildIndex, sceneName);
            if (sceneName == "Gameplay") { _isGameplaySceneCurrentlyActive = true; Log("Gameplay Scene Loaded"); }
        }

        public override void OnSceneWasUnloaded(int buildIndex, string sceneName)
        {
            base.OnSceneWasUnloaded(buildIndex, sceneName);
            if (sceneName == "Gameplay") { _isGameplaySceneCurrentlyActive = false; Log("Gameplay Scene Unloaded"); }
        }

        private static void RegisterPremiumifyTranslations()
        {
            var settingTranslations = new SystemCollectionsGeneric.Dictionary<string, string>() {
                { "en-us", "Premiumify" }, { "pl-pl", "Premiumify" }, { "de-de", "Premiumify" }, { "ru-ru", "Премиумификация" }, { "fr-fr", "Premiumify" }, { "it-it", "Premiumify" },
                { "es-es", "Premiumify" }, { "es-mx", "Premiumify" }, { "pt-br", "Premiumify" }, { "zh-cn", "闪卡化" }, { "ja-jp", "プレミアム化" }, { "ko-kr", "프리미엄화" }};
            ModSettings.ModSettings.RegisterTranslationKey(ModId, PremiumifySettingLocKey, settingTranslations);

            var enabledTranslations = new SystemCollectionsGeneric.Dictionary<string, string>() {
                { "en-us", "ENABLED" }, { "pl-pl", "WŁĄCZONE" }, { "de-de", "AKTIVIERT" }, { "ru-ru", "ВКЛЮЧЕНО" }, { "fr-fr", "ACTIVÉ" }, { "it-it", "ABILITATO" }, { "es-es", "ACTIVADO" },
                { "es-mx", "ACTIVADO" }, { "pt-br", "ATIVADO" }, { "zh-cn", "已启用" }, { "ja-jp", "有効" }, { "ko-kr", "활성화됨" }};
            ModSettings.ModSettings.RegisterTranslationKey(ModId, EnabledLocKey, enabledTranslations);

            var disabledTranslations = new SystemCollectionsGeneric.Dictionary<string, string>() {
                { "en-us", "DISABLED" }, { "pl-pl", "WYŁĄCZONE" }, { "de-de", "DEAKTIVIERT" }, { "ru-ru", "ОТКЛЮЧЕНО" }, { "fr-fr", "DÉSACTIVÉ" }, { "it-it", "DISABILITATO" }, { "es-es", "DESACTIVADO" },
                { "es-mx", "DESACTIVADO" }, { "pt-br", "DESATIVADO" }, { "zh-cn", "已禁用" }, { "ja-jp", "無効" }, { "ko-kr", "비활성화됨" }};
            ModSettings.ModSettings.RegisterTranslationKey(ModId, DisabledLocKey, disabledTranslations);
            Log("Translations Registered");
        }

        private static object GetCurrentEnablePremiumifyValue()
        {
            int val = enablePremiumifyPref.Value;
            if (val == 1) return "enabled";
            return "disabled";
        }

        private static void OnPremiumifySettingChangedInUI(object newValIdObj)
        {
            if (newValIdObj is not string newValId) return;

            int currentPrefValue = enablePremiumifyPref.Value;
            int newIntValue = 0;

            if (newValId == "enabled") newIntValue = 1;
            if (newIntValue != currentPrefValue)
            {
                _pendingEnablePremiumifyValue = newIntValue;
            }
            else
            {
                _pendingEnablePremiumifyValue = null;
            }
        }

        private static bool HasPendingPremiumifyChanges() => _pendingEnablePremiumifyValue.HasValue;

        private static void ApplyPendingPremiumifyChanges()
        {
            if (!_pendingEnablePremiumifyValue.HasValue) return;
            if (_pendingEnablePremiumifyValue.Value != enablePremiumifyPref.Value)
            {
                enablePremiumifyPref.Value = _pendingEnablePremiumifyValue.Value;
            }
            _pendingEnablePremiumifyValue = null;
        }
        private static void RevertPendingPremiumifyChanges() { _pendingEnablePremiumifyValue = null; }

        public static class PremiumHelper
        {
            public static void ApplyPremium(Card c)
            {
                if (c == null || c.Definition.IsPremium || c.Definition.TemplateId == 0) return;
                var m = c.Definition; m.IsPremium = true; c.Definition = m;
                try { c.OnDefinitionChanged?.Invoke(c, c.Definition); } catch { }
            }
        }

        [HarmonyPatch(typeof(Card), "Play")]
        public static class Card_Play_Patch
        {
            static void Postfix(Card __instance)
            {
                if (PremiumifyCore.enablePremiumifyPref.Value == 1 && PremiumifyCore._isGameplaySceneCurrentlyActive) PremiumHelper.ApplyPremium(__instance);
            }
        }

        [HarmonyPatch(typeof(Il2CppGwentUnity.CardBattleView), "OnCardStartedToMoveLogic")]
        public static class CardBattleView_OnCardStartedToMoveLogic_Patch
        {
            static void Prefix(Card card)
            {
                if (PremiumifyCore.enablePremiumifyPref.Value == 1 && PremiumifyCore._isGameplaySceneCurrentlyActive && card != null) PremiumHelper.ApplyPremium(card);
            }
            static void Postfix(Card card)
            {
                if (PremiumifyCore.enablePremiumifyPref.Value == 1 && PremiumifyCore._isGameplaySceneCurrentlyActive && card != null) PremiumHelper.ApplyPremium(card);
            }
        }

        [HarmonyPatch(typeof(Card), "Init")]
        public static class Card_Init_Patch
        {
            static void Prefix(ref CardDefinition definition)
            {
                if (PremiumifyCore.enablePremiumifyPref.Value == 1 && PremiumifyCore._isGameplaySceneCurrentlyActive && !definition.IsPremium && definition.TemplateId != 0)
                    definition.IsPremium = true;
            }
            static void Postfix(Card __instance)
            {
                if (PremiumifyCore.enablePremiumifyPref.Value == 1 && PremiumifyCore._isGameplaySceneCurrentlyActive && __instance != null) PremiumHelper.ApplyPremium(__instance);
            }
        }

        [HarmonyPatch(typeof(SelectChoicesHandlerComponent), "AttachToGame")]
        public static class SelectChoicesHandlerComponent_AttachToGame_Patch
        {
            static void Postfix(SelectChoicesHandlerComponent __instance)
            {
                if (PremiumifyCore.enablePremiumifyPref.Value != 1 || !PremiumifyCore._isGameplaySceneCurrentlyActive || __instance == null) return;
                if (__instance.m_ValidChoiceCards != null) foreach (var card in __instance.m_ValidChoiceCards) PremiumHelper.ApplyPremium(card);
                if (__instance.m_SelectedChoiceCards != null) foreach (var card in __instance.m_SelectedChoiceCards) PremiumHelper.ApplyPremium(card);
            }
        }

        [HarmonyPatch(typeof(Il2CppGwentGameplay.Card), "Transform")]
        public static class Card_Transform_Patch
        {
            static void Postfix(Card __instance)
            {
                if (PremiumifyCore.enablePremiumifyPref.Value == 1 && PremiumifyCore._isGameplaySceneCurrentlyActive && __instance != null) PremiumHelper.ApplyPremium(__instance);
            }
        }

        [HarmonyPatch(typeof(Il2CppGwentVisuals.CardBattleViewAnimation), "SetFaceUpInstant")]
        public static class CardBattleViewAnimation_SetFaceUpInstant_Patch
        {
            static void Postfix(Il2CppGwentVisuals.CardBattleViewAnimation __instance)
            {
                if (PremiumifyCore.enablePremiumifyPref.Value == 1 && PremiumifyCore._isGameplaySceneCurrentlyActive &&
                    __instance?.BattleView?.Card != null) PremiumHelper.ApplyPremium(__instance.BattleView.Card);
            }
        }

        [HarmonyPatch(typeof(Il2CppGwentVisuals.CardBattleViewAnimation), "SetFaceUp", [typeof(bool), typeof(bool)])]
        public static class CardBattleViewAnimation_SetFaceUp_Bool_Bool_Patch
        {
            static void Postfix(Il2CppGwentVisuals.CardBattleViewAnimation __instance)
            {
                if (PremiumifyCore.enablePremiumifyPref.Value == 1 && PremiumifyCore._isGameplaySceneCurrentlyActive &&
                    __instance?.BattleView?.Card != null) PremiumHelper.ApplyPremium(__instance.BattleView.Card);
            }
        }

        [HarmonyPatch(typeof(Il2CppGwentVisuals.CardBattleViewAnimation), "SetFaceUp", [typeof(bool), typeof(bool), typeof(bool)])]
        public static class CardBattleViewAnimation_SetFaceUp_Bool_Bool_Bool_Patch
        {
            static void Postfix(Il2CppGwentVisuals.CardBattleViewAnimation __instance)
            {
                if (PremiumifyCore.enablePremiumifyPref.Value == 1 && PremiumifyCore._isGameplaySceneCurrentlyActive &&
                    __instance?.BattleView?.Card != null) PremiumHelper.ApplyPremium(__instance.BattleView.Card);
            }
        }
    }
}


