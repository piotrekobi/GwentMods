#nullable enable

namespace ModSettings.TranslationProviders;

// Always return null so BaseTranslationProvider creates dummy translations
public sealed class NoTranslationProvider : TranslationProvider
{
    protected override Dictionary<string, string>? TryGetTranslationsFor(string key)
    {
        return null;
    }
}