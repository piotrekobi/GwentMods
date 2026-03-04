using Boardify;
using System.Reflection;
using System.Text.Json;

// embeds the file from build in the DLL
internal sealed class EmbeddedFileTranslationProvider : TranslationProviderBase
{
    private readonly Dictionary<string, Dictionary<string, string>> _translations;

    public EmbeddedFileTranslationProvider(string resourceName)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            _translations = new Dictionary<string, Dictionary<string, string>>();
            return;
        }

        using var reader = new StreamReader(stream);
        string json = reader.ReadToEnd();

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