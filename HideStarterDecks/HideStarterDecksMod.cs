using HarmonyLib;
using Il2CppGwentVisuals;
using Il2CppGwentVisuals.UX;
using MelonLoader;
using ModSettings;
using ModSettings.TranslationProviders;

[assembly: MelonInfo(typeof(HideStarterDecks.HideStarterDecksMod), "HideStarterDecks", "1.0.0", "Author")]
[assembly: MelonGame("CDProjektRED", "Gwent")]
namespace HideStarterDecks;

public class HideStarterDecksMod : MelonMod
{
    private const string ModId = "HideStarterDecksMod";
    internal static MelonPreferences_Entry<bool> isModEnabledPreference = null!;
    internal static MelonPreferences_Entry<bool> guideRedirectEnabledPreference = null!;
    internal static MelonPreferences_Entry<string> guideRedirectURLPreference = null!;

    public override void OnInitializeMelon()
    {
        isModEnabledPreference = MelonPreferences.CreateCategory(ModId).CreateEntry("Enabled", true);
        guideRedirectEnabledPreference = MelonPreferences.CreateCategory(ModId).CreateEntry("DeckGuideRedirect", true);
        guideRedirectURLPreference = MelonPreferences.CreateCategory(ModId).CreateEntry("RedirectURL", "https://www.playgwent.com/en/decks");
        var translationProvider = new EmbeddedFileTranslationProvider(MelonAssembly.Assembly, "HideStarterDecks.Translations.json");
        RegisterHideStarterSettingsSwitch(translationProvider);
        RegisterDeckGuideRedirectSwitch(translationProvider);
        HarmonyInstance.PatchAll();
    }

    private static void RegisterHideStarterSettingsSwitch(TranslationProvider translationProvider)
    {
        string? pendingEnable = null;
        ModSettingsMod.RegisterSwitcherSetting(
            modId: ModId,
            settingTranslationKey: ModSettingsMod.RegisterTranslationKey(ModId, "HideStarterDecks_Enabled_Translation", translationProvider.GetTranslationsFor("HideStarterDecks_Enabled_Translation")),
            switcherOptions: new List<string> {
                ModSettingsMod.RegisterTranslationKey(ModId, true.ToString(), translationProvider.GetTranslationsFor(true.ToString())),
                ModSettingsMod.RegisterTranslationKey(ModId, false.ToString(), translationProvider.GetTranslationsFor(false.ToString()))
            },
            getCurrentValue: () => isModEnabledPreference.Value.ToString(), // currently saved value
            onValueChangedCallback: val => pendingEnable = val as string != isModEnabledPreference.Value.ToString() ? val as string : null, // user changed the switcher in UI
            hasPendingChangesCallback: () => pendingEnable != null, // are there unsaved changes?
            applyPendingChangesCallback: () => { if (pendingEnable != null) { isModEnabledPreference.Value = bool.Parse(pendingEnable); pendingEnable = null; } }, // user clicked Save
            revertPendingChangesCallback: () => pendingEnable = null); // user clicked Back/Cancel
    }

    private static void RegisterDeckGuideRedirectSwitch(TranslationProvider translationProvider)
    {
        string? pendingValue = null;
        ModSettingsMod.RegisterSwitcherSetting(
            modId: ModId,
            settingTranslationKey: ModSettingsMod.RegisterTranslationKey(ModId, "DeckGuideRedirect_Enabled_Translation", translationProvider.GetTranslationsFor("DeckGuideRedirect_Enabled_Translation")),
            switcherOptions: new List<string> {
                ModSettingsMod.RegisterTranslationKey(ModId, "Redirect", translationProvider.GetTranslationsFor("DeckGuide_Redirect_Translation")),
                ModSettingsMod.RegisterTranslationKey(ModId, "Hidden", translationProvider.GetTranslationsFor("DeckGuide_Hidden_Translation"))
            },
            getCurrentValue: () => guideRedirectEnabledPreference.Value ? "Redirect" : "Hidden",
            onValueChangedCallback: val => pendingValue = val as string != (guideRedirectEnabledPreference.Value ? "Redirect" : "Hidden") ? val as string : null,
            hasPendingChangesCallback: () => pendingValue != null,
            applyPendingChangesCallback: () => { if (pendingValue != null) { guideRedirectEnabledPreference.Value = pendingValue == "Redirect"; pendingValue = null; } },
            revertPendingChangesCallback: () => pendingValue = null
        );
    }
}

[HarmonyPatch(typeof(Il2CppGwentVisuals.CollectionData), "GetDeckList")]
public static class Patch_HideStarterDecks
{
    [HarmonyPostfix]
    public static void Postfix_GetDeckList(ref Il2CppSystem.Collections.Generic.List<Il2CppGwentVisuals.CollectionDeck> __result)
    {
        MelonLogger.Msg("Postfix_GetDeckList called");
        if (!HideStarterDecksMod.isModEnabledPreference.Value || __result == null)
            return;

        var filtered = new Il2CppSystem.Collections.Generic.List<Il2CppGwentVisuals.CollectionDeck>();
        for (int i = 0; i < __result.Count; i++)
        {
            var deck = __result[i];
            if (!deck.IsStarterDeck)
                filtered.Add(deck);
        }
        MelonLogger.Msg("Deck filtering complete");

        __result = filtered;
    }
}

// patch that redirects the Deck Guide button in the main menu to a custom URL
[HarmonyPatch(typeof(UIDeckSelectorListContainer), "HandleDeckGuideButtonClicked")]
public static class Patch_DeckGuideRedirect
{
    [HarmonyPrefix]
    public static bool Prefix_HandleDeckGuideButtonClicked()
    {
        if (HideStarterDecksMod.guideRedirectEnabledPreference.Value)
        {
            UnityEngine.Application.OpenURL(HideStarterDecksMod.guideRedirectURLPreference.Value); // Redirect
        }
        else
        {
            return true; // Let original method run (or could optionally hide it via UXManager patch)
        }
        return false; // Skip original method if redirecting
    }
}

// patch that hides the Deck Guide button in the main menu, because it leads god knows where
[HarmonyPatch(typeof(UXManager), "IsContentStateHidden")]
public static class Patch_BlockDeckGuide
{
    [HarmonyPostfix]
    public static void IsContentStateHidden_Postfix(int __0, ref bool __result)
    {
        // __0 means the first parameter
        if (__0 == (int)EUXContentId.MainMenu_DeckSelection_DeckGuideButton)
        {
            if (!HideStarterDecksMod.guideRedirectEnabledPreference.Value) // Only hide if redirect is disabled
                __result = true;
        }
    }
}