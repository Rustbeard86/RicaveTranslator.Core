using System.Text.RegularExpressions;

namespace RicaveTranslator.Core.Helpers;

/// <summary>
///     Handles the extraction and restoration of non-translatable placeholders (like XML tags or game variables)
///     from a string.
/// </summary>
public static partial class PlaceholderManager
{
    [GeneratedRegex(@"<[^>]+>|\[[^\]]+\]", RegexOptions.Compiled)]
    private static partial Regex PlaceholderRegex();

    [GeneratedRegex(@"__p(\d+)__")]
    private static partial Regex TokenRegex();

    /// <summary>
    ///     Extracts placeholders from a string, replacing them with tokens.
    /// </summary>
    /// <returns>A tuple containing the sanitized string and a list of the extracted placeholders.</returns>
    public static (string Sanitized, List<string> Placeholders) Extract(string content)
    {
        var placeholders = new List<string>();
        var sanitizedContent = PlaceholderRegex().Replace(content, match =>
        {
            var placeholderIndex = placeholders.Count;
            placeholders.Add(match.Value);
            return $"__p{placeholderIndex}__";
        });

        return (sanitizedContent, placeholders);
    }

    /// <summary>
    ///     Restores original placeholders into a translated string containing tokens.
    /// </summary>
    public static string Restore(string translatedContent, List<string> originalPlaceholders)
    {
        if (originalPlaceholders.Count == 0) return translatedContent;

        return TokenRegex().Replace(translatedContent, match =>
        {
            if (int.TryParse(match.Groups[1].Value, out var placeholderIndex) &&
                placeholderIndex < originalPlaceholders.Count)
                return originalPlaceholders[placeholderIndex];

            // If the token is invalid or out of bounds, return it as is.
            return match.Value;
        });
    }
}