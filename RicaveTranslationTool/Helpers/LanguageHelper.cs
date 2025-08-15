using System.Diagnostics.CodeAnalysis;
using RicaveTranslator.Core.Models;
using Spectre.Console;

namespace RicaveTranslator.Core.Helpers;

/// <summary>
///     Contains a mapping of simple language codes to formal, AI-friendly names, loaded from configuration.
/// </summary>
public class LanguageHelper(AppSettings appSettings)
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
}