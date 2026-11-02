using System;
using System.Linq;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.Mcp.Auth;
using Jellyfin.Plugin.Mcp.Configuration;
using Jellyfin.Plugin.Mcp.Mcp;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Mcp.Api;

/// <summary>
/// Administrator API for managing MCP access tokens.
/// </summary>
[ApiController]
[Route("McpServer/Tokens")]
[Authorize(Policy = Policies.RequiresElevation)]
public sealed class TokensController : ControllerBase
{
    private const int MaxNameLength = 100;

    private static readonly int[] AllowedExpiryDays = { 30, 90, 365 };

    private readonly ITokenStore _store;
    private readonly IUserManager _users;
    private readonly IClock _clock;

    /// <summary>
    /// Initializes a new instance of the <see cref="TokensController"/> class.
    /// </summary>
    /// <param name="store">The token store.</param>
    /// <param name="users">The user manager.</param>
    /// <param name="clock">The clock.</param>
    public TokensController(ITokenStore store, IUserManager users, IClock clock)
    {
        _store = store;
        _users = users;
        _clock = clock;
    }

    /// <summary>
    /// Lists all tokens. Neither the secret nor its hash is returned.
    /// </summary>
    /// <returns>The tokens.</returns>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<TokenDto[]> List()
        => _store.GetAll().Select(ToDto).ToArray();

    /// <summary>
    /// Creates a token. The plaintext is returned only in this response.
    /// </summary>
    /// <param name="request">The token to create.</param>
    /// <returns>The created token and its plaintext.</returns>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<CreatedTokenDto> Create([FromBody] CreateTokenRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length == 0 || name.Length > MaxNameLength)
        {
            return BadRequest("Name must be between 1 and 100 characters.");
        }

        var user = _users.GetUserById(request.UserId);
        if (user is null)
        {
            return BadRequest("Unknown user.");
        }

        var requested = request.Scopes;
        if (requested is null || requested.Count == 0)
        {
            return BadRequest("At least one scope is required.");
        }

        var unknown = requested.FirstOrDefault(s => s is null || !McpScopes.All.Contains(s));
        if (unknown is not null || requested.Any(s => s is null))
        {
            return BadRequest($"Unknown scope '{unknown}'. Valid scopes: {string.Join(", ", McpScopes.All.Order(StringComparer.Ordinal))}.");
        }

        var scopes = requested.Distinct(StringComparer.Ordinal).ToList();
        if (scopes.Contains(McpScopes.Admin) && !user.HasPermission(PermissionKind.IsAdministrator))
        {
            return BadRequest("The admin scope can only be granted to an administrator.");
        }

        if (request.ExpiresInDays is { } days && !AllowedExpiryDays.Contains(days))
        {
            return BadRequest("ExpiresInDays must be 30, 90, 365 or null.");
        }

        DateTime? expiresAt = request.ExpiresInDays is { } lifetime ? _clock.UtcNow.AddDays(lifetime) : null;
        var (token, plaintext) = _store.Create(name, user.Id, scopes, expiresAt);

        return StatusCode(
            StatusCodes.Status201Created,
            new CreatedTokenDto { Token = ToDto(token), Plaintext = plaintext });
    }

    /// <summary>
    /// Deletes a token.
    /// </summary>
    /// <param name="id">The token id.</param>
    /// <returns>No content, or not found.</returns>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult Delete([FromRoute] Guid id)
        => _store.Delete(id) ? NoContent() : NotFound();

    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    private TokenDto ToDto(McpToken token) => new()
    {
        Id = token.Id,
        Name = token.Name,
        UserId = token.UserId,
        UserName = _users.GetUserById(token.UserId)?.Username ?? string.Empty,
        Scopes = token.Scopes.ToList(),
        Prefix = token.Prefix,
        CreatedAt = ToUtc(token.CreatedAt),
        LastUsedAt = token.LastUsedAt is { } used ? ToUtc(used) : null,
        ExpiresAt = token.ExpiresAt is { } expires ? ToUtc(expires) : null,
        Enabled = token.Enabled,
    };
}
