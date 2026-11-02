using System;
using System.Linq;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.Mcp.Auth;
using Jellyfin.Plugin.Mcp.Configuration;
using Jellyfin.Plugin.Mcp.Mcp;
using MediaBrowser.Controller.Library;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.Mcp.Tests.Auth;

public class TokenAuthenticatorTests
{
    private const string Ip = "10.0.0.1";

    private readonly FakeClock _clock = new();
    private readonly PluginConfiguration _config = new();
    private readonly Mock<IUserManager> _users = new();
    private readonly TokenStore _store;
    private readonly AuthFailureLimiter _limiter;
    private readonly TokenAuthenticator _auth;

    public TokenAuthenticatorTests()
    {
        _store = new TokenStore(() => _config, _ => { }, _clock);
        _limiter = new AuthFailureLimiter(_clock);
        _auth = new TokenAuthenticator(_store, _users.Object, _limiter, _clock, () => _config);
    }

    private static string Bearer(string plaintext) => "Bearer " + plaintext;

    private (string Plaintext, McpToken Token, User User) Setup(bool admin = false, bool userDisabled = false, params string[] scopes)
    {
        var user = TestUsers.Create(admin, userDisabled);
        _users.Setup(u => u.GetUserById(user.Id)).Returns(user);
        var (token, plaintext) = _store.Create("t", user.Id, scopes, null);
        return (plaintext, token, user);
    }

    [Fact]
    public void MissingHeader_Unauthorized()
    {
        Assert.Equal(AuthStatus.Unauthorized, _auth.Authenticate(null, Ip).Status);
    }

    [Fact]
    public void WrongScheme_Unauthorized()
    {
        var (plaintext, _, _) = Setup(false, false, McpScopes.Library);

        Assert.Equal(AuthStatus.Unauthorized, _auth.Authenticate("Basic " + plaintext, Ip).Status);
        Assert.Equal(AuthStatus.Unauthorized, _auth.Authenticate("Bearer not_a_jmcp_token", Ip).Status);
    }

    [Fact]
    public void UnknownToken_Unauthorized()
    {
        Setup(false, false, McpScopes.Library);

        Assert.Equal(AuthStatus.Unauthorized, _auth.Authenticate(Bearer(TokenGenerator.NewToken()), Ip).Status);
    }

    [Fact]
    public void DisabledToken_Unauthorized()
    {
        var (plaintext, token, _) = Setup(false, false, McpScopes.Library);
        _config.Tokens[0].Enabled = false;

        Assert.Equal(AuthStatus.Unauthorized, _auth.Authenticate(Bearer(plaintext), Ip).Status);
        Assert.NotNull(token);
    }

    [Fact]
    public void ExpiredToken_Unauthorized()
    {
        var (plaintext, _, _) = Setup(false, false, McpScopes.Library);
        _config.Tokens[0].ExpiresAt = _clock.UtcNow;

        Assert.Equal(AuthStatus.Unauthorized, _auth.Authenticate(Bearer(plaintext), Ip).Status);
    }

    [Fact]
    public void DeletedUser_Unauthorized()
    {
        var (plaintext, _, user) = Setup(false, false, McpScopes.Library);
        _users.Setup(u => u.GetUserById(user.Id)).Returns((User?)null);

        Assert.Equal(AuthStatus.Unauthorized, _auth.Authenticate(Bearer(plaintext), Ip).Status);
    }

    [Fact]
    public void DisabledUser_Unauthorized()
    {
        var (plaintext, _, _) = Setup(false, true, McpScopes.Library);

        Assert.Equal(AuthStatus.Unauthorized, _auth.Authenticate(Bearer(plaintext), Ip).Status);
    }

    [Fact]
    public void ValidToken_ReturnsPrincipalWithUserAndScopes()
    {
        var (plaintext, token, user) = Setup(false, false, McpScopes.Library, McpScopes.Playback, "bogus");

        var result = _auth.Authenticate(Bearer(plaintext), Ip);

        Assert.Equal(AuthStatus.Success, result.Status);
        Assert.Equal(user.Id, result.Principal!.UserId);
        Assert.Equal(new[] { McpScopes.Library, McpScopes.Playback }.Order(), result.Principal.Scopes.Order());
        Assert.Equal(token.Id, result.Token!.Id);
        Assert.NotNull(_store.GetAll()[0].LastUsedAt);
    }

