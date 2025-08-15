using RicaveTranslator.Core.Helpers;
using RicaveTranslator.Core.Models;
using Spectre.Console;

namespace RicaveTranslator.Core.Services;

/// <summary>
///     Handles the creation, loading, and selection of translation jobs.
/// </summary>
public class JobService(PathSettings pathSettings, LanguageHelper languageHelper)
{
    public async Task<List<TranslationJob>?> GetJobsFromArgsAsync(CommandType command, string[] langCodes)
    {
        switch (command)
        {
            case CommandType.New:
                if (langCodes.Length == 0)
                {
                    AnsiConsole.MarkupLine("[red]Error: The new command requires at least one language code.[/]");
                    return null;
                }

                var newJob = await CreateNewJobAsync(langCodes);
                return newJob != null ? [newJob] : null;

            case CommandType.All:
                var allJob = await CreateNewJobAsync([.. languageHelper.GetLanguageCodes()]);
                return allJob != null ? [allJob] : null;

            case CommandType.SyncAll:
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[bold aqua]--- Creating Bulk Sync Jobs ---[/]");
                var syncJobTasks = languageHelper.GetLanguageCodes()
                    .Select(async lang => await CreateNewJobAsync([lang], true));
                var syncJobs = await Task.WhenAll(syncJobTasks);
                var validSyncJobs = syncJobs.OfType<TranslationJob>().ToList();
                return validSyncJobs.Count > 0 ? validSyncJobs : null;

            case CommandType.GenerateManifest:
            case CommandType.Fix:
            case CommandType.DebugFix:
                return await HandleFixAndManifestCommands(command, langCodes);

            case CommandType.Resume:
                var resumeJob = await SelectJobToResumeAsync();
                return resumeJob != null ? [resumeJob] : null;

            default:
                PrintUsageInstructions();
                return null;
        }
    }

    private async Task<List<TranslationJob>?> HandleFixAndManifestCommands(CommandType command, string[] langCodes)
    {
        var isDebug = command == CommandType.DebugFix;
        var isManifest = command == CommandType.GenerateManifest;
        var jobType = isManifest ? "Manifest Generation" : isDebug ? "Debug-Fix" : "Fix";

        if (langCodes.Length == 0)
        {
            AnsiConsole.MarkupLine(
                $"[red]Error: The {command} command requires at least one language code or '--all'.[/]");
            return null;
        }

        var allLangs = langCodes.Length == 1 && langCodes[0].Equals("all", StringComparison.OrdinalIgnoreCase)
            ? [.. languageHelper.GetLanguageCodes()]
            : langCodes;

        if (allLangs.Length > 1)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[bold aqua]--- Creating Bulk {jobType} Jobs ---[/]");
        }

        var jobTasks = allLangs
            .Select(lang => CreateNewJobAsync([lang], !isManifest, isDebug, isManifest));

        var jobs = await Task.WhenAll(jobTasks);

        var validJobs = jobs.OfType<TranslationJob>().ToList();

        return validJobs.Count > 0 ? validJobs : null;
    }

    private async Task<TranslationJob?> CreateNewJobAsync(
        IReadOnlyCollection<string> languageCodes,
        bool isFixMode = false,
        bool isDebugMode = false,
        bool isManifestGenerationMode = false)
    {
        var validLanguages = languageCodes
            .Where(code =>
            {
                if (languageHelper.TryGetFormalName(code, out _)) return true;
                AnsiConsole.MarkupLine(
                    $"[yellow]Warning: Language code '{code}' is not supported and will be skipped.[/]");
                return false;
            })
            .ToList();

        if (validLanguages.Count == 0)
        {
            AnsiConsole.MarkupLine("[red]Error: No valid language codes provided.[/]");
            return null;
        }

        var job = TranslationJob.CreateNew(
            validLanguages,
            pathSettings.TemplateBasePath,
            isFixMode,
            isDebugMode,
            isManifestGenerationMode
        );

        AnsiConsole.WriteLine();
        var jobType = isManifestGenerationMode ? "GenerateManifest" :
            isFixMode ? isDebugMode ? "Debug-Fix" : "Fix" : "New";
        AnsiConsole.MarkupLine(
            $"Starting new [bold cyan]{jobType}[/] job '[bold yellow]{job.JobId}[/]' for languages: [green]{string.Join(", ", job.TargetLanguages)}[/]");

        await job.SaveAsync();
        return job;
    }

    private static async Task<TranslationJob?> SelectJobToResumeAsync()
    {
        var jobIds = TranslationJob.GetResumableJobIds();
        if (jobIds.Length == 0)
        {
            AnsiConsole.MarkupLine("[green]No incomplete jobs found to resume.[/]");
            return null;
        }

        AnsiConsole.WriteLine();
        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Please select a job to resume:")
                .PageSize(10)
                .AddChoices(jobIds));

        var job = await TranslationJob.LoadAsync(choice);
        if (job != null)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine(
                $"Resuming job '[bold yellow]{job.JobId}[/]' for languages: [green]{string.Join(", ", job.TargetLanguages)}[/]");
        }

        return job;
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
        AnsiConsole.WriteLine();
        languageHelper.PrintSupportedLanguages();
    }
}