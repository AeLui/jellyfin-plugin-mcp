using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Mcp.Mcp;
using Xunit;

namespace Jellyfin.Plugin.Mcp.Tests.Mcp;

public class ToolRegistryTests
{
    private static McpPrincipal PrincipalWith(params string[] scopes) => new(Guid.NewGuid(), new HashSet<string>(scopes));

    [Fact]
    public void ListFor_FiltersByScope()
    {
        var registry = new ToolRegistry(new IMcpTool[]
        {
            new FakeTool("b_admin", McpScopes.Admin),
            new FakeTool("a_library", McpScopes.Library),
        });

        var tools = registry.ListFor(PrincipalWith(McpScopes.Library));

        Assert.Equal("a_library", Assert.Single(tools).Name);
    }

    [Fact]
    public void ListFor_SortsByName()
    {
        var registry = new ToolRegistry(new IMcpTool[]
        {
            new FakeTool("z", McpScopes.Library),
            new FakeTool("m", McpScopes.Library),
            new FakeTool("a", McpScopes.Library),
        });

        Assert.Equal(new[] { "a", "m", "z" }, registry.ListFor(PrincipalWith(McpScopes.Library)).Select(t => t.Name));
    }

    [Fact]
    public void Find_OutOfScope_ReturnsNull()
    {
        var registry = new ToolRegistry(new IMcpTool[] { new FakeTool("admin_tool", McpScopes.Admin) });

        Assert.Null(registry.Find("admin_tool", PrincipalWith(McpScopes.Library)));
        Assert.Null(registry.Find("missing", PrincipalWith(McpScopes.Admin)));
        Assert.NotNull(registry.Find("admin_tool", PrincipalWith(McpScopes.Admin)));
    }

    [Fact]
    public void Constructor_DuplicateNames_Throws()
    {
        var tools = new IMcpTool[] { new FakeTool("dup", McpScopes.Library), new FakeTool("dup", McpScopes.Admin) };

        Assert.Throws<ArgumentException>(() => new ToolRegistry(tools));
    }

    [Fact]
    public void McpScopes_AllContainsExactlyTheFourScopes()
    {
        Assert.Equal(
            new[] { "admin", "library", "manage", "playback" },
            McpScopes.All.OrderBy(s => s, StringComparer.Ordinal));
    }
}