    [Fact]
    public void AdminScope_DroppedWhenUserNotAdmin()
    {
        var (plaintext, _, _) = Setup(false, false, McpScopes.Library, McpScopes.Admin);

        var result = _auth.Authenticate(Bearer(plaintext), Ip);

        Assert.DoesNotContain(McpScopes.Admin, result.Principal!.Scopes);
        Assert.Contains(McpScopes.Library, result.Principal.Scopes);
    }

    [Fact]
    public void AdminScope_KeptWhenUserAdmin()
    {
        var (plaintext, _, _) = Setup(true, false, McpScopes.Library, McpScopes.Admin);

        var result = _auth.Authenticate(Bearer(plaintext), Ip);

        Assert.Contains(McpScopes.Admin, result.Principal!.Scopes);
    }

    [Fact]
    public void TokenDeletedBetweenRequests_SecondRequestUnauthorized()
    {
        var (plaintext, token, _) = Setup(false, false, McpScopes.Library);

        Assert.Equal(AuthStatus.Success, _auth.Authenticate(Bearer(plaintext), Ip).Status);
        Assert.True(_store.Delete(token.Id));
        Assert.Equal(AuthStatus.Unauthorized, _auth.Authenticate(Bearer(plaintext), Ip).Status);
    }

    [Fact]
    public void UserDemotedBetweenRequests_LosesAdmin()
    {
        var (plaintext, _, user) = Setup(true, false, McpScopes.Library, McpScopes.Admin);

        Assert.Contains(McpScopes.Admin, _auth.Authenticate(Bearer(plaintext), Ip).Principal!.Scopes);
        user.SetPermission(Jellyfin.Database.Implementations.Enums.PermissionKind.IsAdministrator, false);
        Assert.DoesNotContain(McpScopes.Admin, _auth.Authenticate(Bearer(plaintext), Ip).Principal!.Scopes);
    }

    [Fact]
    public void EndpointDisabled_ReturnsDisabled()
    {
        var (plaintext, _, _) = Setup(false, false, McpScopes.Library);
        _config.Enabled = false;

        Assert.Equal(AuthStatus.Disabled, _auth.Authenticate(Bearer(plaintext), Ip).Status);
    }

    [Fact]
    public void ElevenFailures_BlocksIp()
    {
        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(AuthStatus.Unauthorized, _auth.Authenticate(null, Ip).Status);
        }

        Assert.Equal(AuthStatus.TooManyRequests, _auth.Authenticate(null, Ip).Status);
        Assert.Equal(AuthStatus.Unauthorized, _auth.Authenticate(null, "10.0.0.2").Status);

        _clock.Advance(TimeSpan.FromSeconds(61));
        Assert.Equal(AuthStatus.Unauthorized, _auth.Authenticate(null, Ip).Status);
    }

    [Fact]
    public void UnknownTokens_CountAsFailures_AndBlockIp()
    {
        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(AuthStatus.Unauthorized, _auth.Authenticate(Bearer(TokenGenerator.NewToken()), Ip).Status);
        }

        Assert.Equal(AuthStatus.TooManyRequests, _auth.Authenticate(Bearer(TokenGenerator.NewToken()), Ip).Status);
    }

    [Fact]
    public void DisabledAndExpiredTokens_CountAsFailures()
    {
        var (disabled, _, _) = Setup(false, false, McpScopes.Library);
        var (expired, _, _) = Setup(false, false, McpScopes.Library);
        _config.Tokens[0].Enabled = false;
        _config.Tokens[1].ExpiresAt = _clock.UtcNow;

        for (var i = 0; i < 5; i++)
        {
            _auth.Authenticate(Bearer(disabled), Ip);
            _auth.Authenticate(Bearer(expired), Ip);
        }

        Assert.Equal(AuthStatus.TooManyRequests, _auth.Authenticate(Bearer(disabled), Ip).Status);
    }

    [Fact]
    public void DeletedUser_DoesNotCountAsFailure()
    {
        var (plaintext, _, user) = Setup(false, false, McpScopes.Library);
        _users.Setup(u => u.GetUserById(user.Id)).Returns((User?)null);

        for (var i = 0; i < 11; i++)
        {
            Assert.Equal(AuthStatus.Unauthorized, _auth.Authenticate(Bearer(plaintext), Ip).Status);
        }

        Assert.False(_limiter.IsBlocked(Ip));
    }
}
