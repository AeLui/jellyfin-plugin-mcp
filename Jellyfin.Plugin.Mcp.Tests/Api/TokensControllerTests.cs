using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.Mcp.Api;
using Jellyfin.Plugin.Mcp.Auth;
using Jellyfin.Plugin.Mcp.Configuration;
using Jellyfin.Plugin.Mcp.Mcp;
using Jellyfin.Plugin.Mcp.Tests.Auth;
using Jellyfin.Plugin.Mcp.Tests.Tools;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.Mcp.Tests.Api;

public class TokensControllerTests
{
    private readonly FakeClock _clock = new();
    private readonly PluginConfiguration _config = new();
    private readonly Mock<IUserManager> _users = new();
    private readonly Mock<ILibraryManager> _library = new();
    private readonly CollectionFolder _movies = new() { Id = Guid.NewGuid(), Name = "Movies", CollectionType = CollectionType.movies };
    private readonly CollectionFolder _kids = new() { Id = Guid.NewGuid(), Name = "Kids", CollectionType = CollectionType.movies };
    private readonly CollectionFolder _collections = new() { Id = Guid.NewGuid(), Name = "Collections", CollectionType = CollectionType.boxsets };
    private readonly RestrictedWorld.RootFolder _root = new();
    private readonly TokenStore _store;
    private readonly TokensController _controller;

    public TokensControllerTests()
    {
        _root.Libraries = [_movies, _kids, _collections];
        _library.Setup(l => l.GetUserRootFolder()).Returns(_root);
        _library.Setup(l => l.GetVirtualFolders()).Returns(
            new[] { _movies, _kids, _collections }.Select(f => new VirtualFolderInfo { ItemId = f.Id.ToString("N"), Name = f.Name }).ToList());
        _store = new TokenStore(() => _config, _ => { }, _clock);
        _controller = new TokensController(_store, _users.Object, _clock, _library.Object);
    }

    private User AddUser(bool admin = false)
    {
        var user = TestUsers.Create(admin);
        _users.Setup(u => u.GetUserById(user.Id)).Returns(user);
        return user;
    }

    private static CreateTokenRequest Request(Guid userId, string name = "Claude", string[]? scopes = null, int? days = null, Guid[]? libraries = null)
        => new() { Name = name, UserId = userId, Scopes = scopes ?? new[] { McpScopes.Library }, ExpiresInDays = days, LibraryIds = libraries };

    [Fact]
    public void Create_WithLibraries_StoresAndListsThem()
    {
        var user = AddUser();

        var result = _controller.Create(Request(user.Id, libraries: new[] { _movies.Id, _movies.Id }));

        var dto = Assert.IsType<CreatedTokenDto>(Assert.IsType<ObjectResult>(result.Result).Value);
        Assert.Equal(new[] { _movies.Id }, dto.Token.LibraryIds);
        Assert.Equal(new[] { _movies.Id }, _store.GetAll().Single().LibraryIds);
        var listed = Assert.Single(Assert.IsType<TokenDto[]>(_controller.List().Value));
        var library = Assert.Single(listed.Libraries);
        Assert.Equal(_movies.Id, library.Id);
        Assert.Equal("Movies", library.Name);
    }

    [Fact]
    public void Create_WithoutLibraries_Unrestricted()
    {
        var user = AddUser();

        var dto = Assert.IsType<CreatedTokenDto>(Assert.IsType<ObjectResult>(_controller.Create(Request(user.Id)).Result).Value);

        Assert.Empty(dto.Token.LibraryIds);
        Assert.Empty(dto.Token.Libraries);
    }

    [Fact]
    public void Create_LibraryUserCannotSee_BadRequest()
    {
        var user = AddUser();
        _root.Libraries = [_movies];

        var result = _controller.Create(Request(user.Id, libraries: new[] { _movies.Id, _kids.Id }));

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains(_kids.Id.ToString(), Assert.IsType<string>(bad.Value), StringComparison.Ordinal);
        Assert.Empty(_store.GetAll());
    }

    [Fact]
    public void Create_UnknownOrEmptyLibraryId_BadRequest()
    {
        var user = AddUser();

        Assert.IsType<BadRequestObjectResult>(_controller.Create(Request(user.Id, libraries: new[] { Guid.NewGuid() })).Result);
        Assert.IsType<BadRequestObjectResult>(_controller.Create(Request(user.Id, libraries: new[] { Guid.Empty })).Result);
        Assert.Empty(_store.GetAll());
    }

    [Fact]
    public void Create_CollectionsLibrary_BadRequest()
    {
        var user = AddUser();

        Assert.IsType<BadRequestObjectResult>(_controller.Create(Request(user.Id, libraries: new[] { _collections.Id })).Result);
        Assert.Empty(_store.GetAll());
    }

    [Fact]
    public void Create_AdminScopeWithLibraries_BadRequest()
    {
        var admin = AddUser(admin: true);

        var result = _controller.Create(Request(admin.Id, scopes: new[] { McpScopes.Library, McpScopes.Admin }, libraries: new[] { _movies.Id }));

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("admin", Assert.IsType<string>(bad.Value), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_store.GetAll());
    }

    [Fact]
    public void Libraries_ListsTheUsersMediaLibraries()
    {
        var user = AddUser();

        var libraries = Assert.IsType<TokenLibraryDto[]>(_controller.Libraries(user.Id).Value);

        Assert.Equal(new[] { "Movies", "Kids" }, libraries.Select(l => l.Name));
        Assert.Equal(new[] { _movies.Id, _kids.Id }, libraries.Select(l => l.Id));
        Assert.Equal("movies", libraries[0].CollectionType);
    }

