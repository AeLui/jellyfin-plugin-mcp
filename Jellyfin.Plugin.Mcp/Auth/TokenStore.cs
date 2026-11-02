using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Jellyfin.Plugin.Mcp.Configuration;

namespace Jellyfin.Plugin.Mcp.Auth;

/// <summary>
/// <see cref="ITokenStore"/> backed by the plugin configuration. All mutations share one lock.
/// </summary>
public sealed class TokenStore : ITokenStore
{
    private static readonly TimeSpan TouchInterval = TimeSpan.FromSeconds(60);

    private readonly Func<PluginConfiguration> _getConfig;
    private readonly Action<PluginConfiguration> _saveConfig;
    private readonly IClock _clock;
    private readonly object _lock;

    /// <summary>
    /// Initializes a new instance of the <see cref="TokenStore"/> class.
    /// </summary>
    /// <param name="getConfig">Gets the current configuration.</param>
    /// <param name="saveConfig">Persists the configuration.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="syncRoot">The lock shared with other writers of the configuration, or null for a private one.</param>
    public TokenStore(Func<PluginConfiguration> getConfig, Action<PluginConfiguration> saveConfig, IClock clock, object? syncRoot = null)
    {
        _lock = syncRoot ?? new object();
        _getConfig = getConfig;
        _saveConfig = saveConfig;
        _clock = clock;
    }

    /// <inheritdoc />
    public IReadOnlyList<McpToken> GetAll()
    {
        lock (_lock)
        {
            return _getConfig().Tokens.Select(Clone).ToList();
        }
    }

    /// <inheritdoc />
    public McpToken? FindByHash(string hash)
    {
        lock (_lock)
        {
            var found = _getConfig().Tokens.FirstOrDefault(t => TokenGenerator.HashEquals(t.TokenHash, hash));
            return found is null ? null : Clone(found);
        }
    }

    /// <inheritdoc />
    public (McpToken Token, string Plaintext) Create(string name, Guid userId, IReadOnlyCollection<string> scopes, DateTime? expiresAt)
    {
        var plaintext = TokenGenerator.NewToken();
        var token = new McpToken
        {
            Id = Guid.NewGuid(),
            Name = name,
            UserId = userId,
            Scopes = new Collection<string>(scopes.ToList()),
            TokenHash = TokenGenerator.Hash(plaintext),
            Prefix = TokenGenerator.DisplayPrefix(plaintext),
            CreatedAt = _clock.UtcNow,
            ExpiresAt = expiresAt,
            Enabled = true
        };

        lock (_lock)
        {
            var config = _getConfig();
            config.Tokens.Add(token);
            _saveConfig(config);
        }

        return (Clone(token), plaintext);
    }

    /// <inheritdoc />
    public bool Delete(Guid id)
    {
        lock (_lock)
        {
            var config = _getConfig();
            var existing = config.Tokens.FirstOrDefault(t => t.Id == id);
            if (existing is null)
            {
                return false;
            }

            config.Tokens.Remove(existing);
            _saveConfig(config);
            return true;
        }
    }

    /// <inheritdoc />
    public void TouchLastUsed(Guid id, DateTime now)
    {
        lock (_lock)
        {
            var config = _getConfig();
            var token = config.Tokens.FirstOrDefault(t => t.Id == id);
            if (token is null || (token.LastUsedAt is { } last && now - last < TouchInterval))
            {
                return;
            }

            token.LastUsedAt = now;
            _saveConfig(config);
        }
    }

    private static McpToken Clone(McpToken t) => new()
    {
        Id = t.Id,
        Name = t.Name,
        UserId = t.UserId,
        Scopes = new Collection<string>(t.Scopes.ToList()),
        TokenHash = t.TokenHash,
        Prefix = t.Prefix,
        CreatedAt = t.CreatedAt,
        LastUsedAt = t.LastUsedAt,
        ExpiresAt = t.ExpiresAt,
        Enabled = t.Enabled
    };
}
