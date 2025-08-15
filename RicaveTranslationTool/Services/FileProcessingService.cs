using System.Xml.Linq;
using Spectre.Console;

namespace RicaveTranslator.Core.Services;

public class FileProcessingService
{
    public static void EnsureDirectory(string path)
    {
        Directory.CreateDirectory(path);
    }

    public static async Task<XDocument> LoadXmlAsync(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(filePath);
        return await XDocument.LoadAsync(stream, LoadOptions.PreserveWhitespace, cancellationToken);
    }

    public static async Task SaveXmlAsync(string filePath, XDocument doc, CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(filePath, doc.ToString(), cancellationToken);
    }

    public static void CopyFile(string source, string destination, bool overwrite = true)
    {
        File.Copy(source, destination, overwrite);
    }

    public static async Task CreateInfoFileAsync(string targetLanguagePath, string formalLanguageName,
        string languageCode,
        string nativeName, CancellationToken cancellationToken)
    {
        var infoDoc = new XDocument(
            new XElement("LanguageInfo",
                new XElement("englishName", formalLanguageName),
                new XElement("nativeName", nativeName),
                new XElement("cultureName", languageCode)
            )
        );
        var infoFilePath = Path.Combine(targetLanguagePath, "Info.xml");
        await File.WriteAllTextAsync(infoFilePath, infoDoc.ToString(), cancellationToken);
        AnsiConsole.MarkupLine($"[blue]- INFO:     Created Info.xml for {formalLanguageName}[/]");
    }
}