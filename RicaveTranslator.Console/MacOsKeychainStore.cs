using System.Diagnostics;
using RicaveTranslator.Core.Interfaces;

namespace RicaveTranslator.Console;

public class MacOsKeychainStore(IUserNotifier notifier) : IApiKeyStore
{
    private const string ServiceName = "RicaveTranslator";
    private const string AccountName = "GeminiApiKey";

    public void SaveKey(string apiKey)
    {
        // This command will add or update the password in the Keychain
        var processInfo = new ProcessStartInfo("security",
            $"add-generic-password -a {AccountName} -s {ServiceName} -w \"{apiKey}\" -U")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        ExecuteSecurityCommand(processInfo);
    }

    public string? LoadKey()
    {
        var processInfo =
            new ProcessStartInfo("security", $"find-generic-password -a {AccountName} -s {ServiceName} -w")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

        return ExecuteSecurityCommand(processInfo);
    }

    private string? ExecuteSecurityCommand(ProcessStartInfo processInfo)
    {
        try
        {
            using var process = Process.Start(processInfo);
            if (process == null) return null;

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode == 0) return output?.Trim();

            // A common "error" is when the item is not found, which is not a critical failure on load.
            if (error.Contains("The specified item could not be found in the keychain."))
                return null;

            notifier.MarkupLine($"[yellow]Warning: Keychain operation failed. {error?.Trim()}[/]");
            return null;
        }
        catch (Exception ex)
        {
            notifier.MarkupLine($"[red]Error interacting with macOS Keychain: {ex.Message}[/]");
            return null;
        }
    }
}