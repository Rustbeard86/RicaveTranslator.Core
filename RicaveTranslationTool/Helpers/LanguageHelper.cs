using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using RicaveTranslator.Core.Models;
using Spectre.Console;

namespace RicaveTranslator.Core.Helpers;

public partial class LanguageHelper(AppSettings appSettings)
{
    private readonly Dictionary<string, string> _supportedLanguages =
        new(appSettings.SupportedLanguages, StringComparer.OrdinalIgnoreCase);

    public ICollection<string> GetLanguageCodes()
    {
        return _supportedLanguages.Keys;
    }

    public bool TryGetFormalName(string code, [MaybeNullWhen(false)] out string formalName)
    {
        return _supportedLanguages.TryGetValue(code, out formalName);
    }

    public void PrintSupportedLanguages()
    {
        AnsiConsole.MarkupLine("Please use one of the following supported language codes:");
        var table = new Table().AddColumn("Code").AddColumn("Formal Name");
        foreach (var lang in _supportedLanguages) table.AddRow($"[yellow]{lang.Key}[/]", $"[green]{lang.Value}[/]");
        AnsiConsole.Write(table);
    }

    [GeneratedRegex(@"^(.*?)\s*\(([^)]+)\)$", RegexOptions.Compiled)]
    private static partial Regex LanguageFolderRegex();

    /// <summary>
    ///     Returns a sanitized folder name for a language.
    ///     Example: "Chinese (Simplified)" -> "Chinese_Simplified"
    ///     "Czech (Czech Republic)" -> "Czech_Czech_Republic"
    ///     "Korean (South Korea)" -> "Korean_South_Korea"
    /// </summary>
    public static string GetLanguageFolderName(string languageName)
    {
        var match = LanguageFolderRegex().Match(languageName);
        if (match.Success)
        {
            // Group 1: language name, Group 2: content inside parentheses
            var main = match.Groups[1].Value.Replace(" ", "_");
            var sub = match.Groups[2].Value.Replace(" ", "_");
            return $"{main}_{sub}";
        }

        // If no parentheses, just replace spaces with underscores
        return languageName.Replace(" ", "_");
    }
}