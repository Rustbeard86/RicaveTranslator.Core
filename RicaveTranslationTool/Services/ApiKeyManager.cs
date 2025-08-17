using RicaveTranslator.Core.Interfaces;

namespace RicaveTranslator.Core.Services;

/// <summary>
///     Manages the storage and retrieval of the user's API key via a platform-specific store.
/// </summary>
public class ApiKeyManager(IApiKeyStore keyStore, IUserNotifier notifier)
{
    public void SaveKey()
    {
        var apiKey = notifier.GetApiKey();
        keyStore.SaveKey(apiKey);
        notifier.LogSuccess("Your API key has been saved securely.");
    }

    public string? LoadKey()
    {
        return keyStore.LoadKey();
    }
}