using RicaveTranslator.Core.Models;

namespace RicaveTranslator.Core.Interfaces;

public interface IProgressTask
{
    string Description { get; set; }
    double MaxValue { get; set; }
    double Value { get; set; }
    void Increment(double amount);
}

public interface IProgressContext
{
    IProgressTask AddTask(string description);
}

public interface IUserNotifier
{
    void MarkupLine(string message);
    void WriteLine();
    void WriteLine(string message);
    void PrintSupportedLanguages(ICollection<KeyValuePair<string, string>> supportedLanguages);
    Task Progress(Func<IProgressContext, Task> action);

    void ShowLanguageSummary(string formalLanguageName,
        IReadOnlyCollection<(string File, string Status, string? Error)> fileResults, bool verbose);

    void ShowOverallSummary(TranslationJob job,
        List<(string Language, string File, string Status, string? Error)> overallFileResults);

    string GetApiKey();
    void LogSuccess(string message);
    string SelectJob(string[] jobIds);
    void PrintUsageInstructions();
    bool Confirm(string prompt);
}