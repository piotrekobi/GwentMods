using HarmonyLib;
using Il2CppGwentGameplay;
using Il2CppGwentUnity;
using MelonLoader;
using System.Text.RegularExpressions;

[assembly: MelonInfo(typeof(Boardify.Boardify), "Boardify", "1.0.0", "Jester")]
[assembly: MelonGame("CDProjektRED", "Gwent")]
namespace Boardify
{
    public class Boardify : MelonMod
    {
        internal static MelonPreferences_Entry<string> boardPreference = null!;
        private static string? pendingBoard;

        public override void OnInitializeMelon()
        {
            boardPreference = MelonPreferences.CreateCategory("Boardify").CreateEntry("Boardify", BoardId.Yamurlak.ToString());
            RegisterAllBoards();
            HarmonyInstance.PatchAll();
        }

        private static void RegisterAllBoards()
        {
            var options = new List<Tuple<string, Func<string>>>();

            foreach (BoardId board in Enum.GetValues(typeof(BoardId)))
            {
                string key = board.ToString();
                ModSettings.ModSettings.RegisterTranslationKey("Boardify", key, CreateDummyTranslations(key));
                options.Add(Tuple.Create(key, () => key));
            }

            // Register switcher once with all enum options
            ModSettings.ModSettings.RegisterSwitcherSetting(
                modId: "Boardify",
                settingKey: "Boardify",
                displayNameKey: "Boardify",
                switcherOptions: options,
                getCurrentValue: () => boardPreference.Value, // what's currently saved
                onValueChangedCallback: val => pendingBoard = val as string != boardPreference.Value ? val as string : null, // user changed the switcher in UI
                hasPendingChangesCallback: () => pendingBoard != null, // are there unsaved changes?
                applyPendingChangesCallback: () => { if (pendingBoard != null) { boardPreference.Value = pendingBoard; pendingBoard = null; } }, // user clicked Save
                revertPendingChangesCallback: () => pendingBoard = null // user clicked Back/Cancel
            );
        }

        private static Dictionary<string, string> CreateDummyTranslations(string baseText)
        {
            string readableText = Regex.Replace(baseText, "(?<!^)([A-Z])", " $1"); // Insert space before uppercase letters following lowercase letters
            var dict = new Dictionary<string, string>();
            foreach (var lang in new List<string> { "en-us", "pl-pl", "de-de", "ru-ru", "fr-fr", "it-it", "es-es", "es-mx", "pt-br", "zh-cn", "ja-jp", "ko-kr" }) 
                dict[lang] = $"{readableText}"/*+"({lang.ToUpper()})"*/;
            return dict;
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

                MelonLogger.Msg("Current board preference: " + Boardify.boardPreference.Value);
                int desiredBoard = (int)Enum.Parse(typeof(BoardId), Boardify.boardPreference.Value);
                if (definition.ArtId != desiredBoard)
                {
                    definition.ArtId = desiredBoard;
                    MelonLogger.Msg($"Forced Board ArtId to {desiredBoard}");
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
}