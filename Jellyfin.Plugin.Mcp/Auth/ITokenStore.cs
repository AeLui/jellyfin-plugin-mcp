using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Mcp.Configuration;

namespace Jellyfin.Plugin.Mcp.Auth;

/// <summary>
/// Storage of MCP access tokens.
/// </summary>
public interface ITokenStore
{
    /// <summary>
    /// Gets a snapshot of all tokens.
    /// </summary>
    /// <returns>The tokens.</returns>
    IReadOnlyList<McpToken> GetAll();

    /// <summary>
    /// Finds a token by its hash.
    /// </summary>
    /// <param name="hash">The lowercase SHA-256 hex hash.</param>
    /// <returns>A snapshot of the token, or null.</returns>
    McpToken? FindByHash(string hash);

    /// <summary>
    /// Creates and persists a new token.
    /// </summary>
    /// <param name="name">The display name.</param>
    /// <param name="userId">The Jellyfin user the token acts as.</param>
    /// <param name="scopes">The granted scopes.</param>
    /// <param name="expiresAt">The expiry, or null.</param>
    /// <returns>The stored token and its plaintext, which is never stored.</returns>
    (McpToken Token, string Plaintext) Create(string name, Guid userId, IReadOnlyCollection<string> scopes, DateTime? expiresAt);

    /// <summary>
    /// Deletes a token.
    /// </summary>
    /// <param name="id">The token id.</param>
    /// <returns>True when a token was removed.</returns>
    bool Delete(Guid id);

    /// <summary>
    /// Records a use of the token, persisting at most once per minute.
    /// </summary>
    /// <param name="id">The token id.</param>
    /// <param name="now">The current UTC time.</param>
    void TouchLastUsed(Guid id, DateTime now);
}
