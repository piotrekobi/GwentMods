namespace Boardify
{
    internal interface ITranslationProvider
    {
        Dictionary<string, string> GetTranslations(string key);
    }
}
