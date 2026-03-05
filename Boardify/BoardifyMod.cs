using HarmonyLib;
using Il2CppGwentGameplay;
using Il2CppGwentUnity;
using MelonLoader;
using ModSettings;
using ModSettings.TranslationProviders;

[assembly: MelonInfo(typeof(Boardify.BoardifyMod), "Boardify", "1.0.0", "Jester")]
[assembly: MelonGame("CDProjektRED", "Gwent")]
namespace Boardify;

public class BoardifyMod : MelonMod
{
    private const string ModId = "Boardify";
    internal static MelonPreferences_Entry<string> boardPreference = null!;
    internal static MelonPreferences_Entry<bool> isModEnabledPreference = null!;

    public override void OnInitializeMelon()
    {
        isModEnabledPreference = MelonPreferences.CreateCategory(ModId).CreateEntry("BoardifyEnabled", true);
        boardPreference = MelonPreferences.CreateCategory(ModId).CreateEntry("CurrentBoard", BoardId.DandelionMeadow.ToString());
        var translationProvider = new EmbeddedFileTranslationProvider(MelonAssembly.Assembly, "Boardify.BoardTranslations.json");
        RegisterEnableSwitch(translationProvider);
        RegisterAllBoards(translationProvider);
        HarmonyInstance.PatchAll();
    }

    private static void RegisterEnableSwitch(TranslationProvider translationProvider)
    {
        string? pendingEnable = null;
        ModSettingsMod.RegisterSwitcherSetting(
            modId: ModId,
            settingTranslationKey: ModSettingsMod.RegisterTranslationKey(ModId, "Boardify_Label", new Dictionary<string, string> {
                { "en-us", "Boardify" }, { "pl-pl", "Boardify" }, { "de-de", "Boardify" }, { "ru-ru", "Бордификация" }, { "fr-fr", "Boardify" }, { "it-it", "Boardify" },
                { "es-es", "Boardify" }, { "es-mx", "Boardify" }, { "pt-br", "Boardify" }, { "zh-cn", "棋盘化" }, { "ja-jp", "ボード化" }, { "ko-kr", "보드화" } }),
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

    private static void RegisterAllBoards(TranslationProvider translationProvider)
    {
        var switcherOptions = new List<string>();
        foreach (BoardId board in Enum.GetValues(typeof(BoardId)))
        {
            switcherOptions.Add(ModSettingsMod.RegisterTranslationKey(ModId, board.ToString(), translationProvider.GetTranslationsFor(board.ToString())));
        }
        string? pendingBoard = null;
        ModSettingsMod.RegisterSwitcherSetting(
            modId: ModId,
            settingTranslationKey: ModSettingsMod.RegisterTranslationKey(ModId, "Current_Board_Translation", translationProvider.GetTranslationsFor("Current_Board_Translation")),
            switcherOptions: switcherOptions,
            getCurrentValue: () => boardPreference.Value, // what's currently saved
            onValueChangedCallback: val => pendingBoard = val as string != boardPreference.Value ? val as string : null, // user changed the switcher in UI
            hasPendingChangesCallback: () => pendingBoard != null, // are there unsaved changes?
            applyPendingChangesCallback: () => { if (pendingBoard != null) { boardPreference.Value = pendingBoard; pendingBoard = null; } }, // user clicked Save
            revertPendingChangesCallback: () => pendingBoard = null); // user clicked Back/Cancel
    }
}

[HarmonyPatch(typeof(BoardLoader), "LoadBoard")]
public static class Patch_LoadBoard
{
    static void Prefix(BoardLoader __instance)
    {
        try
        {
            BoardArtDefinition definition = __instance.m_BoardArtDefinition;
            if (definition == null)
                return;

            MelonLogger.Msg("Current board preference: " + BoardifyMod.boardPreference.Value);
            int desiredBoard = (int)Enum.Parse(typeof(BoardId), BoardifyMod.boardPreference.Value);
            if (definition.ArtId != desiredBoard)
            {
                if (BoardifyMod.isModEnabledPreference.Value == true)
                {
                    definition.ArtId = desiredBoard;
                    MelonLogger.Msg($"Forced Board ArtId to {desiredBoard}");
                }
                else
                {
                    MelonLogger.Msg($"But mod is disabled");
                }
            }
            else
            {
                MelonLogger.Msg($"Board ArtId already was {desiredBoard}");
            }
        }
        catch (Exception ex)
        {
            MelonLogger.Error(ex.ToString());
        }
    }
}

internal enum BoardId
{
    DefaultMonsters = 3578,
    DefaultNilfgaard = 3579,
    DefaultNorthernRealms = 3580,
    DefaultScoiatael = 3581,
    DefaultSkellige = 3582,
    DefaultSyndicate = 3583,
    Lyria = 3584,
    Aedirn = 3585,
    Mahakam = 3586,
    YarugaBridge = 3587,
    ChineseNewYear = 3588,
    Tavern = 3589,
    NovigradNight = 3590,
    ShipBoard = 3591,
    WildHunt = 3592,
    ChineseChristmas = 3593,
    ChineseRatYear = 3594,
    Sewers = 3595,
    AedirnIsOnFire = 3596,
    Gaunter = 3597,
    Forgotten = 3598,
    DemavendShip = 3599,
    DandelionMeadow = 3600,
    Alzur = 3602,
    Winter = 3603,
    Spring = 3604,
    Torture = 3605,
    TheGreatOak = 3613,
    Office = 3606,
    Cave = 3607,
    Hall = 3608,
    HallNight = 3609,
    HallRuined = 3610,
    Classroom = 3611,
    UpsideDown = 3612,
    ClassroomNecromancy = 3626,
    Summer = 3644,
    Autumn = 3711,
    Crones = 3615,
    ChristmasTree = 3616,
    TigerBoard = 3963,
    NovigradLoveboard = 3962,
    YarugaMorning = 4004,
    AbandonedLaboratory = 4022,
    Eclipse = 4053,
    Yamurlak = 3601,
    TempleofLivinirecolor = 3976,
    LyriaNight = 4134,
    OfficeParty = 4192,
    AllgodsCave = 3614,
    Vault = 3665,
    Rivia = 3961,
    FullMoon = 5159,
}