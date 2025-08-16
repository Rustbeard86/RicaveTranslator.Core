using System.Text.Json;
using RicaveTranslator.Core.SourceGeneratedContexts;

namespace RicaveTranslator.Core.Models;

/// <summary>
///     Represents the state of a translation job, which can be persisted to disk.
/// </summary>
public class TranslationJob
{
    private static readonly string JobDirectory = Path.Combine(Environment.CurrentDirectory, ".translator_jobs");

    public string JobId { get; set; } = $"job_{DateTime.Now:yyyyMMdd_HHmmss}";
    public List<string> TargetLanguages { get; set; } = [];
    public Dictionary<string, List<string>> FailedFiles { get; set; } = [];
    public bool IsFixMode { get; set; }
    public bool IsDebugMode { get; set; }
    public bool IsManifestGenerationMode { get; set; }

    // Deterministic job creation
    public static TranslationJob CreateNew(
        IEnumerable<string> targetLanguages,
        string templateBasePath,
        bool isFixMode = false,
        bool isDebugMode = false,
        bool isManifestGenerationMode = false)
    {
        var allFiles = Directory.EnumerateFiles(templateBasePath, "*.xml", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        var languages = targetLanguages.ToList(); // Materialize once

        var jobType = isManifestGenerationMode ? "GenerateManifest" :
            isFixMode ? isDebugMode ? "Debug-Fix" : "Fix" : "New";
        var langIdentifier = languages.Count == 1 ? languages[0] : "multi";

        return new TranslationJob
        {
            JobId = $"job_{jobType}_{langIdentifier}_{DateTime.Now:yyyyMMdd_HHmmss_fff}",
            TargetLanguages = languages,
            FailedFiles = languages.ToDictionary(lang => lang, _ => new List<string>(allFiles)),
            IsFixMode = isFixMode,
            IsDebugMode = isDebugMode,
            IsManifestGenerationMode = isManifestGenerationMode
        };
    }

    /// <summary>
    ///     Saves the current job state to a JSON file in the jobs directory.
    /// </summary>
    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(JobDirectory);
        var filePath = Path.Combine(JobDirectory, $"{JobId}.json");
        var json = JsonSerializer.Serialize(this, AppJsonContext.Default.TranslationJob);
        await File.WriteAllTextAsync(filePath, json, cancellationToken);
    }

    /// <summary>
    ///     Deletes the job state file.
    /// </summary>
    public void Delete()
    {
        var filePath = Path.Combine(JobDirectory, $"{JobId}.json");
        if (File.Exists(filePath)) File.Delete(filePath);
    }

    /// <summary>
    ///     Checks if the job has any remaining failed files.
    /// </summary>
    public bool IsComplete()
    {
        return FailedFiles.Values.All(list => list.Count == 0);
    }

    /// <summary>
    ///     Loads a job state from a JSON file.
    /// </summary>
    public static async Task<TranslationJob?> LoadAsync(string jobId)
    {
        var filePath = Path.Combine(JobDirectory, $"{jobId}.json");
        if (!File.Exists(filePath)) return null;

        var json = await File.ReadAllTextAsync(filePath);
        return JsonSerializer.Deserialize(json, AppJsonContext.Default.TranslationJob);
    }

    /// <summary>
    ///     Gets a list of all available job files to resume.
    /// </summary>
    public static string[] GetResumableJobIds()
    {
        Directory.CreateDirectory(JobDirectory);
        return
        [
            .. Directory.GetFiles(JobDirectory, "job_*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .OfType<string>()
        ];
    }
}