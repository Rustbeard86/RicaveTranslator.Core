using System.Collections.Concurrent;
using System.Xml.Linq;
using RicaveTranslator.Core.Helpers;
using RicaveTranslator.Core.Models;
using Spectre.Console;

namespace RicaveTranslator.Core.Services;

/// <summary>
///     Verifies XML files against a template, identifying nodes that require translation or fixing.
/// </summary>
public class VerificationService(
    PathSettings pathSettings,
    ApiSettings apiSettings,
    NodeTranslationService nodeTranslationService)
{
    public async Task VerifyAndFixFileAsync(
        TranslationJob job,
        string sourceFilePath,
        string escapedPath,
        string targetLanguagePath,
        string formalLanguageName,
        ProgressTask task,
        ConcurrentDictionary<string, List<string>>? debugData,
        ConcurrentDictionary<string, ConcurrentDictionary<string, string>> manifest,
        CancellationToken cancellationToken)
    {
        var relativePath = Path.GetRelativePath(pathSettings.TemplateBasePath, sourceFilePath);
        var targetFilePath = Path.Combine(targetLanguagePath, relativePath);

        if (!File.Exists(targetFilePath))
        {
            task.Description = $"[cyan]INFO:[/] New file [grey]{escapedPath}[/] found. Translating from scratch.";
            await TranslateNewFileAsync(sourceFilePath, targetLanguagePath, formalLanguageName, task, manifest,
                cancellationToken);
            return;
        }

        var sourceDoc = await FileProcessingService.LoadXmlAsync(sourceFilePath, cancellationToken);
        var targetDoc = await FileProcessingService.LoadXmlAsync(targetFilePath, cancellationToken);
        var targetElements = targetDoc.Root?.Elements().ToDictionary(e => e.Name.LocalName) ?? [];
        manifest.TryGetValue(relativePath, out var fileHashes);

        var debugReasons = new List<string>();
        var itemsToTranslate = TranslationUtils.GetTranslatableItems(sourceDoc, targetElements, fileHashes,
            job.IsDebugMode ? debugReasons : null);

        if (itemsToTranslate.Count == 0)
        {
            task.Description = $"[green]✓[/] {escapedPath} [dim](Verified)[/]";
            task.Increment(100);
            return;
        }

        if (job.IsDebugMode)
        {
            debugData?.TryAdd(PathUtils.Normalize(relativePath),
                debugReasons.Count > 0 ? debugReasons : ["NEEDS FIX"]);
            task.Description = $"[yellow]NEEDS FIX:[/] {escapedPath} [dim]({itemsToTranslate.Count} nodes)[/]";
            task.Increment(100);
            return;
        }

        task.Description = $"[yellow]FIXING:[/] {escapedPath} [dim]({itemsToTranslate.Count} nodes)[/]";

        IReadOnlyDictionary<string, string> translatedTexts =
            itemsToTranslate.Count > apiSettings.IncrementalProcessingThreshold
                ? await nodeTranslationService.ProcessIncrementallyAsync(itemsToTranslate, formalLanguageName,
                    sourceFilePath, task, cancellationToken)
                : await nodeTranslationService.GetTranslationsForItemsAsync(itemsToTranslate, formalLanguageName,
                    sourceFilePath, task, cancellationToken);

        UpdateDocumentAndManifest(targetDoc, manifest, relativePath, itemsToTranslate, translatedTexts);

        await FileProcessingService.SaveXmlAsync(targetFilePath, targetDoc, cancellationToken);
    }

    public async Task TranslateNewFileAsync(
        string sourceFilePath,
        string targetLanguagePath,
        string formalLanguageName,
        ProgressTask task,
        ConcurrentDictionary<string, ConcurrentDictionary<string, string>> manifest,
        CancellationToken cancellationToken)
    {
        var relativePath = Path.GetRelativePath(pathSettings.TemplateBasePath, sourceFilePath);
        var targetFilePath = Path.Combine(targetLanguagePath, relativePath);
        var targetFileDir = Path.GetDirectoryName(targetFilePath) ??
                            throw new DirectoryNotFoundException($"Could not determine directory for {targetFilePath}");
        FileProcessingService.EnsureDirectory(targetFileDir);

        var doc = await FileProcessingService.LoadXmlAsync(sourceFilePath, cancellationToken);
        var itemsToTranslate = TranslationUtils.GetTranslatableItems(doc);

        if (itemsToTranslate.Count == 0)
        {
            if (!File.Exists(targetFilePath)) FileProcessingService.CopyFile(sourceFilePath, targetFilePath);
            task.Increment(100);
            return;
        }

        IReadOnlyDictionary<string, string> translatedTexts =
            itemsToTranslate.Count > apiSettings.IncrementalProcessingThreshold
                ? await nodeTranslationService.ProcessIncrementallyAsync(itemsToTranslate, formalLanguageName,
                    sourceFilePath, task, cancellationToken)
                : await nodeTranslationService.GetTranslationsForItemsAsync(itemsToTranslate, formalLanguageName,
                    sourceFilePath, task, cancellationToken);

        UpdateDocumentAndManifest(doc, manifest, relativePath, itemsToTranslate, translatedTexts);

        await FileProcessingService.SaveXmlAsync(targetFilePath, doc, cancellationToken);
    }

    private static void UpdateDocumentAndManifest(
        XDocument targetDoc,
        ConcurrentDictionary<string, ConcurrentDictionary<string, string>> manifest,
        string relativePath,
        IReadOnlyDictionary<string, TranslationItem> originalItems,
        IReadOnlyDictionary<string, string> translatedTexts)
    {
        foreach (var (key, item) in originalItems)
            if (translatedTexts.TryGetValue(key, out var finalTranslation))
            {
                var element = targetDoc.Root?.Element(key);
                if (element == null)
                {
                    element = new XElement(key);
                    targetDoc.Root?.Add(element);
                }

                TranslationUtils.UpdateElementTranslation(element, finalTranslation);

                var fileSpecificHashes =
                    manifest.GetOrAdd(relativePath, _ => new ConcurrentDictionary<string, string>());
                fileSpecificHashes[key] = HashingHelper.GetSha1Hash(item.OriginalContent);
            }
    }
}