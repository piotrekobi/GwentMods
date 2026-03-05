#nullable enable

namespace ModSettings.TranslationProviders;

//string "en-us:Premiumify;pl-pl:Premiumify;de-de:Premiumify;ru-ru:Премиумификация;"
// becomes 
//Dictionary { "en-us": "Premiumify", "pl-pl": "Premiumify", "de-de": "Premiumify", "ru-ru": "Премиумификация" }
public sealed class ParseStringTranslationProvider : TranslationProvider
{
    private readonly string _text;

    public ParseStringTranslationProvider(string text)
    {
        _text = text ?? string.Empty;
    }

    protected override Dictionary<string, string>? TryGetTranslationsFor(string key)
    {
        if (string.IsNullOrWhiteSpace(_text))
            return null;

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var part in _text.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = part.Trim();

            int colonIndex = trimmed.IndexOf(':');
            if (colonIndex <= 0)
                continue;

            var lang = trimmed[..colonIndex].Trim();
            var value = trimmed[(colonIndex + 1)..].Trim();

            result[lang] = value;
        }

        return result;
    }
}
