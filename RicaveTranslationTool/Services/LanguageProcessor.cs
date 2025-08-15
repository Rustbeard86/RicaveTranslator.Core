using System.Collections.Concurrent;
using System.Text.Json;
using GenerativeAI;
using RicaveTranslator.Core.Helpers;
using RicaveTranslator.Core.Models;
using RicaveTranslator.Core.SourceGeneratedContexts;
using Spectre.Console;

namespace RicaveTranslator.Core.Services;

/// <summary>
///     Handles all processing tasks for a single target language within a translation job.
/// </summary>
public class LanguageProcessor(
    AppSettings appSettings,
    PathSettings pathSettings,
    ApiSettings apiSettings,
    GenerativeModel geminiModel,
    FileProcessingService fileService,
    ManifestService manifestService,
    VerificationService verificationService,
    LanguageHelper languageHelper)
{
    /// <summary>
    ///     Processes all relevant files for a single language.
    /// </summary>
    public async Task<List<(string Language, string File, string Status, string? Error)>> ProcessLanguageAsync(
        TranslationJob job,
        string languageCode,
        bool verbose,
        CancellationToken cancellationToken)
    {
        if (!languageHelper.TryGetFormalName(languageCode, out var formalLanguageName))
            return [];

        var targetLanguagePath = Path.Combine(
            pathSettings.LanguagesBasePath,
            LanguageHelper.GetLanguageFolderName(formalLanguageName)
        );
        fileService.EnsureDirectory(targetLanguagePath);

        var manifestPath = Path.Combine(targetLanguagePath, "translation_manifest.json");
        var manifest = await TranslationManifest.LoadAsync(manifestPath, cancellationToken);
        var concurrentManifest = new ConcurrentDictionary<string, ConcurrentDictionary<string, string>>(
            manifest.FileHashes.ToDictionary(
                kvp => kvp.Key,
                kvp => new ConcurrentDictionary<string, string>(kvp.Value)
            )
        );

        await CreateInfoFileIfNeededAsync(targetLanguagePath, formalLanguageName, languageCode, cancellationToken);

        var filesToProcess = job.FailedFiles[languageCode];
        if (filesToProcess.Count == 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"Language '[green]{formalLanguageName}[/]' has no files to process. Skipping.");
            return [];
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(
            $"--- Processing [yellow]{filesToProcess.Count}[/] file(s) for [green]{formalLanguageName}[/] ---");

        var fileResults = await ProcessFilesConcurrentlyAsync(job, filesToProcess, targetLanguagePath,
            formalLanguageName, concurrentManifest, cancellationToken);

        var failedFiles = fileResults.Where(r => r.Status == "Failed").Select(r => r.File).ToList();
        job.FailedFiles[languageCode] = failedFiles;
        await job.SaveAsync(CancellationToken.None);

        manifest.FileHashes = concurrentManifest.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.ToDictionary(inner => inner.Key, inner => inner.Value)
        );
        await manifest.SaveAsync(manifestPath, CancellationToken.None);

        PrintLanguageSummary(formalLanguageName, fileResults, verbose);

        return [.. fileResults.Select(r => (formalLanguageName, r.File, r.Status, r.Error))];
    }

    private async Task CreateInfoFileIfNeededAsync(string targetLanguagePath, string formalLanguageName,
        string languageCode, CancellationToken cancellationToken)
    {
        var infoFilePath = Path.Combine(targetLanguagePath, "Info.xml");
        if (appSettings.AlwaysCreateInfoFile || !File.Exists(infoFilePath))
        {
            var prompt =
                $"What is the native name for the language '{formalLanguageName}'? Provide only the name itself, without any additional text or explanation. For example, for 'Japanese (Japan)', you should return '日本語（日本）'.";
            var result = await geminiModel.GenerateContentAsync(prompt, cancellationToken);

            // Use static method as requested
            var nativeName = TranslationService.CleanApiResponse(result.Text, false);

            await fileService.CreateInfoFileAsync(targetLanguagePath, formalLanguageName, languageCode,
                nativeName,
                cancellationToken);
        }
    }

    private async Task<List<(string File, string Status, string? Error)>> ProcessFilesConcurrentlyAsync(
        TranslationJob job,
        List<string> filesToProcess,
        string targetLanguagePath,
        string formalLanguageName,
        ConcurrentDictionary<string, ConcurrentDictionary<string, string>> concurrentManifest,
        CancellationToken cancellationToken)
    {
        //var failures = new ConcurrentDictionary<string, Exception>();
        var debugData = new ConcurrentDictionary<string, List<string>>();
        var fileResults = new ConcurrentBag<(string File, string Status, string? Error)>();

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism =
                job.IsManifestGenerationMode || job.IsFixMode
                    ? Environment.ProcessorCount
                    : apiSettings.MaxConcurrentRequests,
            CancellationToken = cancellationToken
        };

        await AnsiConsole.Progress()
            .Columns(new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn(), new SpinnerColumn())
            .StartAsync(async ctx =>
            {
                try
                {
                    await Parallel.ForEachAsync(filesToProcess, parallelOptions, async (filePath, ct) =>
                    {
                        var relativePath = Path.GetRelativePath(pathSettings.TemplateBasePath, filePath);
                        var escapedPath = Markup.Escape(relativePath);
                        var task = ctx.AddTask($"[grey]{escapedPath}[/]");

                        try
                        {
                            if (job.IsManifestGenerationMode)
                                await manifestService.GenerateManifestForFileAsync(filePath, targetLanguagePath, task,
                                    concurrentManifest, ct);
                            else if (job.IsFixMode)
                                await verificationService.VerifyAndFixFileAsync(job, filePath, escapedPath,
                                    targetLanguagePath, formalLanguageName, task, debugData, concurrentManifest, ct);
                            else
                                await verificationService.TranslateNewFileAsync(filePath, targetLanguagePath,
                                    formalLanguageName, task, concurrentManifest, ct);

                            fileResults.Add((PathUtils.Normalize(relativePath), "Success", null));
                        }
                        catch (Exception ex)
                        {
                            //failures.TryAdd(filePath, ex);
                            fileResults.Add((relativePath, "Failed", ex.GetBaseException().Message));
                        }
                    });
                }
                catch (OperationCanceledException)
                {
                    // Suppress
                }
            });

        if (job.IsDebugMode && !debugData.IsEmpty)
        {
            await SaveDebugReportAsync(job, debugData, cancellationToken);
            return MarkDebugIssuesAsFailed([.. fileResults], debugData);
        }

        return [.. fileResults];
    }

    private static async Task SaveDebugReportAsync(TranslationJob job,
        ConcurrentDictionary<string, List<string>> debugData, CancellationToken cancellationToken)
    {
        var debugDir = Path.Combine(Environment.CurrentDirectory, ".translator_debug");
        Directory.CreateDirectory(debugDir);
        var debugFileName = $"debug_fix_report_{job.JobId}_{job.TargetLanguages.First()}.json";
        var debugFilePath = Path.Combine(debugDir, debugFileName);

        var json = JsonSerializer.Serialize(
            debugData.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
            AppJsonContext.Default.DictionaryStringListString
        );
        await File.WriteAllTextAsync(debugFilePath, json, cancellationToken);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[bold yellow]Debug report saved to:[/] [grey]{Markup.Escape(debugFilePath)}[/]");
    }

    private static List<(string File, string Status, string? Error)> MarkDebugIssuesAsFailed(
        List<(string File, string Status, string? Error)> results,
        ConcurrentDictionary<string, List<string>> debugData)
    {
        var debugIssueFiles = new HashSet<string>(debugData.Keys.Select(PathUtils.Normalize));
        return
        [
            .. results
                .Select(r => debugIssueFiles.Contains(PathUtils.Normalize(r.File))
                    ? (r.File, "Failed", "Debug issues reported (see debug log)")
                    : r)
        ];
    }

    private static void PrintLanguageSummary(string formalLanguageName,
        IReadOnlyCollection<(string File, string Status, string? Error)> fileResults,
        bool verbose)
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
}