using System.Reflection;
using System.Text.Json;

#nullable enable

namespace ModSettings.TranslationProviders;

// embeds the file from build in the DLL, use following code snippet in .csproj to include the file as embedded resource
// <ItemGroup>
//  < EmbeddedResource Include = "translations.json" />
// </ItemGroup >
public sealed class EmbeddedFileTranslationProvider : TranslationProvider
{
    private readonly Dictionary<string, Dictionary<string, string>> _translations;

    public EmbeddedFileTranslationProvider(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName);
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

    protected override Dictionary<string, string>? TryGetTranslationsFor(string key)
    {
        return _translations.TryGetValue(key, out var value) ? value : null;
    }
}