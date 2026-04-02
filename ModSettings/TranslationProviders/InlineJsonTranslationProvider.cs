using System.Text.Json;

#nullable enable

namespace ModSettings.TranslationProviders;

public sealed class InlineJsonTranslationProvider : TranslationProvider
{
    private readonly Dictionary<string, Dictionary<string, string>> _translations;

    public InlineJsonTranslationProvider(string json)
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

    protected override Dictionary<string, string>? TryGetTranslationsFor(string key)
    {
        return _translations.TryGetValue(key, out var value) ? value : null;
    }
}
