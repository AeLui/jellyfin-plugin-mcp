using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.Mcp.Configuration;
using Jellyfin.Plugin.Mcp.Mcp;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Mcp.Auth;

/// <summary>
/// Authenticates bearer tokens on every request; nothing is cached between requests.
/// </summary>
public sealed class TokenAuthenticator
{
    private const string BearerPrefix = "Bearer ";

    private readonly ITokenStore _store;
    private readonly IUserManager _users;
    private readonly AuthFailureLimiter _limiter;
    private readonly IClock _clock;
    private readonly Func<PluginConfiguration> _getConfig;

    /// <summary>
    /// Initializes a new instance of the <see cref="TokenAuthenticator"/> class.
    /// </summary>
    /// <param name="store">The token store.</param>
    /// <param name="users">The user manager.</param>
    /// <param name="limiter">The failure limiter.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="getConfig">Gets the current configuration.</param>
    public TokenAuthenticator(ITokenStore store, IUserManager users, AuthFailureLimiter limiter, IClock clock, Func<PluginConfiguration> getConfig)
    {
        _store = store;
        _users = users;
        _limiter = limiter;
        _clock = clock;
        _getConfig = getConfig;
    }

    /// <summary>
    /// Authenticates a request.
    /// </summary>
    /// <param name="authorizationHeader">The Authorization header value, if any.</param>
    /// <param name="remoteIp">The remote address, used for rate limiting.</param>
    /// <returns>The result.</returns>
    public AuthResult Authenticate(string? authorizationHeader, string remoteIp)
    {
        if (!_getConfig().Enabled)
        {
            return Fail(AuthStatus.Disabled);
        }

        if (_limiter.IsBlocked(remoteIp))
        {
            return Fail(AuthStatus.TooManyRequests);
        }

        if (authorizationHeader is null
            || !authorizationHeader.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return Reject(remoteIp);
        }

        var plaintext = authorizationHeader[BearerPrefix.Length..].Trim();
        if (!plaintext.StartsWith(TokenGenerator.TokenPrefix, StringComparison.Ordinal))
        {
            return Reject(remoteIp);
        }

        var token = _store.FindByHash(TokenGenerator.Hash(plaintext));
        var now = _clock.UtcNow;
        if (token is null || !token.Enabled || (token.ExpiresAt is { } expiry && expiry <= now))
        {
            return Reject(remoteIp);
        }

        var user = _users.GetUserById(token.UserId);
        if (user is null || user.HasPermission(PermissionKind.IsDisabled))
        {
            return Fail(AuthStatus.Unauthorized);
        }

        var scopes = new HashSet<string>(token.Scopes.Where(McpScopes.All.Contains), StringComparer.Ordinal);
        if (!user.HasPermission(PermissionKind.IsAdministrator))
        {
            scopes.Remove(McpScopes.Admin);
        }

        _store.TouchLastUsed(token.Id, now);
        return new AuthResult(AuthStatus.Success, new McpPrincipal(user.Id, scopes), token);
    }

    private static AuthResult Fail(AuthStatus status) => new(status, null, null);

    private AuthResult Reject(string remoteIp)
    {
        _limiter.RecordFailure(remoteIp);
        return Fail(AuthStatus.Unauthorized);
    }
}
