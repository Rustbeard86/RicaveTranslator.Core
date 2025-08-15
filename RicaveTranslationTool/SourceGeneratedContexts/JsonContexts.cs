using System.Text.Json.Serialization;
using RicaveTranslator.Core.Models;

namespace RicaveTranslator.Core.SourceGeneratedContexts;

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(TranslationJob))]
[JsonSerializable(typeof(TranslationManifest))]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(TranslationItem))]
[JsonSerializable(typeof(DebugContent))]
[JsonSerializable(typeof(Dictionary<string, object>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, List<string>>))]
public partial class AppJsonContext : JsonSerializerContext
{
}