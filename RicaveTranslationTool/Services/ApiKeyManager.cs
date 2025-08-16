using System.Security.Cryptography;
using System.Text;

namespace RicaveTranslator.Core.Services;

/// <summary>
/// Manages the secure storage and retrieval of the user's API key using DPAPI.
/// </summary>
public class ApiKeyManager
{
    private readonly IOutputService _outputService;
    private readonly string _filePath;

    public ApiKeyManager(IOutputService outputService)
    {
        _outputService = outputService;
        // Store the encrypted key in the user's local app data folder.
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appDirectory = Path.Combine(appDataPath, "RicaveTranslator");
        Directory.CreateDirectory(appDirectory);
        _filePath = Path.Combine(appDirectory, "settings.dat");
    }

    public void SaveKey(string apiKey)
    {
        if (!OperatingSystem.IsWindows())
        {
            _outputService.MarkupLine("[yellow]Warning: Secure key storage is only supported on Windows.[/]");
            return;
        }

        var apiKeyBytes = Encoding.UTF8.GetBytes(apiKey);

        // Encrypt the key using the current user's scope.
        // Only this user on this machine can decrypt it.
        var encryptedBytes = ProtectedData.Protect(apiKeyBytes, null, DataProtectionScope.CurrentUser);

        File.WriteAllBytes(_filePath, encryptedBytes);
    }

    public string? LoadKey()
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(_filePath))
        {
            return null;
        }

        try
        {
            var encryptedBytes = File.ReadAllBytes(_filePath);

            // Decrypt the key using the current user's scope.
            var apiKeyBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);

            return Encoding.UTF8.GetString(apiKeyBytes);
        }
        catch (CryptographicException)
        {
            // This can happen if the file is corrupted or was encrypted by another user.
            _outputService.MarkupLine("[red]Error: Could not decrypt the saved API key. It may be corrupted.[/]");
            _outputService.MarkupLine($"[grey]You can try deleting the file at {_filePath} and setting the key again.[/]");
            return null;
        }
    }
}