namespace RicaveTranslator.Core.Models;

/// <summary>
///     Defines the types of operations the translation tool can perform.
/// </summary>
public enum CommandType
{
    New,
    All,
    SyncAll,
    GenerateManifest,
    Fix,
    DebugFix,
    Resume
}