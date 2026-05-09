using HarmonyLib;
using Il2CppGwentGameplay;
using Il2CppGwentUnity;
using MelonLoader;
using ModSettings;
using ModSettings.TranslationProviders;

[assembly: MelonInfo(typeof(VolumeHotkeyMod.VolumeHotkeyMod), "VolumeHotkeyMod", "1.0.0", "Jester")]
[assembly: MelonGame("CDProjektRED", "Gwent")]
namespace VolumeHotkeyMod;

public class VolumeHotkeyMod : MelonMod
{
    private const string ModId = "VolumeHotkeyMod";
    internal static MelonPreferences_Entry<string> modEnabledPreference = null!;
    internal static MelonPreferences_Entry<string> affectedVolumesPreference = null!;

    public override void OnInitializeMelon()
    {
        modEnabledPreference = MelonPreferences.CreateCategory(ModId).CreateEntry("VolumeHotkeyMod_Enabled", true.ToString());
        affectedVolumesPreference = MelonPreferences.CreateCategory(ModId).CreateEntry("AffectedVolumes", "Music");
        var translationProvider = new EmbeddedFileTranslationProvider(MelonAssembly.Assembly, "VolumeHotkeyMod.ModTranslations.json");
        RegisterEnableSwitch(translationProvider);
        HarmonyInstance.PatchAll();
    }

    private static void RegisterEnableSwitch(TranslationProvider translationProvider)
    {
        string? pendingMode = null;
        ModSettingsMod.RegisterSwitcherSetting(
            modId: ModId,
            settingTranslationKey: ModSettingsMod.RegisterTranslationKey(ModId, "VolumeHotkeyMod_Enabled_Translation", translationProvider.GetTranslationsFor("VolumeHotkeyMod_Enabled_Translation")),
            switcherOptions: new List<string> {
                ModSettingsMod.RegisterTranslationKey(ModId, true.ToString(), translationProvider.GetTranslationsFor(true.ToString())),
                ModSettingsMod.RegisterTranslationKey(ModId, false.ToString(), translationProvider.GetTranslationsFor(false.ToString())),
            },
            getCurrentValue: () => modEnabledPreference.Value, // currently saved value
            onValueChangedCallback: val => pendingMode = val as string != modEnabledPreference.Value ? val as string : null, // user changed the switcher in UI
            hasPendingChangesCallback: () => pendingMode != null, // are there unsaved changes?
            applyPendingChangesCallback: () => { if (pendingMode != null) { modEnabledPreference.Value = pendingMode; pendingMode = null; } }, // user clicked Save
            revertPendingChangesCallback: () => pendingMode = null); // user clicked Back/Cancel
    }
}
