using System;
using System.Security.Cryptography;
using System.Text;

namespace Jellyfin.Plugin.Mcp.Auth;

/// <summary>
/// Generates and hashes MCP access tokens.
/// </summary>
public static class TokenGenerator
{
    /// <summary>
    /// The prefix of every token.
    /// </summary>
    public const string TokenPrefix = "jmcp_";

    private const int SecretBytes = 32;
    private const int DisplayPrefixLength = 8;

    /// <summary>
    /// Creates a new random token.
    /// </summary>
    /// <returns>The plaintext token.</returns>
    public static string NewToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(SecretBytes);
        var secret = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return TokenPrefix + secret;
    }

    /// <summary>
    /// Computes the lowercase SHA-256 hex hash of a token.
    /// </summary>
    /// <param name="token">The plaintext token.</param>
    /// <returns>The hash.</returns>
    public static string Hash(string token)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }

    /// <summary>
    /// Gets the first characters of the secret part, for display.
    /// </summary>
    /// <param name="token">The plaintext token.</param>
    /// <returns>The display prefix.</returns>
    public static string DisplayPrefix(string token)
    {
        var secret = token.StartsWith(TokenPrefix, StringComparison.Ordinal) ? token[TokenPrefix.Length..] : token;
        return secret.Length <= DisplayPrefixLength ? secret : secret[..DisplayPrefixLength];
    }

    /// <summary>
    /// Compares two hashes in constant time.
    /// </summary>
    /// <param name="a">The first hash.</param>
    /// <param name="b">The second hash.</param>
    /// <returns>True when equal.</returns>
    public static bool HashEquals(string a, string b)
    {
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
    }
}
