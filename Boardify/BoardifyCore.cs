using HarmonyLib;
using Il2CppGwentGameplay;
using Il2CppGwentUnity;
using MelonLoader;

[assembly: MelonInfo(typeof(Boardify.BoardifyCore), "Boardify", "1.0.0", "Jester")]
[assembly: MelonGame(null, null)]
namespace Boardify
{
    public class BoardifyCore : MelonMod
    {
        static MelonPreferences_Category _category;
        public static MelonPreferences_Entry<string> _boardPref;
        static string _pendingBoard;


        public override void OnInitializeMelon()
        {
            _category = MelonPreferences.CreateCategory("Boardify");
            _boardPref = _category.CreateEntry("Boardify", "Yamurlak");

            RegisterTranslations();
            RegisterSettings();

            HarmonyInstance.PatchAll();
        }
        Dictionary<string, string> T(string text) =>
        new[] { "en-us", "pl-pl", "de-de", "ru-ru", "fr-fr", "it-it",
                "es-es", "es-mx", "pt-br", "zh-cn", "ja-jp", "ko-kr" }
        .ToDictionary(l => l, l => text);

        void RegisterTranslations()
        {
            // Setting label
            ModSettings.ModSettings.RegisterTranslationKey("Boardify", "Boardify_difficulty", T("Board"));

            // Option labels
            ModSettings.ModSettings.RegisterTranslationKey("Boardify", BoardId.DefaultMonsters.ToString(), T(BoardId.DefaultMonsters.ToString()));
            ModSettings.ModSettings.RegisterTranslationKey("Boardify", BoardId.DefaultNilfgaard.ToString(), T(BoardId.DefaultNilfgaard.ToString()));
            ModSettings.ModSettings.RegisterTranslationKey("Boardify", BoardId.DefaultNorthernRealms.ToString(), T(BoardId.DefaultNorthernRealms.ToString()));
        }

        void RegisterSettings()
        {
            // Define options: List of (id, localization key getter)
            var options = new List<Tuple<string, Func<string>>>
            {
                Tuple.Create(BoardId.DefaultMonsters.ToString(), (Func<string>)(() => BoardId.DefaultMonsters.ToString())),
                Tuple.Create(BoardId.DefaultNilfgaard.ToString(), (Func<string>)(() => BoardId.DefaultNilfgaard.ToString())),
                Tuple.Create(BoardId.DefaultNorthernRealms.ToString(),   (Func<string>)(() => BoardId.DefaultNorthernRealms.ToString())),
            };

            ModSettings.ModSettings.RegisterSwitcherSetting(
                modId: "Boardify",
                settingKey: "Boardify",
                displayNameKey: "Board",       // localization key for setting label
                switcherOptions: options,

                getCurrentValue: () => _boardPref.Value,  // what's currently saved

                onValueChangedCallback: (val) =>            // user changed the switcher in UI
                {
                    string newVal = val as string;
                    // Only mark as pending if different from saved value
                    _pendingBoard = (newVal != _boardPref.Value) ? newVal : null;
                },

                hasPendingChangesCallback: () => _pendingBoard != null,

                applyPendingChangesCallback: () =>          // user clicked Save
                {
                    if (_pendingBoard != null)
                    {
                        _boardPref.Value = _pendingBoard;
                        _pendingBoard = null;
                        // MelonPreferences.Save() is called automatically by ModSettings
                    }
                },

                revertPendingChangesCallback: () =>          // user clicked Back/Cancel
                {
                    _pendingBoard = null;
                }
            );
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

                MelonLogger.Error(BoardifyCore._boardPref.Value);
                int desired = (int) Enum.Parse(typeof(BoardId), BoardifyCore._boardPref.Value);
                if (definition.ArtId != desired)
                {
                    definition.ArtId = desired;
                    MelonLogger.Msg($"Forced Board ArtId to {desired}");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error(ex.ToString());
            }
        }
    }

    // gonna leave this here for now if cause it might be needed later (this patched later than the LoadBoard patch, possibly leading to a small stutter)
    //[HarmonyPatch(typeof(BoardLoader), "OnBoardVisualsReady")] public static class Patch_OnBoardVisualsReady { static void Prefix(BoardLoader __instance) { try { var definition = __instance.m_BoardArtDefinition; if (definition == null) return; int desired = (int)ArtId.YarugaBridge; definition.ArtId = desired; MelonLogger.Msg($"Forced Board ArtId to {(int)desired}"); } catch (Exception ex) { MelonLogger.Error(ex.ToString()); } } }

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
}