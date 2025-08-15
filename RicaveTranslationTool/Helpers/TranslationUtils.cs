using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using RicaveTranslator.Core.Models;

namespace RicaveTranslator.Core.Helpers;

public static partial class TranslationUtils
{
    [GeneratedRegex("__p\\d+__", RegexOptions.Compiled)]
    public static partial Regex PlaceholderTokenRegex();

    [GeneratedRegex("<En>(.*)</En>", RegexOptions.Singleline | RegexOptions.Compiled)]
    public static partial Regex EnTagRegex();

    public static (bool Found, string Content) FindEnglishComment(XElement element)
    {
        XNode? currentNode = element;
        while (currentNode.NextNode != null)
        {
            currentNode = currentNode.NextNode;
            if (currentNode is XComment comment)
            {
                var match = EnTagRegex().Match(comment.Value);
                if (match.Success) return (true, match.Groups[1].Value.Trim());
            }

            if (currentNode is XElement || (currentNode is XText text && !string.IsNullOrWhiteSpace(text.Value))) break;
        }

        return (false, string.Empty);
    }

    public static Dictionary<string, TranslationItem> GetTranslatableItems(
        XDocument sourceDoc,
        Dictionary<string, XElement>? targetElements = null,
        ConcurrentDictionary<string, string>? fileHashes = null,
        List<string>? debugOutput = null)
    {
        var itemsToTranslate = new Dictionary<string, TranslationItem>();
        var potentialElements = sourceDoc.Descendants().Where(el => !el.HasElements).ToList();

        foreach (var element in potentialElements)
        {
            var (found, content) = FindEnglishComment(element);
            if (!found) continue;

            if (targetElements != null && targetElements.TryGetValue(element.Name.LocalName, out var targetElement))
            {
                var currentSourceHash = HashingHelper.GetSha1Hash(content);
                if (fileHashes != null && fileHashes.TryGetValue(element.Name.LocalName, out var storedSourceHash))
                {
                    if (string.Equals(currentSourceHash, storedSourceHash, StringComparison.OrdinalIgnoreCase))
                        continue;

                    debugOutput?.Add(
                        $"'{element.Name.LocalName}': Source text changed (hash mismatch). Re-translating.");
                }
                else
                {
                    var targetInnerXml = string.Concat(targetElement.Nodes());
                    var sourceItem = CreateTranslationItem(content);
                    var targetItem = CreateTranslationItem(targetInnerXml);

                    var structureIsSame = sourceItem.IsList == targetItem.IsList;

                    var sourcePlaceholders = sourceItem.Placeholders.SelectMany(p => p).OrderBy(x => x).ToList();
                    var targetPlaceholders = targetItem.Placeholders.SelectMany(p => p).OrderBy(x => x).ToList();
                    var placeholdersMatch = sourcePlaceholders.SequenceEqual(targetPlaceholders);

                    var textContentIsValid = sourceItem.TextsToTranslate.Count == targetItem.TextsToTranslate.Count &&
                                             !targetItem.TextsToTranslate.Any(string.IsNullOrWhiteSpace);

                    if (sourceItem.TextsToTranslate.All(string.IsNullOrWhiteSpace))
                    {
                        if (placeholdersMatch && structureIsSame) continue;
                    }
                    else
                    {
                        if (placeholdersMatch && textContentIsValid && structureIsSame) continue;
                    }

                    var reason =
                        $"Structure/content mismatch. Structure same: {structureIsSame}, Placeholders match: {placeholdersMatch}, Text valid: {textContentIsValid}.";
                    debugOutput?.Add($"'{element.Name.LocalName}': {reason}");
                }
            }
            else if (targetElements != null)
            {
                debugOutput?.Add($"'{element.Name.LocalName}': Target element not found.");
            }

            itemsToTranslate.Add(element.Name.LocalName, CreateTranslationItem(content));
        }

        return itemsToTranslate;
    }

    public static void UpdateElementTranslation(XElement element, string finalTranslation)
    {
        try
        {
            var parsedContent = XElement.Parse($"<root>{finalTranslation}</root>");
            element.ReplaceNodes(parsedContent.Nodes());
        }
        catch (XmlException)
        {
            element.Value = finalTranslation;
        }
    }

    public static TranslationItem CreateTranslationItem(string originalContent)
    {
        var item = new TranslationItem { OriginalContent = originalContent };

        try
        {
            var parsedContent = XElement.Parse($"<root>{originalContent}</root>");
            var listItems = parsedContent.Elements("li").ToList();

            if (listItems.Count > 0)
            {
                var texts = new List<string>();
                var placeholdersList = new List<List<string>>();
                foreach (var li in listItems)
                {
                    var innerContent = string.Concat(li.Nodes());
                    var (sanitized, placeholders) = PlaceholderManager.Extract(innerContent);
                    texts.Add(sanitized);
                    placeholdersList.Add(placeholders);
                }

                item.TextsToTranslate = texts;
                item.Placeholders = placeholdersList;
                item.IsList = true;
                return item;
            }
        }
        catch (XmlException)
        {
            // Content is not XML, treat as plain text.
        }

        var (sanitizedText, singlePlaceholders) = PlaceholderManager.Extract(originalContent);
        item.TextsToTranslate = [sanitizedText];
        item.Placeholders = [singlePlaceholders];
        item.IsList = false;
        return item;
    }
}