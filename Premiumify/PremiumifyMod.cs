using HarmonyLib;
using Il2CppGwentGameplay;
using Il2CppGwentVisuals;
using MelonLoader;
using ModSettings;
using System.Diagnostics;

[assembly: MelonInfo(typeof(Premiumify.PremiumifyMod), "Premiumify", "1.0.0", "piotrekobi")]
[assembly: MelonGame("CDProjektRED", "Gwent")]

namespace Premiumify;

public class PremiumifyMod : MelonMod
{
    private static MelonLogger.Instance staticLogger;
    internal static MelonPreferences_Entry<bool> isPremiumifyEnabledPref;
    private static bool _isGameplaySceneCurrentlyActive = false;
    internal const string ModId = "Premiumify";

    [Conditional("DEBUG")] internal static void Log(string m) => staticLogger?.Msg($"[{ModId}] {m}");
    [Conditional("DEBUG")] private static void LogError(string m, Exception e = null) => staticLogger?.Error($"[{ModId}] {m}" + (e == null ? "" : $"\n{e}"));

    public override void OnInitializeMelon()
    {
        staticLogger = LoggerInstance;
        Log("Init");
        try
        {
            isPremiumifyEnabledPref = MelonPreferences.CreateCategory(ModId).CreateEntry("EnablePremiumify", true);
            Log("Prefs Loaded");
        }
        catch (Exception e) { LogError("Prefs Init Error", e); }

        var settingTranslationKey = ModSettingsMod.RegisterTranslationKey(ModId, "Premiumify_Enabled_Switch", new Dictionary<string, string>() {
            { "en-us", "Premiumify" }, { "pl-pl", "Premiumify" }, { "de-de", "Premiumify" }, { "ru-ru", "Премиумификация" }, { "fr-fr", "Premiumify" }, { "it-it", "Premiumify" }, 
            { "es-es", "Premiumify" }, { "es-mx", "Premiumify" }, { "pt-br", "Premiumify" }, { "zh-cn", "闪卡化" }, { "ja-jp", "プレミアム化" }, { "ko-kr", "프리미엄화" }});

        var switcherOptions = new List<string>
        {
            ModSettingsMod.RegisterTranslationKey(ModId, true.ToString(), new Dictionary<string, string>() {
                { "en-us", "ENABLED" }, { "pl-pl", "WŁĄCZONE" }, { "de-de", "AKTIVIERT" }, { "ru-ru", "ВКЛЮЧЕНО" }, { "fr-fr", "ACTIVÉ" }, { "it-it", "ABILITATO" }, { "es-es", "ACTIVADO" },
                { "es-mx", "ACTIVADO" }, { "pt-br", "ATIVADO" }, { "zh-cn", "已启用" }, { "ja-jp", "有効" }, { "ko-kr", "활성화됨" }}),

            ModSettingsMod.RegisterTranslationKey(ModId, false.ToString(), new Dictionary<string, string>() {
                { "en-us", "DISABLED" }, { "pl-pl", "WYŁĄCZONE" }, { "de-de", "DEAKTIVIERT" }, { "ru-ru", "ОТКЛЮЧЕНО" }, { "fr-fr", "DÉSACTIVÉ" }, { "it-it", "DISABILITATO" }, { "es-es", "DESACTIVADO" },
                { "es-mx", "DESACTIVADO" }, { "pt-br", "DESATIVADO" }, { "zh-cn", "已禁用" }, { "ja-jp", "無効" }, { "ko-kr", "비활성화됨" }}),
        };

        bool? _pendingEnablePremiumifyValue = null;
        ModSettingsMod.RegisterSwitcherSetting(
            ModId,
            settingTranslationKey,
            switcherOptions,
            getCurrentValue: () => isPremiumifyEnabledPref.Value.ToString(),
            onValueChangedCallback: val => { if (val is string strVal) { bool newBool = bool.Parse(strVal); _pendingEnablePremiumifyValue = newBool != isPremiumifyEnabledPref.Value ? newBool : null; } },
            hasPendingChangesCallback: () => _pendingEnablePremiumifyValue.HasValue,
            applyPendingChangesCallback: () => { if (_pendingEnablePremiumifyValue.HasValue) { isPremiumifyEnabledPref.Value = _pendingEnablePremiumifyValue.Value; _pendingEnablePremiumifyValue = null; } },
            revertPendingChangesCallback: () => _pendingEnablePremiumifyValue = null);
        Log("ModSettings Registered");

        try { HarmonyInstance.PatchAll(typeof(PremiumifyMod).Assembly); Log("Harmony Patched"); }
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

    public static void ApplyPremium(Card card)
    {
        if (card == null || card.Definition.IsPremium || card.Definition.TemplateId == 0) return;
        var definition = card.Definition;
        definition.IsPremium = true;
        card.Definition = definition;
        try { card.OnDefinitionChanged?.Invoke(card, card.Definition); } catch { }
    }

    [HarmonyPatch(typeof(Card), "Play")]
    public static class Card_Play_Patch
    {
        static void Postfix(Card __instance)
        {
            if (isPremiumifyEnabledPref.Value && _isGameplaySceneCurrentlyActive) ApplyPremium(__instance);
        }
    }

    [HarmonyPatch(typeof(Il2CppGwentUnity.CardBattleView), "OnCardStartedToMoveLogic")]
    public static class CardBattleView_OnCardStartedToMoveLogic_Patch
    {
        static void Prefix(Card card)
        {
            if (isPremiumifyEnabledPref.Value && _isGameplaySceneCurrentlyActive && card != null) ApplyPremium(card);
        }
        static void Postfix(Card card)
        {
            if (isPremiumifyEnabledPref.Value && _isGameplaySceneCurrentlyActive && card != null) ApplyPremium(card);
        }
    }

    [HarmonyPatch(typeof(Card), "Init")]
    public static class Card_Init_Patch
    {
        static void Prefix(ref CardDefinition definition)
        {
            if (isPremiumifyEnabledPref.Value && _isGameplaySceneCurrentlyActive && !definition.IsPremium && definition.TemplateId != 0)
                definition.IsPremium = true;
        }
        static void Postfix(Card __instance)
        {
            if (isPremiumifyEnabledPref.Value && _isGameplaySceneCurrentlyActive && __instance != null) ApplyPremium(__instance);
        }
    }

    [HarmonyPatch(typeof(SelectChoicesHandlerComponent), "AttachToGame")]
    public static class SelectChoicesHandlerComponent_AttachToGame_Patch
    {
        static void Postfix(SelectChoicesHandlerComponent __instance)
        {
            if (!isPremiumifyEnabledPref.Value || !_isGameplaySceneCurrentlyActive || __instance == null) return;
            if (__instance.m_ValidChoiceCards != null) foreach (var card in __instance.m_ValidChoiceCards) ApplyPremium(card);
            if (__instance.m_SelectedChoiceCards != null) foreach (var card in __instance.m_SelectedChoiceCards) ApplyPremium(card);
        }
    }

    [HarmonyPatch(typeof(Card), "Transform")]
    public static class Card_Transform_Patch
    {
        static void Postfix(Card __instance)
        {
            if (isPremiumifyEnabledPref.Value && _isGameplaySceneCurrentlyActive && __instance != null) ApplyPremium(__instance);
        }
    }

    [HarmonyPatch(typeof(CardBattleViewAnimation), "SetFaceUpInstant")]
    public static class CardBattleViewAnimation_SetFaceUpInstant_Patch
    {
        static void Postfix(CardBattleViewAnimation __instance)
        {
            if (isPremiumifyEnabledPref.Value && _isGameplaySceneCurrentlyActive &&
                __instance?.BattleView?.Card != null) ApplyPremium(__instance.BattleView.Card);
        }
    }

    [HarmonyPatch(typeof(CardBattleViewAnimation), "SetFaceUp", [typeof(bool), typeof(bool)])]
    public static class CardBattleViewAnimation_SetFaceUp_Bool_Bool_Patch
    {
        static void Postfix(CardBattleViewAnimation __instance)
        {
            if (isPremiumifyEnabledPref.Value && _isGameplaySceneCurrentlyActive &&
                __instance?.BattleView?.Card != null) ApplyPremium(__instance.BattleView.Card);
        }
    }

    [HarmonyPatch(typeof(CardBattleViewAnimation), "SetFaceUp", [typeof(bool), typeof(bool), typeof(bool)])]
    public static class CardBattleViewAnimation_SetFaceUp_Bool_Bool_Bool_Patch
    {
        static void Postfix(CardBattleViewAnimation __instance)
        {
            if (isPremiumifyEnabledPref.Value && _isGameplaySceneCurrentlyActive &&
                __instance?.BattleView?.Card != null) ApplyPremium(__instance.BattleView.Card);
        }
    }
}
