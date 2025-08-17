using System.Diagnostics;
using RicaveTranslator.Core.Interfaces;

namespace RicaveTranslator.Console;

public class LinuxKeyringStore(IUserNotifier notifier) : IApiKeyStore
{
    private const string Attribute = "RicaveTranslator.GeminiApiKey";

    public void SaveKey(string apiKey)
    {
        var processInfo =
            new ProcessStartInfo("secret-tool", $"store --label=\"Ricave Translator API Key\" {Attribute}")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

        ExecuteSecretToolCommand(processInfo, apiKey);
    }

    public string? LoadKey()
    {
        var processInfo = new ProcessStartInfo("secret-tool", $"lookup {Attribute}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        return ExecuteSecretToolCommand(processInfo);
    }

    private string? ExecuteSecretToolCommand(ProcessStartInfo processInfo, string? input = null)
    {
        try
        {
            using var process = Process.Start(processInfo);
            if (process == null) return null;

            if (!string.IsNullOrEmpty(input))
            {
                process.StandardInput.Write(input);
                process.StandardInput.Close();
            }

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode == 0) return output.Trim();

            if (error.Contains("No such item")) return null;

            notifier.MarkupLine(
                $"[yellow]Warning: secret-tool operation failed. It may not be installed. {error.Trim()}[/]");
            return null;
        }
        catch (Exception ex)
        {
            notifier.MarkupLine($"[red]Error interacting with Linux Secret Service: {ex.Message}[/]");
            notifier.MarkupLine(
                "[yellow]Please ensure 'secret-tool' is installed (e.g., via 'sudo apt-get install libsecret-tools').[/]");
            return null;
        }
    }
}