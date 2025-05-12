using MelonLoader;
using System.Diagnostics;

[assembly: MelonInfo(typeof(ModSettingsTest.ModSettingsTestCore), "ModSettingsTest", "1.0.0", "piotrekobi")]
[assembly: MelonGame("CDProjektRED", "Gwent")]

namespace ModSettingsTest
{
    public class ModSettingsTestCore : MelonMod
    {
        private static MelonLogger.Instance staticLogger;

        internal const string ModId = "ModSettingsTest";

        internal static MelonPreferences_Category settingsTestCategory;
        internal static MelonPreferences_Entry<string> switcher1Pref;
        internal static MelonPreferences_Entry<string> switcher2Pref;
        internal static MelonPreferences_Entry<string> switcher3Pref;
        internal static List<MelonPreferences_Entry<string>> additionalSwitcherPrefs = [];

        private static string _pendingSwitcher1Value;
        private static string _pendingSwitcher2Value;
        private static string _pendingSwitcher3Value;
        private static readonly List<string> _pendingAdditionalSwitcherValues = [];

        internal const string Switcher1SettingKey = "TestSwitcher1";
        internal const string Switcher2SettingKey = "TestSwitcher2";
        internal const string Switcher3SettingKey = "TestSwitcher3";
        internal static List<string> additionalSwitcherSettingKeys = [];

        internal const string Switcher1LocKey = "panel_settings_entry_mods_modsettingstest_switcher1";
        internal const string Switcher2LocKey = "panel_settings_entry_mods_modsettingstest_switcher2";
        internal const string Switcher3LocKey = "panel_settings_entry_mods_modsettingstest_switcher3";
        internal static List<string> additionalSwitcherLocKeys = [];

        internal const string S1Opt1LocKey = "panel_settings_entry_mods_modsettingstest_s1_opt1";
        internal const string S1Opt2LocKey = "panel_settings_entry_mods_modsettingstest_s1_opt2";
        internal const string S1Opt3LocKey = "panel_settings_entry_mods_modsettingstest_s1_opt3";

        internal const string S2Opt1LocKey = "panel_settings_entry_mods_modsettingstest_s2_opt1";
        internal const string S2Opt2LocKey = "panel_settings_entry_mods_modsettingstest_s2_opt2";
        internal const string S2Opt3LocKey = "panel_settings_entry_mods_modsettingstest_s2_opt3";
        internal const string S2Opt4LocKey = "panel_settings_entry_mods_modsettingstest_s2_opt4";

        internal const string S3Opt1LocKey = "panel_settings_entry_mods_modsettingstest_s3_opt1";
        internal const string S3Opt2LocKey = "panel_settings_entry_mods_modsettingstest_s3_opt2";
        internal const string S3Opt3LocKey = "panel_settings_entry_mods_modsettingstest_s3_opt3";
        internal const string S3Opt4LocKey = "panel_settings_entry_mods_modsettingstest_s3_opt4";
        internal const string S3Opt5LocKey = "panel_settings_entry_mods_modsettingstest_s3_opt5";

        internal const string AdditionalOptALocKey = "panel_settings_entry_mods_modsettingstest_add_opt_a";
        internal const string AdditionalOptBLocKey = "panel_settings_entry_mods_modsettingstest_add_opt_b";


        [Conditional("DEBUG")] internal static void Log(string m) => staticLogger?.Msg($"[{ModId}] {m}");
        [Conditional("DEBUG")] private static void LogError(string m, Exception e = null) => staticLogger?.Error($"[{ModId}] {m}" + (e == null ? "" : $"\n{e}"));

