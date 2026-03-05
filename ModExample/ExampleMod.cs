using MelonLoader;
using ModSettings;
using ModSettings.TranslationProviders;


[assembly: MelonInfo(typeof(ExampleMod.ExampleMod), "ExampleMod", "1.0.0", "Author")]
[assembly: MelonGame("CDProjektRED", "Gwent")]
namespace ExampleMod;

public class ExampleMod : MelonMod
{
    private const string ModId = "ExampleMod";
    internal static MelonPreferences_Entry<bool> isModEnabledPreference = null!;

    public override void OnInitializeMelon()
    {
        isModEnabledPreference = MelonPreferences.CreateCategory(ModId).CreateEntry("Enabled", true);
        var translationProvider = new EmbeddedFileTranslationProvider(MelonAssembly.Assembly, "Translations.json");
        RegisterEnableSwitch(translationProvider);
        HarmonyInstance.PatchAll();
    }

    private static void RegisterEnableSwitch(TranslationProvider translationProvider)
    {
        string? pendingEnable = null;
        ModSettingsMod.RegisterSwitcherSetting(
            modId: ModId,
            settingTranslationKey: ModSettingsMod.RegisterTranslationKey(ModId, "Mod_Enabled_Translation", translationProvider.GetTranslationsFor("Mod_Enabled_Translation")),
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

// HARMONY PATCHES HERE