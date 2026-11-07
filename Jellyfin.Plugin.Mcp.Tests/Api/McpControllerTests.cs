using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Plugin.Mcp.Auth;
using Jellyfin.Plugin.Mcp.Mcp;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.Mcp.Tests.Api;

public class McpControllerTests
{
    private const string Ping = """{"jsonrpc":"2.0","id":1,"method":"ping"}""";

    [Fact]
    public async Task Get_Returns405()
    {
        await using var host = await McpTestHost.StartAsync();

        var response = await host.Client.GetAsync("/mcp");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal("POST", response.Content.Headers.Allow.Single());
    }

    [Fact]
    public async Task Delete_Returns405()
    {
        await using var host = await McpTestHost.StartAsync();

        var response = await host.Client.DeleteAsync("/mcp");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task Post_NoAuth_Returns401WithChallenge()
    {
        await using var host = await McpTestHost.StartAsync();

        var response = await host.Client.SendAsync(host.Post(Ping, token: null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer realm=\"jellyfin-mcp\"", response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task Post_Disabled_Returns503()
    {
        await using var host = await McpTestHost.StartAsync();
        host.Config.Enabled = false;

        var response = await host.Client.SendAsync(host.Post(Ping));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Post_RateLimited_Returns429()
    {
        await using var host = await McpTestHost.StartAsync();
        for (var i = 0; i < 10; i++)
        {
            var bad = await host.Client.SendAsync(host.Post(Ping, token: "jmcp_wrong"));
            Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);
        }

        var response = await host.Client.SendAsync(host.Post(Ping));

        Assert.Equal((HttpStatusCode)429, response.StatusCode);
        Assert.Equal("60", response.Headers.GetValues("Retry-After").Single());
    }

    [Fact]
    public async Task Post_TooLargeBody_Returns413()
    {
        await using var host = await McpTestHost.StartAsync();
        var body = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\",\"params\":{\"x\":\"" + new string('a', 1_048_576) + "\"}}";

        var response = await host.Client.SendAsync(host.Post(body));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task Post_InitializeThenToolsList_EndToEnd()
    {
        await using var host = await McpTestHost.StartAsync();

        var init = await host.Client.SendAsync(host.Post("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18"}}"""));
        Assert.Equal(HttpStatusCode.OK, init.StatusCode);
        Assert.StartsWith("application/json", init.Content.Headers.ContentType!.ToString(), StringComparison.Ordinal);
        using var initDoc = JsonDocument.Parse(await init.Content.ReadAsStringAsync());
        Assert.Equal("jellyfin-mcp", initDoc.RootElement.GetProperty("result").GetProperty("serverInfo").GetProperty("name").GetString());

        var list = await host.Client.SendAsync(host.Post("""{"jsonrpc":"2.0","id":2,"method":"tools/list"}"""));
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var listDoc = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        var names = listDoc.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray()
            .Select(t => t.GetProperty("name").GetString()).ToList();
        Assert.Equal(new[] { "jellyfin_library_tool" }, names);
    }

    [Fact]
    public async Task Post_Notification_Returns202()
    {
        await using var host = await McpTestHost.StartAsync();

        var response = await host.Client.SendAsync(host.Post("""{"jsonrpc":"2.0","method":"notifications/initialized"}"""));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Post_Responses_CarryExplicitContentLength()
    {
        await using var host = await McpTestHost.StartAsync();

        var ping = await host.Client.SendAsync(host.Post(Ping));
        var body = await ping.Content.ReadAsByteArrayAsync();
        Assert.Equal(body.Length, ping.Content.Headers.ContentLength);
        Assert.NotEqual(true, ping.Headers.TransferEncodingChunked);
        Assert.Equal("application/json; charset=utf-8", ping.Content.Headers.ContentType!.ToString());

        var notification = await host.Client.SendAsync(host.Post("""{"jsonrpc":"2.0","method":"notifications/initialized"}"""));
        Assert.Equal(0, notification.Content.Headers.ContentLength);
        Assert.NotEqual(true, notification.Headers.TransferEncodingChunked);
    }

    [Fact]
    public async Task Post_WrongContentType_Returns415()
    {
        await using var host = await McpTestHost.StartAsync();
        var request = host.Post(Ping);
        request.Content = new StringContent(Ping, Encoding.UTF8, "text/plain");

        var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task Post_PluginNotReady_Returns503()
    {
        await using var host = await McpTestHost.StartAsync(pluginReady: false);

        var response = await host.Client.SendAsync(host.Post(Ping));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public void Registrator_ResolvesAllTools()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new Mock<IUserManager>().Object);
        services.AddSingleton(new Mock<ILibraryManager>().Object);
        services.AddSingleton(new Mock<IUserDataManager>().Object);
        services.AddSingleton(new Mock<IUserViewManager>().Object);
        services.AddSingleton(new Mock<MediaBrowser.Controller.TV.ITVSeriesManager>().Object);
        services.AddSingleton(new Mock<MediaBrowser.Controller.Session.ISessionManager>().Object);
        services.AddSingleton(new Mock<MediaBrowser.Controller.Playlists.IPlaylistManager>().Object);
        services.AddSingleton(new Mock<MediaBrowser.Controller.Providers.IProviderManager>().Object);
        services.AddSingleton(new Mock<MediaBrowser.Model.IO.IFileSystem>().Object);
        services.AddSingleton(new Mock<MediaBrowser.Model.Tasks.ITaskManager>().Object);
        services.AddSingleton(new Mock<ISimilarItemsManager>().Object);
        new PluginServiceRegistrator().RegisterServices(services, new Mock<IServerApplicationHost>().Object);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ToolRegistry>());
        Assert.Contains(scope.ServiceProvider.GetServices<IMcpTool>(), t => t.Name == "jellyfin_get_item");
        Assert.Contains(scope.ServiceProvider.GetServices<IMcpTool>(), t => t.Name == "jellyfin_next_up");
        Assert.Contains(scope.ServiceProvider.GetServices<IMcpTool>(), t => t.Name == "jellyfin_manage_playlist");
        Assert.Contains(scope.ServiceProvider.GetServices<IMcpTool>(), t => t.Name == "jellyfin_list_playlists");
        Assert.Contains(scope.ServiceProvider.GetServices<IMcpTool>(), t => t.Name == "jellyfin_list_users");
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<JsonRpcDispatcher>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<TokenAuthenticator>());
        Assert.Same(provider.GetRequiredService<ITokenStore>(), provider.GetRequiredService<ITokenStore>());
        Assert.Same(provider.GetRequiredService<AuthFailureLimiter>(), provider.GetRequiredService<AuthFailureLimiter>());
        Assert.Same(provider.GetRequiredService<TokenAuthenticator>(), provider.GetRequiredService<TokenAuthenticator>());
    }

    [Fact]
    public void Registrator_RegistersExactly24Tools()
    {
        var services = new ServiceCollection();
        new PluginServiceRegistrator().RegisterServices(services, new Mock<IServerApplicationHost>().Object);

        var tools = services.Where(d => d.ServiceType == typeof(IMcpTool)).Select(d => d.ImplementationType!).ToList();

        Assert.Equal(24, tools.Count);
        Assert.Equal(24, tools.Distinct().Count());
    }

    [Fact]
    public void Registrator_EveryToolHasTitleAndObjectSchemas()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new Mock<IUserManager>().Object);
        services.AddSingleton(new Mock<ILibraryManager>().Object);
        services.AddSingleton(new Mock<IUserDataManager>().Object);
        services.AddSingleton(new Mock<IUserViewManager>().Object);
        services.AddSingleton(new Mock<MediaBrowser.Controller.TV.ITVSeriesManager>().Object);
        services.AddSingleton(new Mock<MediaBrowser.Controller.Session.ISessionManager>().Object);
        services.AddSingleton(new Mock<MediaBrowser.Controller.Playlists.IPlaylistManager>().Object);
        services.AddSingleton(new Mock<MediaBrowser.Controller.Providers.IProviderManager>().Object);
        services.AddSingleton(new Mock<MediaBrowser.Model.IO.IFileSystem>().Object);
        services.AddSingleton(new Mock<MediaBrowser.Model.Tasks.ITaskManager>().Object);
        services.AddSingleton(new Mock<ISimilarItemsManager>().Object);
        new PluginServiceRegistrator().RegisterServices(services, new Mock<IServerApplicationHost>().Object);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var tools = scope.ServiceProvider.GetServices<IMcpTool>().ToList();

        Assert.Equal(24, tools.Count);
        Assert.Equal(24, tools.Select(t => t.Title).Distinct(StringComparer.Ordinal).Count());
        foreach (var tool in tools)
        {
            Assert.False(string.IsNullOrWhiteSpace(tool.Title), tool.Name);
            Assert.Equal("object", tool.InputSchema.GetProperty("type").GetString());
            Assert.True(tool.OutputSchema.HasValue, tool.Name + " has no output schema");
            Assert.Equal("object", tool.OutputSchema!.Value.GetProperty("type").GetString());
        }
    }

    private const string StatelessMeta = "\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/clientCapabilities\":{}}";

    [Fact]
    public async Task Post_StatelessToolsList_EndToEnd()
    {
        await using var host = await McpTestHost.StartAsync();
        var request = host.Post("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\",\"params\":{" + StatelessMeta + "}}");
        request.Headers.Add("MCP-Protocol-Version", "2026-07-28");
        request.Headers.Add("Mcp-Method", "tools/list");

        var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("complete", doc.RootElement.GetProperty("result").GetProperty("resultType").GetString());
    }

    [Fact]
    public async Task Post_StatelessMismatch_Returns400()
    {
        await using var host = await McpTestHost.StartAsync();
        var request = host.Post("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\",\"params\":{" + StatelessMeta + "}}");
        request.Headers.Add("MCP-Protocol-Version", "2026-07-28");
        request.Headers.Add("Mcp-Method", "tools/call");

        var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(-32020, doc.RootElement.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Post_ForeignOrigin_Returns403BeforeAuth()
    {
        await using var host = await McpTestHost.StartAsync();
        var request = host.Post(Ping, token: null);
        request.Headers.Add("Origin", "https://evil.example");

        var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Post_NoOrigin_Works()
    {
        await using var host = await McpTestHost.StartAsync();

        var response = await host.Client.SendAsync(host.Post(Ping));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Post_SameHostOrListedOrigin_Works()
    {
        await using var host = await McpTestHost.StartAsync();
        var sameHost = host.Post(Ping);
        sameHost.Headers.Add("Origin", "https://localhost:8920");
        host.Config.AllowedOrigins.Add("https://inspector.example");
        var listed = host.Post(Ping);
        listed.Headers.Add("Origin", "https://inspector.example");

        Assert.Equal(HttpStatusCode.OK, (await host.Client.SendAsync(sameHost)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.SendAsync(listed)).StatusCode);
    }
}
