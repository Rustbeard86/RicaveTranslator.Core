using RicaveTranslator.Core.Models;
using Spectre.Console;

namespace RicaveTranslator.Core.Services;

/// <summary>
///     Orchestrates the high-level processing of a translation job, delegating language-specific tasks.
/// </summary>
public class TranslationProcessor(LanguageProcessor languageProcessor)
{
    /// <summary>
    ///     Orchestrates the entire translation process for a given job.
    /// </summary>
    public async Task<List<(string Language, string File, string Status, string? Error)>> ProcessJobAsync(
        TranslationJob job, bool verbose, CancellationToken cancellationToken = default)
    {
        if (job.IsFixMode)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine(
                job.IsDebugMode
                    ? "[bold cyan]Running in Debug & Fix mode.[/]"
                    : "[bold cyan]Running in Sync & Fix mode.[/]");
        }

        var overallFileResults = new List<(string Language, string File, string Status, string? Error)>();

        foreach (var languageCode in job.TargetLanguages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Delegate the processing for each language to the LanguageProcessor.
            var languageResults =
                await languageProcessor.ProcessLanguageAsync(job, languageCode, verbose, cancellationToken);
            overallFileResults.AddRange(languageResults);
        }

        PrintOverallSummary(job, overallFileResults);

        return overallFileResults;
    }

    /// <summary>
    ///     Prints a summary of the entire job if it involved multiple languages.
    /// </summary>
    private static void PrintOverallSummary(TranslationJob job,
        List<(string Language, string File, string Status, string? Error)> overallFileResults)
    {
        if (job.TargetLanguages.Count <= 1) return;

        var totalSuccess = overallFileResults.Count(r => r.Status == "Success");
        var totalFail = overallFileResults.Count(r => r.Status == "Failed");
        var totalFiles = overallFileResults.Count;

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(
            $"[bold green]Overall Job Summary: {totalSuccess} succeeded, [red]{totalFail} failed, [yellow]{totalFiles} processed.[/]");

        if (totalFail > 0)
            foreach (var group in overallFileResults.Where(r => r.Status == "Failed").GroupBy(r => r.Language))
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine($"[red]Failures for {group.Key}:[/]");
                foreach (var result in group)
                    AnsiConsole.MarkupLine($"[red]    {result.File}: {result.Error}[/]");
            }
    }
}