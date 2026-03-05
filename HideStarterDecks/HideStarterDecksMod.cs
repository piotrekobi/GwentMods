using HarmonyLib;
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
        if (!HideStarterDecksMod.isModEnabledPreference.Value || __result == null)
            return;

        var filtered = new Il2CppSystem.Collections.Generic.List<Il2CppGwentVisuals.CollectionDeck>();
        for (int i = 0; i < __result.Count; i++)
        {
            var deck = __result[i];
            if (!deck.IsStarterDeck)
                filtered.Add(deck);
        }

        __result = filtered;
    }
}       