namespace ModSettings.TranslationProviders;

public interface ITranslationProvider
{
    Dictionary<string, string> GetTranslations(string key);
}
