using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.Mcp.Api;
using Jellyfin.Plugin.Mcp.Auth;
using Jellyfin.Plugin.Mcp.Configuration;
using Jellyfin.Plugin.Mcp.Mcp;
using Jellyfin.Plugin.Mcp.Tests.Auth;
using MediaBrowser.Controller.Library;
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
    private readonly TokenStore _store;
    private readonly TokensController _controller;

    public TokensControllerTests()
    {
        _store = new TokenStore(() => _config, _ => { }, _clock);
        _controller = new TokensController(_store, _users.Object, _clock);
    }

    private User AddUser(bool admin = false)
    {
        var user = TestUsers.Create(admin);
        _users.Setup(u => u.GetUserById(user.Id)).Returns(user);
        return user;
    }

    private static CreateTokenRequest Request(Guid userId, string name = "Claude", string[]? scopes = null, int? days = null)
        => new() { Name = name, UserId = userId, Scopes = scopes ?? new[] { McpScopes.Library }, ExpiresInDays = days };

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
