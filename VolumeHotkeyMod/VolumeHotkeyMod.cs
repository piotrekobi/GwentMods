using HarmonyLib;
using Il2CppGwentGameplay;
using Il2CppGwentUnity;
using MelonLoader;
using ModSettings;
using ModSettings.TranslationProviders;
using UnityEngine;

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

    public override void OnUpdate()
    {
        if (modEnabledPreference.Value != true.ToString())
            return;

        for (int i = 0; i < NumberKeys.Length; i++)
        {
            if (Input.GetKeyDown(NumberKeys[i]))
            {
                SetVolume(i * 0.1f);
                break;
            }
        }
    }

    private static void SetVolume(float volume)
    {
        var broadcaster = EventBroadcaster.Instance;
        if (broadcaster == null)
        {
            MelonLogger.Warning("[VolumeHotkeyMod] EventBroadcaster instance not available.");
            return;
        }

        string volumeStr = volume.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);

        broadcaster.SettingsChanged.Invoke(SettingsKey.MUSIC, volumeStr);
        broadcaster.SettingsChanged.Invoke(SettingsKey.SFX, volumeStr);
        broadcaster.SettingsChanged.Invoke(SettingsKey.VOICE, volumeStr);

        MelonLogger.Msg($"[VolumeHotkeyMod] Volume set to {volume * 100:0}%");
    }

    private static readonly KeyCode[] NumberKeys = new KeyCode[]
    {
            KeyCode.Alpha0,
            KeyCode.Alpha1,
            KeyCode.Alpha2,
            KeyCode.Alpha3,
            KeyCode.Alpha4,
            KeyCode.Alpha5,
            KeyCode.Alpha6,
            KeyCode.Alpha7,
            KeyCode.Alpha8,
            KeyCode.Alpha9,
    };
}
