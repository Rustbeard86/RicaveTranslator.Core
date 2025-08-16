using System.Collections.Concurrent;
using System.Xml.Linq;
using RicaveTranslator.Core.Helpers;
using RicaveTranslator.Core.Interfaces;
using RicaveTranslator.Core.Models;
using Spectre.Console;

namespace RicaveTranslator.Core.Services;

public class ManifestService(PathSettings pathSettings)
{
    public async Task GenerateManifestForFileAsync(
        string sourceFilePath,
        string targetLanguagePath,
        IProgressTask task,
        ConcurrentDictionary<string, ConcurrentDictionary<string, string>> manifest,
        CancellationToken cancellationToken)
    {
        var relativePath = Path.GetRelativePath(pathSettings.TemplateBasePath, sourceFilePath);
        var targetFilePath = Path.Combine(targetLanguagePath, relativePath);
        var escapedPath = Markup.Escape(relativePath);

        task.Description = $"[cyan]GENERATING HASHES:[/] {escapedPath}";

        if (!File.Exists(targetFilePath))
        {
            task.Description = $"[yellow]SKIP:[/] Target file not found for {escapedPath}";
            task.Increment(100);
            return;
        }

        await using var stream = File.OpenRead(sourceFilePath);
        var sourceDoc = await XDocument.LoadAsync(stream, LoadOptions.PreserveWhitespace, cancellationToken);

        var nodesToHash = sourceDoc.Descendants()
            .Where(el => !el.HasElements && TranslationUtils.FindEnglishComment(el).Found)
            .ToList();

        if (nodesToHash.Count == 0)
        {
            task.Description = $"[green]✓[/] No translatable nodes in {escapedPath}";
            task.Increment(100);
            return;
        }

        var fileSpecificHashes = manifest.GetOrAdd(relativePath, _ => new ConcurrentDictionary<string, string>());

        foreach (var element in nodesToHash)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (found, content) = TranslationUtils.FindEnglishComment(element);
            if (found) fileSpecificHashes[element.Name.LocalName] = HashingHelper.GetSha1Hash(content);
        }

        task.Description = $"[green]✓[/] Generated hashes for {escapedPath}";
        task.Increment(100);
    }
}