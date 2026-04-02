using System.Text.RegularExpressions;

#nullable enable

namespace ModSettings.TranslationProviders;

public abstract class TranslationProvider
{
    protected abstract Dictionary<string, string>? TryGetTranslationsFor(string key);

    public Dictionary<string, string> GetTranslationsFor(string key)
    {
        var translations = TryGetTranslationsFor(key);
        var dummy = CreateDummyTranslations(key);

        // If nothing found, return a full dummy set
        if (translations == null || translations.Count == 0)
            return dummy;

        // Work on a copy so we don't mutate provider internals
        var result = new Dictionary<string, string>(translations);

        // Ensure all required languages are present; fill missing ones from dummy
        foreach (var lang in ModSettingsMod.RequiredLanguages)
        {
            if (!result.TryGetValue(lang, out var val) || string.IsNullOrWhiteSpace(val))
                result[lang] = dummy[lang];
        }

        return result;
    }

    // Shared dummy/fallback logic
    protected virtual Dictionary<string, string> CreateDummyTranslations(string baseText)
    {
        string readableText = Regex.Replace(baseText, "(?<!^)([A-Z])", " $1");

        var dict = new Dictionary<string, string>();
        foreach (var lang in ModSettingsMod.RequiredLanguages)
            dict[lang] = readableText;

        return dict;
    }
}
