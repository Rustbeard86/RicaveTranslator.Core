using RicaveTranslator.Core.Helpers;

using Spectre.Console;

using System.Xml.Linq;

#pragma warning disable CA1822 // Mark members as static

// This warning is disabled for FileProcessingService because it's designed as an injectable service
// for architectural consistency and testability (allowing it to be mocked), even though its
// current methods don't rely on instance state.

namespace RicaveTranslator.Core.Services;

/// <summary>
///     Provides file and directory operations for XML translation workflows.
///     Designed as an injectable service for architectural consistency and testability.
///     Supports directory creation, XML file loading and saving, file copying, and creation of language info files.
/// </summary>
public class FileProcessingService
{
    public void EnsureDirectory(string path)
    {
        Directory.CreateDirectory(path);
    }

    public async Task<XDocument> LoadXmlAsync(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(filePath);
        return await XDocument.LoadAsync(stream, LoadOptions.PreserveWhitespace, cancellationToken);
    }

    public async Task SaveXmlAsync(string filePath, XDocument doc, CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(filePath, doc.ToString(), cancellationToken);
    }

    public void CopyFile(string source, string destination, bool overwrite = true)
    {
        File.Copy(source, destination, overwrite);
    }

    public async Task CreateInfoFileAsync(string targetLanguagePath, string formalLanguageName,
        string languageCode,
        string nativeName, CancellationToken cancellationToken)
    {
        var infoDoc = new XDocument(
            new XElement("LanguageInfo",
                new XElement("englishName", LanguageHelper.GetLanguageFolderName(formalLanguageName)),
                new XElement("nativeName", nativeName),
                new XElement("cultureName", languageCode)
            )
        );
        var infoFilePath = Path.Combine(targetLanguagePath, "Info.xml");
        await File.WriteAllTextAsync(infoFilePath, infoDoc.ToString(), cancellationToken);
        AnsiConsole.MarkupLine($"[blue]- INFO:     Created Info.xml for {formalLanguageName}[/]");
    }
}