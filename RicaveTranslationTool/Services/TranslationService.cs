using System.Text.Json;
using System.Text.RegularExpressions;
using GenerativeAI;
using RicaveTranslator.Core.Models;
using RicaveTranslator.Core.SourceGeneratedContexts;
using Spectre.Console;

namespace RicaveTranslator.Core.Services;

public partial class TranslationService(ApiSettings apiSettings, GenerativeModel geminiModel)
{
    private static readonly JsonDocumentOptions SJsonDocOptions = new() { AllowTrailingCommas = true };

    [GeneratedRegex(@"```(?:json)?\s*(\{[\s\S]*\})\s*```", RegexOptions.Multiline)]
    private static partial Regex JsonMarkdownRegex();

    public async Task<Dictionary<string, string>> CallTranslationApiAsync(
        Dictionary<string, string> textsToTranslate,
        string formalLanguageName,
        string sourceFilePath,
        bool isFixAttempt = false,
        CancellationToken cancellationToken = default)
    {
        var translatedTexts = new Dictionary<string, string>();

        foreach (var batch in textsToTranslate.Chunk(apiSettings.ApiBatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batchDictionary = new Dictionary<string, string>(batch);
            var jsonToTranslate =
                JsonSerializer.Serialize(batchDictionary, AppJsonContext.Default.DictionaryStringString);
            var prompt = isFixAttempt
                ? BuildFixJsonPrompt(formalLanguageName, jsonToTranslate)
                : BuildJsonPrompt(formalLanguageName, jsonToTranslate);

            for (var attempt = 1; attempt <= apiSettings.MaxNetworkRetries; attempt++)
            {
                string? rawResponse = null;
                try
                {
                    using var timeoutCts =
                        new CancellationTokenSource(TimeSpan.FromMinutes(apiSettings.ApiTimeoutMinutes));
                    using var linkedCts =
                        CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);
                    var result = await geminiModel.GenerateContentAsync(prompt, linkedCts.Token);
                    rawResponse = result.Text;
                    var translatedJson = CleanApiResponse(rawResponse);

                    using var jsonDoc = JsonDocument.Parse(translatedJson, SJsonDocOptions);
                    foreach (var property in jsonDoc.RootElement.EnumerateObject())
                        if (property.Value.ValueKind == JsonValueKind.String)
                            translatedTexts[property.Name] = property.Value.GetString() ?? string.Empty;

                    break;
                }
                catch (OperationCanceledException) when (attempt < apiSettings.MaxNetworkRetries)
                {
                    AnsiConsole.MarkupLine(
                        $"[orange1]API request timed out (Attempt {attempt}/{apiSettings.MaxNetworkRetries}). Retrying in 5 seconds...[/]");
                    await Task.Delay(5000, cancellationToken);
                }
                catch (OperationCanceledException ex)
                {
                    throw new TimeoutException(
                        $"API request timed out after {apiSettings.ApiTimeoutMinutes} minute(s).", ex);
                }
                catch (HttpRequestException httpEx) when (attempt < apiSettings.MaxNetworkRetries)
                {
                    AnsiConsole.MarkupLine(
                        $"[orange1]API request failed (Attempt {attempt}/{apiSettings.MaxNetworkRetries}): {httpEx.Message}. Retrying in 5 seconds...[/]");
                    await Task.Delay(5000, cancellationToken);
                }
                catch (JsonException ex)
                {
                    var debugDir = Path.Combine(Environment.CurrentDirectory, ".translator_debug");
                    Directory.CreateDirectory(debugDir);
                    var debugFileName =
                        $"{Path.GetFileNameWithoutExtension(sourceFilePath)}_{DateTime.Now:yyyyMMdd_HHmmss_fff}_parsing_error.txt";
                    var debugFilePath = Path.Combine(debugDir, debugFileName);

                    var debugContent = new DebugContent
                    {
                        Error = "Failed to parse API response as JSON.",
                        ExceptionMessage = ex.Message,
                        Prompt = prompt,
                        RawResponse = rawResponse ?? "Response was null."
                    };

                    await File.WriteAllTextAsync(
                        debugFilePath,
                        JsonSerializer.Serialize(debugContent, AppJsonContext.Default.DebugContent),
                        cancellationToken
                    );

                    throw new InvalidDataException(
                        $"Failed to parse API response for '{sourceFilePath}'. Raw response and prompt saved to '{debugFilePath}'.",
                        ex);
                }
            }
        }

        return translatedTexts;
    }

    public static string BuildJsonPrompt(string formalLanguageName, string jsonContent)
    {
        return $$"""
                 You are an expert translator for the video game 'Ricave'. Your task is to translate the string values in the following JSON object from English to {{formalLanguageName}}.
                 You must follow these rules precisely:
                 1. Return ONLY a valid JSON object. Do not include any other text or explanations.
                 2. Preserve the original JSON structure and all keys exactly.
                 3. Translate only the string values.
                 4. Ensure all string values are properly JSON-escaped.
                 5. **CRITICAL**: Preserve all placeholder tokens (e.g., `__p0__`, `__p1__`) exactly as they appear. Do not translate them.
                 Here is the JSON object to translate:
                 {{jsonContent}}
                 """;
    }

    public static string BuildFixJsonPrompt(string formalLanguageName, string jsonContent)
    {
        return $$"""
                 You are a translation correction assistant. You previously failed to preserve placeholder tokens in a translation.
                 Your task is to translate the following JSON values from English to {{formalLanguageName}} again, this time following the rules correctly.

                 You must follow these rules precisely:
                 1. Return ONLY a valid JSON object.
                 2. **CRITICAL**: You MUST preserve all placeholder tokens (e.g., `__p0__`, `__p1__`) exactly as they appear in the original text. Do not translate them. This is the most important rule.
                 3. Preserve the original JSON structure and all keys exactly.

                 Here is the JSON object with the original English text that you must translate correctly:
                 {{jsonContent}}
                 """;
    }

    public static string CleanApiResponse(string rawResponse, bool isJson = true)
    {
        if (isJson)
        {
            var match = JsonMarkdownRegex().Match(rawResponse);
            if (match.Success) return match.Groups[1].Value.Trim();

            var startIndex = rawResponse.IndexOf('{');
            var endIndex = rawResponse.LastIndexOf('}');
            if (startIndex != -1 && endIndex != -1 && endIndex > startIndex)
                return rawResponse.Substring(startIndex, endIndex - startIndex + 1).Trim();
        }

        return rawResponse.Trim().Trim('"');
    }
}