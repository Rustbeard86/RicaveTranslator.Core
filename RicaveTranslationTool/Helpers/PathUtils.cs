namespace RicaveTranslator.Core.Helpers;

/// <summary>
///     Provides utility methods for path manipulation.
/// </summary>
public static class PathUtils
{
    /// <summary>
    ///     Normalizes a file path for consistent use in collections and comparisons.
    ///     Replaces backslashes with forward slashes and converts to lowercase.
    /// </summary>
    public static string Normalize(string path)
    {
        return path.Replace('\\', '/').ToLowerInvariant();
    }
}