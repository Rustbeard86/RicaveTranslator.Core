using System.Security.Cryptography;
using System.Text;
using RicaveTranslator.Core.Interfaces;

namespace RicaveTranslator.Console;

/// <summary>
///     Stores the API key securely using the Windows Data Protection API (DPAPI).
/// </summary>
public class WindowsApiKeyStore : IApiKeyStore
{
    private readonly string _filePath;
    private readonly IUserNotifier _notifier;

    public WindowsApiKeyStore(IUserNotifier notifier)
    {
        _notifier = notifier;
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appDirectory = Path.Combine(appDataPath, "RicaveTranslator");
        Directory.CreateDirectory(appDirectory);
        _filePath = Path.Combine(appDirectory, "settings.dat");
    }

    public void SaveKey(string apiKey)
    {
        if (!OperatingSystem.IsWindows())
        {
            _notifier.MarkupLine(
                "[yellow]Warning: Secure key storage is only supported on Windows. Key will be stored in plaintext.[/]");
            File.WriteAllText(_filePath, apiKey);
            return;
        }

        var apiKeyBytes = Encoding.UTF8.GetBytes(apiKey);
        var encryptedBytes = ProtectedData.Protect(apiKeyBytes, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_filePath, encryptedBytes);
    }

    public string? LoadKey()
    {
        if (!File.Exists(_filePath)) return null;

        if (!OperatingSystem.IsWindows()) return File.ReadAllText(_filePath);

        try
        {
            var encryptedBytes = File.ReadAllBytes(_filePath);
            var apiKeyBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(apiKeyBytes);
        }
        catch (CryptographicException)
        {
            _notifier.MarkupLine("[red]Error: Could not decrypt the saved API key. It may be corrupted.[/]");
            _notifier.MarkupLine($"[grey]You can try deleting the file at {_filePath} and setting the key again.[/]");
            return null;
        }
    }
}