        public override void OnInitializeMelon()
        {
            staticLogger = LoggerInstance;
            Log("Init");

            try
            {
                settingsTestCategory = MelonPreferences.CreateCategory(ModId);
                switcher1Pref = settingsTestCategory.CreateEntry(Switcher1SettingKey, "s1_opt1");
                switcher2Pref = settingsTestCategory.CreateEntry(Switcher2SettingKey, "s2_opt1");
                switcher3Pref = settingsTestCategory.CreateEntry(Switcher3SettingKey, "s3_opt1");

                for (int i = 0; i < 10; i++)
                {
                    string settingKey = $"AdditionalSwitcher{i + 1}";
                    additionalSwitcherSettingKeys.Add(settingKey);
                    additionalSwitcherLocKeys.Add($"panel_settings_entry_mods_modsettingstest_add_switcher{i + 1}");
                    additionalSwitcherPrefs.Add(settingsTestCategory.CreateEntry(settingKey, $"add_s{i + 1}_opt_a"));
                    _pendingAdditionalSwitcherValues.Add(null);
                }

                Log("Prefs Loaded");
            }
            catch (Exception e) { LogError("Prefs Init Error", e); }

            RegisterTranslations();

            var switcher1Options = new List<Tuple<string, Func<string>>>
            {
                Tuple.Create("s1_opt1", (Func<string>)(() => S1Opt1LocKey)),
                Tuple.Create("s1_opt2", (Func<string>)(() => S1Opt2LocKey)),
                Tuple.Create("s1_opt3", (Func<string>)(() => S1Opt3LocKey))
            };
            ModSettings.ModSettings.RegisterSwitcherSetting(ModId, Switcher1SettingKey, Switcher1LocKey, switcher1Options,
                GetCurrentSwitcher1Value, OnSwitcher1ChangedInUI, HasPendingSwitcher1Changes, ApplyPendingSwitcher1Changes, RevertPendingSwitcher1Changes);

            var switcher2Options = new List<System.Tuple<string, Func<string>>>
            {
                Tuple.Create("s2_opt1", (Func<string>)(() => S2Opt1LocKey)),
                Tuple.Create("s2_opt2", (Func<string>)(() => S2Opt2LocKey)),
                Tuple.Create("s2_opt3", (Func<string>)(() => S2Opt3LocKey)),
                Tuple.Create("s2_opt4", (Func<string>)(() => S2Opt4LocKey))
            };
            ModSettings.ModSettings.RegisterSwitcherSetting(ModId, Switcher2SettingKey, Switcher2LocKey, switcher2Options,
                GetCurrentSwitcher2Value, OnSwitcher2ChangedInUI, HasPendingSwitcher2Changes, ApplyPendingSwitcher2Changes, RevertPendingSwitcher2Changes);

            var switcher3Options = new List<System.Tuple<string, Func<string>>>
            {
                Tuple.Create("s3_opt1", (Func<string>)(() => S3Opt1LocKey)),
                Tuple.Create("s3_opt2", (Func<string>)(() => S3Opt2LocKey)),
                Tuple.Create("s3_opt3", (Func<string>)(() => S3Opt3LocKey)),
                Tuple.Create("s3_opt4", (Func<string>)(() => S3Opt4LocKey)),
                Tuple.Create("s3_opt5", (Func<string>)(() => S3Opt5LocKey))
            };
            ModSettings.ModSettings.RegisterSwitcherSetting(ModId, Switcher3SettingKey, Switcher3LocKey, switcher3Options,
                GetCurrentSwitcher3Value, OnSwitcher3ChangedInUI, HasPendingSwitcher3Changes, ApplyPendingSwitcher3Changes, RevertPendingSwitcher3Changes);

            for (int i = 0; i < 10; i++)
            {
                int local_i = i;
                var additionalSwitcherOptions = new List<Tuple<string, Func<string>>>
                {
                    Tuple.Create($"add_s{local_i + 1}_opt_a", (Func<string>)(() => AdditionalOptALocKey)),
                    Tuple.Create($"add_s{local_i + 1}_opt_b", (Func<string>)(() => AdditionalOptBLocKey))
                };

                ModSettings.ModSettings.RegisterSwitcherSetting(ModId,
                    additionalSwitcherSettingKeys[local_i],
                    additionalSwitcherLocKeys[local_i],
                    additionalSwitcherOptions,
                    () => GetCurrentAdditionalSwitcherValue(local_i),
                    (val) => OnAdditionalSwitcherChangedInUI(local_i, val),
                    () => HasPendingAdditionalSwitcherChanges(local_i),
                    () => ApplyPendingAdditionalSwitcherChanges(local_i),
                    () => RevertPendingAdditionalSwitcherChanges(local_i)
                );
            }

            Log("ModSettings Registered for all switchers");
            Log("Init Complete");
        }

