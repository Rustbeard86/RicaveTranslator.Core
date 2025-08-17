namespace RicaveTranslator.Core.Interfaces;

public interface IApiKeyStore
{
    void SaveKey(string apiKey);
    string? LoadKey();
}