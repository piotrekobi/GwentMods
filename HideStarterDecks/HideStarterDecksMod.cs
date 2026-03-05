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

    public override void OnInitializeMelon()
    {
        isModEnabledPreference = MelonPreferences.CreateCategory(ModId).CreateEntry("Enabled", true);
        var translationProvider = new EmbeddedFileTranslationProvider(MelonAssembly.Assembly, "HideStarterDecks.Translations.json");
        RegisterEnableSwitch(translationProvider);
        HarmonyInstance.PatchAll();
    }

    private static void RegisterEnableSwitch(TranslationProvider translationProvider)
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
        UnityEngine.Application.OpenURL("https://your.custom.url"); // Open your own URL instead
        return false; // Skip original method
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
            __result = true; // Pretend it’s hidden
        }
    }
}