        private static void RegisterTranslations()
        {
            var requiredLangs = new List<string>
                { "en-us", "pl-pl", "de-de", "ru-ru", "fr-fr", "it-it", "es-es", "es-mx", "pt-br", "zh-cn", "ja-jp", "ko-kr" };

            Dictionary<string, string> CreateDummyTranslations(string baseText)
            {
                var dict = new Dictionary<string, string>();
                foreach (var lang in requiredLangs) dict[lang] = $"{baseText} ({lang.ToUpper()})";
                return dict;
            }

            ModSettings.ModSettings.RegisterTranslationKey(ModId, Switcher1LocKey, CreateDummyTranslations("Test Switcher 1"));
            ModSettings.ModSettings.RegisterTranslationKey(ModId, Switcher2LocKey, CreateDummyTranslations("Test Switcher 2"));
            ModSettings.ModSettings.RegisterTranslationKey(ModId, Switcher3LocKey, CreateDummyTranslations("Test Switcher 3"));

            ModSettings.ModSettings.RegisterTranslationKey(ModId, S1Opt1LocKey, CreateDummyTranslations("S1 Option One"));
            ModSettings.ModSettings.RegisterTranslationKey(ModId, S1Opt2LocKey, CreateDummyTranslations("S1 Option Two"));
            ModSettings.ModSettings.RegisterTranslationKey(ModId, S1Opt3LocKey, CreateDummyTranslations("S1 Option Three"));

            ModSettings.ModSettings.RegisterTranslationKey(ModId, S2Opt1LocKey, CreateDummyTranslations("S2 Option A"));
            ModSettings.ModSettings.RegisterTranslationKey(ModId, S2Opt2LocKey, CreateDummyTranslations("S2 Option B"));
            ModSettings.ModSettings.RegisterTranslationKey(ModId, S2Opt3LocKey, CreateDummyTranslations("S2 Option C"));
            ModSettings.ModSettings.RegisterTranslationKey(ModId, S2Opt4LocKey, CreateDummyTranslations("S2 Option D"));

            ModSettings.ModSettings.RegisterTranslationKey(ModId, S3Opt1LocKey, CreateDummyTranslations("S3 Choice Alpha"));
            ModSettings.ModSettings.RegisterTranslationKey(ModId, S3Opt2LocKey, CreateDummyTranslations("S3 Choice Beta"));
            ModSettings.ModSettings.RegisterTranslationKey(ModId, S3Opt3LocKey, CreateDummyTranslations("S3 Choice Gamma"));
            ModSettings.ModSettings.RegisterTranslationKey(ModId, S3Opt4LocKey, CreateDummyTranslations("S3 Choice Delta"));
            ModSettings.ModSettings.RegisterTranslationKey(ModId, S3Opt5LocKey, CreateDummyTranslations("S3 Choice Epsilon"));

            ModSettings.ModSettings.RegisterTranslationKey(ModId, AdditionalOptALocKey, CreateDummyTranslations("Option A / On"));
            ModSettings.ModSettings.RegisterTranslationKey(ModId, AdditionalOptBLocKey, CreateDummyTranslations("Option B / Off"));

            for (int i = 0; i < 10; i++)
            {
                ModSettings.ModSettings.RegisterTranslationKey(ModId, additionalSwitcherLocKeys[i], CreateDummyTranslations($"Scroll Test Switcher {i + 1}"));
            }

            Log("Translations Registered");
        }

