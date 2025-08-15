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

    [GeneratedRegex(@"\s*\(([^)]+)\)", RegexOptions.Compiled)]
    private static partial Regex LanguageFolderRegex();

    /// <summary>
    ///     Returns a sanitized folder name for a language.
    ///     Example: "Chinese (Simplified)" -> "Chinese(Simplified)"
    ///     "Czech (Czech Republic)" -> "Czech(Czech-Republic)"
    /// </summary>
    public static string GetLanguageFolderName(string languageName)
    {
        // Remove space before '(' and replace spaces inside parentheses with '-'
        return LanguageFolderRegex().Replace(
            languageName,
            m => "(" + m.Groups[1].Value.Replace(" ", "-") + ")"
        );
    }
}