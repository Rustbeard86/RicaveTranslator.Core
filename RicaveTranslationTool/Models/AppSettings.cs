namespace RicaveTranslator.Core.Models;

/// <summary>
///     Holds all configurable settings for the application.
/// </summary>
public class AppSettings
{
    public PathSettings Paths { get; set; } = new();
    public ApiSettings API { get; set; } = new();
    public Dictionary<string, string> SupportedLanguages { get; set; } = [];
    public bool AlwaysCreateInfoFile { get; set; }
}

public class PathSettings
{
    public string TemplateBasePath { get; set; } = "TranslationTemplate";
    public string LanguagesBasePath { get; set; } = "Languages";
}

public class ApiSettings
{
    public string ModelName { get; set; } = "gemini-2.5-pro";
    public int ApiTimeoutMinutes { get; set; } = 10;
    public int MaxFormattingRetries { get; set; } = 2;
    public int MaxNetworkRetries { get; set; } = 3;
    public int ApiBatchSize { get; set; } = 150;
    public int MaxConcurrentRequests { get; set; } = 10;
    public int IncrementalProcessingThreshold { get; set; } = 250;
}