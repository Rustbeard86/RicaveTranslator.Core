namespace RicaveTranslator.Core.Models;

/// <summary>
///     Represents a piece of content to be translated, which may include multiple text segments and placeholders.
/// </summary>
public class TranslationItem
{
    /// <summary>
    ///     A list of text segments to be translated. For simple content, this will have one item.
    ///     For lists (e.g., <li> elements), it will have multiple items.
    /// </summary>
    public List<string> TextsToTranslate { get; set; } = [];

    /// <summary>
    ///     A list of placeholder lists, corresponding to each text segment in TextsToTranslate.
    /// </summary>
    public List<List<string>> Placeholders { get; set; } = [];

    /// <summary>
    ///     Indicates whether the original content was an HTML list (i.e., contained <li> tags).
    /// </summary>
    public bool IsList { get; set; }

    /// <summary>
    ///     The original, unmodified source content from the <En> tag.
    /// </summary>
    public string OriginalContent { get; set; } = string.Empty;
}