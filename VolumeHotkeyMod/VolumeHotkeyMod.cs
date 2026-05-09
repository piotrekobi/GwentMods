using HarmonyLib;
using Il2CppGwentGameplay;
using Il2CppGwentUnity;
using Il2CppGwentUnity.Audio;
using Il2CppGwentVisuals.UX;
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
        affectedVolumesPreference = MelonPreferences.CreateCategory(ModId).CreateEntry("VolumeHotkeyMod_AffectedVolumes", "Music");
        RegisterModOptions();
    }

    #region Register mod options
    private void RegisterModOptions()
    {
        var translationProvider = new EmbeddedFileTranslationProvider(MelonAssembly.Assembly, "VolumeHotkeyMod.ModTranslations.json");
        RegisterEnableSwitch(translationProvider);
        RegisterAffectedVolumesSwitch(translationProvider);
    }

    private static void RegisterEnableSwitch(TranslationProvider translationProvider)
    {
        string? pendingMode = null;
        ModSettingsMod.RegisterSwitcherSetting(
            modId: ModId,
            settingTranslationKey: ModSettingsMod.RegisterTranslationKey(ModId, "Mod_Enabled_Translation", translationProvider.GetTranslationsFor("Mod_Enabled_Translation")),
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

    private static void RegisterAffectedVolumesSwitch(TranslationProvider translationProvider)
    {
        string? pendingMode = null;
        ModSettingsMod.RegisterSwitcherSetting(
            modId: ModId,
            settingTranslationKey: ModSettingsMod.RegisterTranslationKey(ModId, "AffectedVolumes_Translation", translationProvider.GetTranslationsFor("AffectedVolumes_Translation")),
            switcherOptions: new List<string> {
                ModSettingsMod.RegisterTranslationKey(ModId, "Music",             translationProvider.GetTranslationsFor("Music")),
                ModSettingsMod.RegisterTranslationKey(ModId, "SFX",               translationProvider.GetTranslationsFor("SFX")),
                ModSettingsMod.RegisterTranslationKey(ModId, "Speech",            translationProvider.GetTranslationsFor("Speech")),
                ModSettingsMod.RegisterTranslationKey(ModId, "Music+SFX",         translationProvider.GetTranslationsFor("Music+SFX")),
                ModSettingsMod.RegisterTranslationKey(ModId, "Music+Speech",      translationProvider.GetTranslationsFor("Music+Speech")),
                ModSettingsMod.RegisterTranslationKey(ModId, "SFX+Speech",        translationProvider.GetTranslationsFor("SFX+Speech")),
                ModSettingsMod.RegisterTranslationKey(ModId, "Music+SFX+Speech",  translationProvider.GetTranslationsFor("Music+SFX+Speech")),
            },
            getCurrentValue: () => affectedVolumesPreference.Value,
            onValueChangedCallback: val => pendingMode = val as string != affectedVolumesPreference.Value ? val as string : null,
            hasPendingChangesCallback: () => pendingMode != null,
            applyPendingChangesCallback: () => { if (pendingMode != null) { affectedVolumesPreference.Value = pendingMode; pendingMode = null; } },
            revertPendingChangesCallback: () => pendingMode = null);
    }
    #endregion

    public override void OnUpdate()
    {
        if (modEnabledPreference.Value != true.ToString())
            return;

        for (int i = 0; i < NumberKeys.Length; i++)
        {
            if (Input.GetKeyDown(NumberKeys[i]))
            {
                SetVolume(i * 0.1f);
                return;
            }
        }

        HandleMuting();
    }

    private static void HandleMuting()
    {
        var broadcaster = EventBroadcaster.Instance;
        if (broadcaster == null)
        {
            MelonLogger.Warning("[VolumeHotkeyMod] EventBroadcaster instance not available.");
            return;
        }
        if (Input.GetKeyDown(KeyCode.KeypadPlus) || Input.GetKeyDown(KeyCode.Plus))
        {
            broadcaster.SettingsChanged.Invoke(SettingsKey.MUTE, false.ToString());
            MelonLogger.Msg("[VolumeHotkeyMod] Unmuted");
        }
        else if (Input.GetKeyDown(KeyCode.KeypadMinus) || Input.GetKeyDown(KeyCode.Minus))
        {
            broadcaster.SettingsChanged.Invoke(SettingsKey.MUTE, true.ToString());
            MelonLogger.Msg("[VolumeHotkeyMod] Muted");
        }
    }

    private static void SetVolume(float volume)
    {
        var manager = SoundManager.Instance;
        if (manager?.SettingsHandler == null)
        {
            MelonLogger.Warning("[VolumeHotkeyMod] SoundManager not available.");
            return;
        }

        string mode = affectedVolumesPreference.Value;

        if (mode == "Music" || mode == "Music+SFX" || mode == "Music+Speech" || mode == "Music+SFX+Speech")
            manager.SettingsHandler.UpdateFloatSettingValue(SoundSettingType.MusicVolume, volume);
        if (mode == "SFX" || mode == "Music+SFX" || mode == "SFX+Speech" || mode == "Music+SFX+Speech")
            manager.SettingsHandler.UpdateFloatSettingValue(SoundSettingType.SfxVolume, volume);
        if (mode == "Speech" || mode == "Music+Speech" || mode == "SFX+Speech" || mode == "Music+SFX+Speech")
            manager.SettingsHandler.UpdateFloatSettingValue(SoundSettingType.VoicesVolume, volume);

        MelonLogger.Msg($"[VolumeHotkeyMod] Volume set to {volume * 100:0}% (mode: {mode})");
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
