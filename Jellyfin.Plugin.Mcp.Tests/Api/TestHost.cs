using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.Mcp.Api;
using Jellyfin.Plugin.Mcp.Auth;
using Jellyfin.Plugin.Mcp.Configuration;
using Jellyfin.Plugin.Mcp.Mcp;
using Jellyfin.Plugin.Mcp.Tests.Auth;
using Jellyfin.Plugin.Mcp.Tests.Mcp;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Jellyfin.Plugin.Mcp.Tests.Api;

/// <summary>
/// A minimal in-memory host that serves <see cref="McpController"/> with fake Jellyfin services.
/// </summary>
internal sealed class McpTestHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    private McpTestHost(WebApplication app, HttpClient client, PluginConfiguration config, string plaintext, User user)
    {
        _app = app;
        Client = client;
        Config = config;
        Plaintext = plaintext;
        User = user;
    }

    public HttpClient Client { get; }

    public PluginConfiguration Config { get; }

    public string Plaintext { get; }

    public User User { get; }

    public static async Task<McpTestHost> StartAsync(bool pluginReady = true)
    {
        var config = new PluginConfiguration();
        var clock = new FakeClock();
        var user = TestUsers.Create(admin: false);
        var users = new Mock<IUserManager>();
        users.Setup(m => m.GetUserById(user.Id)).Returns(user);

        var store = new TokenStore(() => config, _ => { }, clock);
        var (_, plaintext) = store.Create("test", user.Id, new[] { McpScopes.Library }, null);

        Func<PluginConfiguration> getConfig = pluginReady
            ? () => config
            : () => throw new PluginNotReadyException();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddControllers().AddApplicationPart(typeof(McpController).Assembly);
        builder.Services.AddSingleton<ITokenStore>(store);
        builder.Services.AddSingleton<IClock>(clock);
        builder.Services.AddSingleton(users.Object);
        builder.Services.AddSingleton(new AuthFailureLimiter(clock));
        var settings = new Mock<IPluginSettings>();
        settings.SetupGet(s => s.AllowedOrigins).Returns(() => config.AllowedOrigins);
        builder.Services.AddSingleton(new OriginPolicy(settings.Object));
        builder.Services.AddSingleton(sp => new TokenAuthenticator(
            sp.GetRequiredService<ITokenStore>(),
            sp.GetRequiredService<IUserManager>(),
            sp.GetRequiredService<AuthFailureLimiter>(),
            sp.GetRequiredService<IClock>(),
            getConfig));
        builder.Services.AddSingleton<IMcpTool>(new FakeTool("jellyfin_library_tool", McpScopes.Library));
        builder.Services.AddSingleton<IMcpTool>(new FakeTool("jellyfin_admin_tool", McpScopes.Admin));
        builder.Services.AddScoped<ToolRegistry>();
        builder.Services.AddScoped(sp => new JsonRpcDispatcher(
            sp.GetRequiredService<ToolRegistry>(),
            new PromptRegistry(Array.Empty<IMcpPrompt>()),
            new McpServerInfo("jellyfin-mcp", "1.0.0")));

        var app = builder.Build();
        app.MapControllers();
        await app.StartAsync();
        return new McpTestHost(app, app.GetTestClient(), config, plaintext, user);
    }

    public HttpRequestMessage Post(string json, string? token = "default")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };
        var bearer = token == "default" ? Plaintext : token;
        if (bearer is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }

        return request;
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.DisposeAsync();
    }
}
