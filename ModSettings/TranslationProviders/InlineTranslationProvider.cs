using System.Text.Json;

#nullable enable

namespace ModSettings.TranslationProviders;

public sealed class InlineTranslationProvider : TranslationProviderBase
{
    private readonly Dictionary<string, Dictionary<string, string>> _translations;

    public InlineTranslationProvider(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            _translations = new Dictionary<string, Dictionary<string, string>>();
            return;
        }

        _translations = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
        ) ?? new Dictionary<string, Dictionary<string, string>>();
    }

    protected override Dictionary<string, string>? TryGetTranslations(string key)
    {
        return _translations.TryGetValue(key, out var value) ? value : null;
    }
}
