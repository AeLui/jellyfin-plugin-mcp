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
        new PluginServiceRegistrator().RegisterServices(services, new Mock<IServerApplicationHost>().Object);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ToolRegistry>());
        Assert.Contains(scope.ServiceProvider.GetServices<IMcpTool>(), t => t.Name == "jellyfin_get_item");
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<JsonRpcDispatcher>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<TokenAuthenticator>());
        Assert.Same(provider.GetRequiredService<ITokenStore>(), provider.GetRequiredService<ITokenStore>());
        Assert.Same(provider.GetRequiredService<AuthFailureLimiter>(), provider.GetRequiredService<AuthFailureLimiter>());
        Assert.Same(provider.GetRequiredService<TokenAuthenticator>(), provider.GetRequiredService<TokenAuthenticator>());
    }
}
