using RicaveTranslator.Core.Interfaces;
using RicaveTranslator.Core.Models;
using Spectre.Console;

namespace RicaveTranslator.Console;

public class SpectreNotifier : IUserNotifier
{
    public void MarkupLine(string message)
    {
        AnsiConsole.MarkupLine(message);
    }

    public void WriteLine()
    {
        AnsiConsole.WriteLine();
    }

    public void WriteLine(string message)
    {
        AnsiConsole.WriteLine(message);
    }

    public void PrintSupportedLanguages(ICollection<KeyValuePair<string, string>> supportedLanguages)
    {
        AnsiConsole.MarkupLine("Please use one of the following supported language codes:");
        var table = new Table().AddColumn("Code").AddColumn("Formal Name");
        foreach (var lang in supportedLanguages) table.AddRow($"[yellow]{lang.Key}[/]", $"[green]{lang.Value}[/]");
        AnsiConsole.Write(table);
    }

    public async Task Progress(Func<IProgressContext, Task> action)
    {
        await AnsiConsole.Progress()
            .Columns(new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn(), new SpinnerColumn())
            .StartAsync(async ctx => await action(new SpectreProgressContext(ctx)));
    }

    public string GetApiKey()
    {
        return AnsiConsole.Prompt(
            new TextPrompt<string>("Please enter your [yellow]Gemini API key[/]:")
                .PromptStyle("blue")
                .Secret());
    }

    public void LogSuccess(string message)
    {
        AnsiConsole.MarkupLine($"[bold green]✓ Success![/] {message}");
    }

    public string SelectJob(string[] jobIds)
    {
        return AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Please select a job to resume:")
                .PageSize(10)
                .AddChoices(jobIds));
    }

    public void PrintUsageInstructions()
    {
        var exeName = Path.GetFileName(Environment.ProcessPath ?? "RicaveTranslator.exe");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Usage Commands:[/]");
        AnsiConsole.MarkupLine(
            $"  [yellow]{exeName} new --lang <lang-code-1> ...[/]    (Translates languages from scratch)");
        AnsiConsole.MarkupLine(
            $"  [yellow]{exeName} all[/]                             (Translates all supported languages from scratch)");
        AnsiConsole.MarkupLine(
            $"  [yellow]{exeName} sync-all[/]                       (Translates new content and fixes all languages)");
        AnsiConsole.MarkupLine(
            $"  [yellow]{exeName} generate-manifest --lang <lang-code-1> ...[/]   (Generates source hashes for all existing translations)");
        AnsiConsole.MarkupLine(
            $"  [yellow]{exeName} fix --lang <lang-code-1> ...[/]   (Verifies and fixes missing/empty translations)");
        AnsiConsole.MarkupLine(
            $"  [yellow]{exeName} fix --lang all[/]                 (Verifies and fixes all supported languages)");
        AnsiConsole.MarkupLine(
            $"  [yellow]{exeName} debug-fix --lang <lang-code-1> ...[/](Reports why files need fixing without modifying them)");
        AnsiConsole.MarkupLine(
            $"  [yellow]{exeName} debug-fix --lang all[/]           (Reports why all files need fixing without modifying them)");
        AnsiConsole.MarkupLine($"  [yellow]{exeName} resume[/]                         (Resumes the last failed job)");
        AnsiConsole.MarkupLine(
            "  [yellow]--verbose[/]                   (Enables verbose output for file-level details)");
        AnsiConsole.MarkupLine(
            "  [yellow]--always-create-info-file[/]   (Always recreate Info.xml, even if it exists)");
    }

    public void ShowLanguageSummary(string formalLanguageName,
        IReadOnlyCollection<(string File, string Status, string? Error)> fileResults, bool verbose)
    {
        var successCount = fileResults.Count(r => r.Status == "Success");
        var failCount = fileResults.Count(r => r.Status == "Failed");

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(
            $"[bold green]{successCount} files processed successfully for [green]{formalLanguageName}[/].[/]");
        if (failCount > 0)
        {
            AnsiConsole.MarkupLine($"[bold red]{failCount} files failed for [green]{formalLanguageName}[/]:[/]");
            foreach (var failResult in fileResults.Where(r => r.Status == "Failed"))
                AnsiConsole.MarkupLine($"[red]    {failResult.File}: {failResult.Error}[/]");
        }

        if (verbose)
            foreach (var result in fileResults)
            {
                var color = result.Status == "Success" ? "green" : "red";
                AnsiConsole.MarkupLine($"[{color}]{result.Status}: {result.File}[/]");
            }
    }

    public void ShowOverallSummary(TranslationJob job,
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
                foreach (var result in group) AnsiConsole.MarkupLine($"[red]    {result.File}: {result.Error}[/]");
            }
    }
}

public class SpectreProgressContext(ProgressContext context) : IProgressContext
{
    public IProgressTask AddTask(string description)
    {
        return new SpectreProgressTask(context.AddTask(description));
    }
}

public class SpectreProgressTask(ProgressTask task) : IProgressTask
{
    public string Description
    {
        get => task.Description;
        set => task.Description = value;
    }

    public double MaxValue
    {
        get => task.MaxValue;
        set => task.MaxValue = value;
    }

    public double Value
    {
        get => task.Value;
        set => task.Value = value;
    }

    public void Increment(double amount)
    {
        task.Increment(amount);
    }
}