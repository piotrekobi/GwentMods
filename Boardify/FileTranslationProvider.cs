using Boardify;
using System.Text.Json;


// uses external file or the runtime
internal sealed class FileTranslationProvider : TranslationProviderBase
{
    private readonly Dictionary<string, Dictionary<string, string>> _translations;

    public FileTranslationProvider(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            _translations = new Dictionary<string, Dictionary<string, string>>();
            return;
        }

        string json = File.ReadAllText(filePath);

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