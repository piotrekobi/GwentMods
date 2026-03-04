using System.Reflection;
using System.Text.Json;

#nullable enable

// uses external file in the same folder as DLL on the runtime
namespace ModSettings.TranslationProviders;

public sealed class ExternalFileTranslationProvider : TranslationProviderBase
{
    private readonly Dictionary<string, Dictionary<string, string>> _translations;

    // Constructor now only takes the file name (e.g., "BoardTranslations.json")
    public ExternalFileTranslationProvider(string fileNameInDirectory)
    {
        if (string.IsNullOrWhiteSpace(fileNameInDirectory))
        {
            _translations = new Dictionary<string, Dictionary<string, string>>();
            return;
        }

        // Compute full path relative to the DLL location
        string filePath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, fileNameInDirectory);

        if (!File.Exists(filePath))
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