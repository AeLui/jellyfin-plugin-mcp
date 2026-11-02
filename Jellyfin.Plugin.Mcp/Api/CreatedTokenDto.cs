using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Mcp.Api;

/// <summary>
/// A newly created token, including its plaintext secret. The plaintext is returned only once.
/// </summary>
public sealed class CreatedTokenDto
{
    /// <summary>
    /// Gets the stored token.
    /// </summary>
    public TokenDto Token { get; init; } = new();

    /// <summary>
    /// Gets the plaintext secret.
    /// </summary>
    public string Plaintext { get; init; } = string.Empty;
}
