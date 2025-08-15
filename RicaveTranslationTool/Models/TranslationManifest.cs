using System.Text.Json;
using RicaveTranslator.Core.SourceGeneratedContexts;

namespace RicaveTranslator.Core.Models;

/// <summary>
///     Represents a manifest file that stores source text hashes for an entire language folder.
/// </summary>
public class TranslationManifest
{
    /// <summary>
    ///     A dictionary where the key is the relative file path (e.g., "SpecTranslations\\Traits.xml")
    ///     and the value is another dictionary mapping a node name to its source text hash.
    /// </summary>
    public Dictionary<string, Dictionary<string, string>> FileHashes { get; set; } = [];

    /// <summary>
    ///     Loads a manifest from a file path, or creates a new one if it doesn't exist.
    /// </summary>
    public static async Task<TranslationManifest> LoadAsync(string manifestPath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(manifestPath)) return new TranslationManifest();
        var json = await File.ReadAllTextAsync(manifestPath, cancellationToken);
        return JsonSerializer.Deserialize(json, AppJsonContext.Default.TranslationManifest) ??
               new TranslationManifest();
    }

    /// <summary>
    ///     Saves the manifest to a file path.
    /// </summary>
    public async Task SaveAsync(string manifestPath, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(this, AppJsonContext.Default.TranslationManifest);
        await File.WriteAllTextAsync(manifestPath, json, cancellationToken);
    }
}