    [Fact]
    public void Libraries_UnknownUser_NotFound()
    {
        Assert.IsType<NotFoundResult>(_controller.Libraries(Guid.NewGuid()).Result);
    }

    [Fact]
    public void Create_ValidRequest_ReturnsPlaintextOnce()
    {
        var user = AddUser();

        var result = _controller.Create(Request(user.Id, scopes: new[] { McpScopes.Library, McpScopes.Library, McpScopes.Playback }, days: 30));

        var created = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(201, created.StatusCode);
        var dto = Assert.IsType<CreatedTokenDto>(created.Value);
        Assert.StartsWith("jmcp_", dto.Plaintext, StringComparison.Ordinal);
        Assert.Equal(new[] { "library", "playback" }, dto.Token.Scopes);
        Assert.Equal(user.Username, dto.Token.UserName);
        Assert.Equal(_clock.UtcNow.AddDays(30), dto.Token.ExpiresAt);
        Assert.Equal(DateTimeKind.Utc, dto.Token.ExpiresAt!.Value.Kind);

        var listed = Assert.IsType<TokenDto[]>(_controller.List().Value);
        var only = Assert.Single(listed);
        Assert.Equal(dto.Token.Id, only.Id);
        var json = JsonSerializer.Serialize(listed);
        Assert.DoesNotContain(dto.Plaintext, json, StringComparison.Ordinal);
        Assert.DoesNotContain(_store.GetAll().Single().TokenHash, json, StringComparison.Ordinal);
        Assert.DoesNotContain("Plaintext", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Hash", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_AdminScopeForAdmin_Created()
    {
        var admin = AddUser(admin: true);

        var result = _controller.Create(Request(admin.Id, scopes: new[] { McpScopes.Admin }));

        Assert.Equal(201, Assert.IsType<ObjectResult>(result.Result).StatusCode);
    }

    [Fact]
    public void Create_AdminScopeForNonAdmin_BadRequest()
    {
        var user = AddUser(admin: false);

        var result = _controller.Create(Request(user.Id, scopes: new[] { McpScopes.Library, McpScopes.Admin }));

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(_store.GetAll());
    }

    [Theory]
    [InlineData("bogus")]
    [InlineData("Library")]
    [InlineData("")]
    public void Create_UnknownScope_BadRequest(string scope)
    {
        var user = AddUser();

        var result = _controller.Create(Request(user.Id, scopes: new[] { McpScopes.Library, scope }));

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(_store.GetAll());
    }

    [Fact]
    public void Create_NoScopes_BadRequest()
    {
        var user = AddUser();

        var result = _controller.Create(Request(user.Id, scopes: Array.Empty<string>()));

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public void Create_UnknownUser_BadRequest()
    {
        var result = _controller.Create(Request(Guid.NewGuid()));

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(_store.GetAll());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(60)]
    [InlineData(-30)]
    public void Create_InvalidExpiry_BadRequest(int days)
    {
        var user = AddUser();

        var result = _controller.Create(Request(user.Id, days: days));

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(_store.GetAll());
    }

    [Theory]
    [InlineData(30)]
    [InlineData(90)]
    [InlineData(365)]
    public void Create_AllowedExpiry_UsesStoreClock(int days)
    {
        var user = AddUser();

        var result = _controller.Create(Request(user.Id, days: days));

        var dto = Assert.IsType<CreatedTokenDto>(Assert.IsType<ObjectResult>(result.Result).Value);
        Assert.Equal(_clock.UtcNow.AddDays(days), dto.Token.ExpiresAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_EmptyName_BadRequest(string name)
    {
        var user = AddUser();

        Assert.IsType<BadRequestObjectResult>(_controller.Create(Request(user.Id, name: name)).Result);
    }

    [Fact]
    public void Create_NameLength_Boundary()
    {
        var user = AddUser();

        Assert.Equal(201, Assert.IsType<ObjectResult>(_controller.Create(Request(user.Id, name: new string('a', 100))).Result).StatusCode);
        Assert.IsType<BadRequestObjectResult>(_controller.Create(Request(user.Id, name: new string('a', 101))).Result);
    }

    [Fact]
    public void Delete_Existing_NoContent()
    {
        var user = AddUser();
        var created = Assert.IsType<CreatedTokenDto>(Assert.IsType<ObjectResult>(_controller.Create(Request(user.Id)).Result).Value);

        Assert.IsType<NoContentResult>(_controller.Delete(created.Token.Id));
        Assert.Empty(_store.GetAll());
    }

    [Fact]
    public void Delete_Unknown_NotFound()
    {
        Assert.IsType<NotFoundResult>(_controller.Delete(Guid.NewGuid()));
    }

    [Fact]
    public void List_UnspecifiedDateKind_EmittedAsUtc()
    {
        var user = AddUser();
        _config.Tokens.Add(new McpToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified),
        });

        var dto = Assert.Single(Assert.IsType<TokenDto[]>(_controller.List().Value));

        Assert.Equal(DateTimeKind.Utc, dto.CreatedAt.Kind);
        Assert.Contains("2026-01-01T00:00:00Z", JsonSerializer.Serialize(dto), StringComparison.Ordinal);
    }

    [Fact]
    public void Controller_RequiresElevation()
    {
        var attribute = typeof(TokensController).GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal("RequiresElevation", attribute.Policy);
        Assert.DoesNotContain(typeof(TokensController).GetMethods(), m => m.GetCustomAttribute<AllowAnonymousAttribute>() is not null);
    }
}
