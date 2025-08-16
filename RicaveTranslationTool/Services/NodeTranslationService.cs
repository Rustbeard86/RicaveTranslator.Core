using System.Collections.Concurrent;
using System.Text.Json;
using RicaveTranslator.Core.Helpers;
using RicaveTranslator.Core.Models;
using RicaveTranslator.Core.SourceGeneratedContexts;
using Spectre.Console;

namespace RicaveTranslator.Core.Services;

/// <summary>
///     Handles the direct interaction with the translation API for a collection of nodes.
/// </summary>
public class NodeTranslationService(ApiSettings apiSettings, TranslationService translationService, IOutputService outputService)
{
    public async Task<Dictionary<string, string>> GetTranslationsForItemsAsync(
        Dictionary<string, TranslationItem> itemsToTranslate,
        string formalLanguageName,
        string sourceFilePath,
        ProgressTask? task,
        CancellationToken cancellationToken)
    {
        var flatTextsToTranslate = new Dictionary<string, string>();
        var originalPlaceholdersMap = new Dictionary<string, List<string>>();
        foreach (var (key, item) in itemsToTranslate)
            for (var i = 0; i < item.TextsToTranslate.Count; i++)
            {
                var flatKey = $"{key}_{i}";
                flatTextsToTranslate[flatKey] = item.TextsToTranslate[i];
                originalPlaceholdersMap[flatKey] = item.Placeholders[i];
            }

        var translatedFlatTexts =
            await translationService.CallTranslationApiAsync(flatTextsToTranslate, formalLanguageName, sourceFilePath,
                false, cancellationToken);

        await FixFormattingErrors(translatedFlatTexts, flatTextsToTranslate, originalPlaceholdersMap,
            formalLanguageName, sourceFilePath, cancellationToken);

        var finalTranslatedTexts = new Dictionary<string, string>();
        foreach (var (key, item) in itemsToTranslate)
        {
            var isList = item.IsList;
            string finalTranslation;
            if (isList)
            {
                var translatedItems = new List<string>();
                for (var j = 0; j < item.TextsToTranslate.Count; j++)
                    if (translatedFlatTexts.TryGetValue($"{key}_{j}", out var trans))
                    {
                        var restored = PlaceholderManager.Restore(trans, item.Placeholders[j]);
                        translatedItems.Add($"<li>{restored}</li>");
                    }

                finalTranslation = string.Concat(translatedItems);
            }
            else
            {
                var trans = translatedFlatTexts.GetValueOrDefault($"{key}_0") ?? string.Empty;
                finalTranslation = PlaceholderManager.Restore(trans, item.Placeholders[0]);
            }

            finalTranslatedTexts[key] = finalTranslation;
        }

        task?.Increment(100);
        return finalTranslatedTexts;
    }

    public async Task<ConcurrentDictionary<string, string>> ProcessIncrementallyAsync(
        Dictionary<string, TranslationItem> itemsToTranslate,
        string formalLanguageName,
        string sourceFilePath,
        ProgressTask task,
        CancellationToken cancellationToken)
    {
        var totalItems = itemsToTranslate.Count;
        var processedItems = 0;
        task.MaxValue = totalItems;

        var semaphore = new SemaphoreSlim(apiSettings.MaxConcurrentRequests);
        var allTranslatedTexts = new ConcurrentDictionary<string, string>();

        var chunks = itemsToTranslate.Chunk(apiSettings.ApiBatchSize).ToList();
        var tasks = chunks.Select(async chunk =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var chunkDict = chunk.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                var translatedTexts =
                    await GetTranslationsForItemsAsync(chunkDict, formalLanguageName, sourceFilePath, null,
                        cancellationToken);

                foreach (var kvp in translatedTexts) allTranslatedTexts.TryAdd(kvp.Key, kvp.Value);

                var itemsInChunk = chunk.Length;
                Interlocked.Add(ref processedItems, itemsInChunk);
                task.Value = processedItems;
                task.Description =
                    $"[yellow]INCREMENTAL:[/] {Markup.Escape(Path.GetFileName(sourceFilePath))} ({processedItems}/{totalItems})";
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
        return allTranslatedTexts;
    }

    private async Task FixFormattingErrors(
        Dictionary<string, string> translatedFlatTexts,
        Dictionary<string, string> flatTextsToTranslate,
        Dictionary<string, List<string>> originalPlaceholdersMap,
        string formalLanguageName,
        string sourceFilePath,
        CancellationToken cancellationToken)
    {
        Dictionary<string, string> itemsToFix = [];
        for (var i = 0; i < apiSettings.MaxFormattingRetries; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            itemsToFix = translatedFlatTexts
                .Where(kvp =>
                {
                    var expectedCount = originalPlaceholdersMap[kvp.Key].Count;
                    var actualCount = TranslationUtils.PlaceholderTokenRegex().Matches(kvp.Value).Count;
                    return expectedCount != actualCount;
                })
                .ToDictionary(kvp => kvp.Key, kvp => flatTextsToTranslate[kvp.Key]);

            if (itemsToFix.Count == 0) break;

            outputService.MarkupLine(
                $"[yellow]WARNING:[/] File [grey]{Markup.Escape(Path.GetFileName(sourceFilePath))}[/] has [bold red]{itemsToFix.Count}[/] formatting errors. Attempting fix {i + 1} of {apiSettings.MaxFormattingRetries}...");

            var fixedTexts =
                await translationService.CallTranslationApiAsync(itemsToFix, formalLanguageName, sourceFilePath, true,
                    cancellationToken);

            foreach (var (key, value) in fixedTexts) translatedFlatTexts[key] = value;
        }

        if (itemsToFix.Count > 0)
        {
            var errorMsg =
                $"After {apiSettings.MaxFormattingRetries} attempts, {itemsToFix.Count} items in {Path.GetFileName(sourceFilePath)} still have formatting errors.";
            outputService.MarkupLine($"[bold red]ERROR:[/] {errorMsg}");
            await SaveFormattingErrorDebugFile(sourceFilePath, itemsToFix, translatedFlatTexts,
                originalPlaceholdersMap, cancellationToken);
            throw new InvalidOperationException(
                $"{errorMsg} Debug info has been saved to the .translator_debug directory.");
        }
    }

    private static async Task SaveFormattingErrorDebugFile(
        string sourceFilePath,
        Dictionary<string, string> itemsToFix,
        Dictionary<string, string> translatedFlatTexts,
        Dictionary<string, List<string>> originalPlaceholdersMap,
        CancellationToken cancellationToken)
    {
        var debugDir = Path.Combine(Environment.CurrentDirectory, ".translator_debug");
        Directory.CreateDirectory(debugDir);
        var debugFileName =
            $"{Path.GetFileNameWithoutExtension(sourceFilePath)}_{DateTime.Now:yyyyMMdd_HHmmss_fff}_formatting_error.json";
        var debugFilePath = Path.Combine(debugDir, debugFileName);

        var debugData = itemsToFix.ToDictionary(
            kvp => kvp.Key,
            kvp => new
            {
                OriginalText = kvp.Value,
                IncorrectTranslation = translatedFlatTexts[kvp.Key],
                ExpectedPlaceholders = originalPlaceholdersMap[kvp.Key],
                ActualPlaceholdersInTranslation = TranslationUtils.PlaceholderTokenRegex()
                    .Matches(translatedFlatTexts[kvp.Key])
                    .Select(m => m.Value).ToList()
            });

        var json = JsonSerializer.Serialize(debugData, AppJsonContext.Default.DictionaryStringObject);
        await File.WriteAllTextAsync(debugFilePath, json, cancellationToken);
    }
}