using System.Security.Cryptography;
using RicaveTranslator.Core.Interfaces;

namespace RicaveTranslator.Console;

/// <summary>
///     Stores the API key in an encrypted file as a fallback for non-Windows/Mac/Linux systems.
///     This method is more secure than plaintext but less secure than native OS keychains.
/// </summary>
public class EncryptedFileApiKeyStore : IApiKeyStore
{
    private readonly string _encryptedKeyPath;
    private readonly string _encryptionKeyPath;
    private readonly IUserNotifier _notifier;

    public EncryptedFileApiKeyStore(IUserNotifier notifier)
    {
        _notifier = notifier;
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appDirectory = Path.Combine(appDataPath, "RicaveTranslator");
        Directory.CreateDirectory(appDirectory);

        // Path to the file containing the encrypted API key and the IV
        _encryptedKeyPath = Path.Combine(appDirectory, "settings.enc");
        // Path to the file containing the AES encryption key
        _encryptionKeyPath = Path.Combine(appDirectory, "key.bin");
    }

    public void SaveKey(string apiKey)
    {
        _notifier.MarkupLine("[bold yellow]WARNING: Secure OS-level key storage is not available.[/]");

        if (_notifier.Confirm("Do you want to save the API key in a locally encrypted file?"))
            try
            {
                // Generate a new encryption key if one doesn't exist
                if (!File.Exists(_encryptionKeyPath))
                {
                    using var aesForNewKey = Aes.Create();
                    File.WriteAllBytes(_encryptionKeyPath, aesForNewKey.Key);
                }

                var key = File.ReadAllBytes(_encryptionKeyPath);
                using var aes = Aes.Create();
                aes.Key = key;

                // The IV (Initialization Vector) is crucial for security and must be unique per encryption
                var iv = aes.IV;

                using var encryptor = aes.CreateEncryptor(aes.Key, iv);
                using var msEncrypt = new MemoryStream();
                using var csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write);
                using (var swEncrypt = new StreamWriter(csEncrypt))
                {
                    swEncrypt.Write(apiKey);
                }

                var encryptedContent = msEncrypt.ToArray();

                // Write the IV and the encrypted key to the same file
                using var fs = new FileStream(_encryptedKeyPath, FileMode.Create);
                fs.Write(iv, 0, iv.Length);
                fs.Write(encryptedContent, 0, encryptedContent.Length);

                _notifier.LogSuccess("Your API key has been saved in an encrypted file.");
            }
            catch (Exception ex)
            {
                _notifier.MarkupLine("[bold red]Error saving encrypted key:[/]");
                _notifier.WriteLine(ex.Message);
            }
        else
            _notifier.MarkupLine("[red]Operation cancelled. API key was not saved.[/]");
    }

    public string? LoadKey()
    {
        if (!File.Exists(_encryptedKeyPath) || !File.Exists(_encryptionKeyPath)) return null;

        try
        {
            var key = File.ReadAllBytes(_encryptionKeyPath);

            using var fs = new FileStream(_encryptedKeyPath, FileMode.Open);

            // Read the IV from the beginning of the file
            var iv = new byte[16];
            fs.ReadExactly(iv);

            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
            using var msDecrypt = new MemoryStream();
            fs.CopyTo(msDecrypt);

            var encryptedContent = msDecrypt.ToArray();

            using var csDecrypt =
                new CryptoStream(new MemoryStream(encryptedContent), decryptor, CryptoStreamMode.Read);
            using var srDecrypt = new StreamReader(csDecrypt);

            return srDecrypt.ReadToEnd();
        }
        catch (Exception ex)
        {
            _notifier.MarkupLine("[bold red]Error loading encrypted key:[/]");
            _notifier.WriteLine(ex.Message);
            return null;
        }
    }
}