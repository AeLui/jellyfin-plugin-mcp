using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.Mcp.Auth;
using Jellyfin.Plugin.Mcp.Configuration;
using Jellyfin.Plugin.Mcp.Mcp;
using Jellyfin.Plugin.Mcp.Tools;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Entities;
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
    private readonly ILibraryManager _libraryManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="TokensController"/> class.
    /// </summary>
    /// <param name="store">The token store.</param>
    /// <param name="users">The user manager.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="libraryManager">The library manager.</param>
    public TokensController(ITokenStore store, IUserManager users, IClock clock, ILibraryManager libraryManager)
    {
        _store = store;
        _users = users;
        _clock = clock;
        _libraryManager = libraryManager;
    }

    /// <summary>
    /// Lists all tokens. Neither the secret nor its hash is returned.
    /// </summary>
    /// <returns>The tokens.</returns>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<TokenDto[]> List()
    {
        var names = LibraryNames();
        return _store.GetAll().Select(t => ToDto(t, names)).ToArray();
    }

    /// <summary>
    /// Lists the libraries a token for a user can be limited to: the user's media libraries.
    /// </summary>
    /// <param name="userId">The id of the user the token will act as.</param>
    /// <returns>The libraries, or not found for an unknown user.</returns>
    [HttpGet("Libraries")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<TokenLibraryDto[]> Libraries([FromQuery] Guid userId)
    {
        var user = _users.GetUserById(userId);
        if (user is null)
        {
            return NotFound();
        }

        return LibraryRestriction.GetSelectableLibraries(_libraryManager, user).Select(ToLibraryDto).ToArray();
    }

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

        var libraryIds = (request.LibraryIds ?? Array.Empty<Guid>()).Distinct().ToList();
        if (libraryIds.Count > 0)
        {
            // Admin tools act on the whole server (all sessions, users, tasks, library scans), so they cannot be limited.
            if (scopes.Contains(McpScopes.Admin))
            {
                return BadRequest("The admin scope cannot be combined with a library restriction: admin tools act on the whole server.");
            }

            var selectable = LibraryRestriction.GetSelectableLibraries(_libraryManager, user).Select(l => l.Id).ToHashSet();
            var invalid = libraryIds.Where(id => !selectable.Contains(id)).ToList();
            if (invalid.Count > 0)
            {
                return BadRequest($"Library '{invalid[0]}' is not one of the user's media libraries.");
            }
        }

        if (request.ExpiresInDays is { } days && !AllowedExpiryDays.Contains(days))
        {
            return BadRequest("ExpiresInDays must be 30, 90, 365 or null.");
        }

        DateTime? expiresAt = request.ExpiresInDays is { } lifetime ? _clock.UtcNow.AddDays(lifetime) : null;
        var (token, plaintext) = _store.Create(name, user.Id, scopes, expiresAt, libraryIds);

        return StatusCode(
            StatusCodes.Status201Created,
            new CreatedTokenDto { Token = ToDto(token, LibraryNames()), Plaintext = plaintext });
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

    private static TokenLibraryDto ToLibraryDto(Folder folder) => new()
    {
        Id = folder.Id,
        Name = folder.Name ?? string.Empty,
        CollectionType = (folder as ICollectionFolder)?.CollectionType?.ToString().ToLowerInvariant(),
    };

    // Display names of every library on the server (the list behind /Library/VirtualFolders), for the token table only.
    private Dictionary<Guid, string> LibraryNames()
    {
        var names = new Dictionary<Guid, string>();
        foreach (var folder in _libraryManager.GetVirtualFolders())
        {
            if (Guid.TryParse(folder.ItemId, out var id))
            {
                names[id] = folder.Name ?? string.Empty;
            }
        }

        return names;
    }

    private TokenDto ToDto(McpToken token, IReadOnlyDictionary<Guid, string> libraryNames)
    {
        var libraryIds = (token.LibraryIds ?? []).ToList();
        return new()
        {
            Id = token.Id,
            Name = token.Name,
            UserId = token.UserId,
            UserName = _users.GetUserById(token.UserId)?.Username ?? string.Empty,
            Scopes = token.Scopes.ToList(),
            LibraryIds = libraryIds,
            Libraries = libraryIds.Select(id => new TokenLibraryDto { Id = id, Name = libraryNames.GetValueOrDefault(id, string.Empty) }).ToList(),
            Prefix = token.Prefix,
            CreatedAt = ToUtc(token.CreatedAt),
            LastUsedAt = token.LastUsedAt is { } used ? ToUtc(used) : null,
            ExpiresAt = token.ExpiresAt is { } expires ? ToUtc(expires) : null,
            Enabled = token.Enabled,
        };
    }
}