        private static object GetCurrentSwitcher1Value() => switcher1Pref.Value;
        private static void OnSwitcher1ChangedInUI(object newValIdObj) { if (newValIdObj is not string newValId) return; if (newValId != switcher1Pref.Value) _pendingSwitcher1Value = newValId; else _pendingSwitcher1Value = null; }
        private static bool HasPendingSwitcher1Changes() => _pendingSwitcher1Value != null;
        private static void ApplyPendingSwitcher1Changes() { if (_pendingSwitcher1Value == null) return; if (_pendingSwitcher1Value != switcher1Pref.Value) switcher1Pref.Value = _pendingSwitcher1Value; _pendingSwitcher1Value = null; Log("Applied Switcher 1 changes."); }
        private static void RevertPendingSwitcher1Changes() { _pendingSwitcher1Value = null; Log("Reverted Switcher 1 changes."); }

        private static object GetCurrentSwitcher2Value() => switcher2Pref.Value;
        private static void OnSwitcher2ChangedInUI(object newValIdObj) { if (newValIdObj is not string newValId) return; if (newValId != switcher2Pref.Value) _pendingSwitcher2Value = newValId; else _pendingSwitcher2Value = null; }
        private static bool HasPendingSwitcher2Changes() => _pendingSwitcher2Value != null;
        private static void ApplyPendingSwitcher2Changes() { if (_pendingSwitcher2Value == null) return; if (_pendingSwitcher2Value != switcher2Pref.Value) switcher2Pref.Value = _pendingSwitcher2Value; _pendingSwitcher2Value = null; Log("Applied Switcher 2 changes."); }
        private static void RevertPendingSwitcher2Changes() { _pendingSwitcher2Value = null; Log("Reverted Switcher 2 changes."); }

        private static object GetCurrentSwitcher3Value() => switcher3Pref.Value;
        private static void OnSwitcher3ChangedInUI(object newValIdObj) { if (newValIdObj is not string newValId) return; if (newValId != switcher3Pref.Value) _pendingSwitcher3Value = newValId; else _pendingSwitcher3Value = null; }
        private static bool HasPendingSwitcher3Changes() => _pendingSwitcher3Value != null;
        private static void ApplyPendingSwitcher3Changes() { if (_pendingSwitcher3Value == null) return; if (_pendingSwitcher3Value != switcher3Pref.Value) switcher3Pref.Value = _pendingSwitcher3Value; _pendingSwitcher3Value = null; Log("Applied Switcher 3 changes."); }
        private static void RevertPendingSwitcher3Changes() { _pendingSwitcher3Value = null; Log("Reverted Switcher 3 changes."); }

        private static object GetCurrentAdditionalSwitcherValue(int index) => additionalSwitcherPrefs[index].Value;
        private static void OnAdditionalSwitcherChangedInUI(int index, object newValIdObj)
        {
            if (newValIdObj is not string newValId) return;
            if (newValId != additionalSwitcherPrefs[index].Value) _pendingAdditionalSwitcherValues[index] = newValId;
            else _pendingAdditionalSwitcherValues[index] = null;
        }
        private static bool HasPendingAdditionalSwitcherChanges(int index) => _pendingAdditionalSwitcherValues[index] != null;
        private static void ApplyPendingAdditionalSwitcherChanges(int index)
        {
            if (_pendingAdditionalSwitcherValues[index] == null) return;
            if (_pendingAdditionalSwitcherValues[index] != additionalSwitcherPrefs[index].Value)
            {
                additionalSwitcherPrefs[index].Value = _pendingAdditionalSwitcherValues[index];
            }
            _pendingAdditionalSwitcherValues[index] = null;
            Log($"Applied Additional Switcher {index + 1} changes.");
        }
        private static void RevertPendingAdditionalSwitcherChanges(int index)
        {
            _pendingAdditionalSwitcherValues[index] = null;
            Log($"Reverted Additional Switcher {index + 1} changes.");
        }
    }
}