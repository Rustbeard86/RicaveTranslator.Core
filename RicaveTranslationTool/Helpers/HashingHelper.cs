using System.Security.Cryptography;
using System.Text;

namespace RicaveTranslator.Core.Helpers;

/// <summary>
///     Provides helper methods for creating content hashes.
/// </summary>
public static class HashingHelper
{
    /// <summary>
    ///     Computes the SHA-1 hash of a string.
    /// </summary>
    public static string GetSha1Hash(string input)
    {
        var hashBytes = SHA1.HashData(Encoding.UTF8.GetBytes(input));
        var sb = new StringBuilder(hashBytes.Length * 2);
        foreach (var b in hashBytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}