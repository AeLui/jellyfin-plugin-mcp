using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Mcp.Mcp;
using Xunit;

namespace Jellyfin.Plugin.Mcp.Tests.Mcp;

public class PromptRegistryTests
{
    private static readonly McpPrincipal LibraryPrincipal = new(Guid.NewGuid(), new HashSet<string> { McpScopes.Library });

    private static PromptRegistry Registry() => new(new IMcpPrompt[]
    {
        new FakePrompt("b", McpScopes.Library),
        new FakePrompt("a", McpScopes.Library),
        new FakePrompt("c", McpScopes.Admin),
    });

    [Fact]
    public void Constructor_RejectsDuplicates()
    {
        var ex = Assert.Throws<ArgumentException>(() => new PromptRegistry(new IMcpPrompt[] { new FakePrompt("a", McpScopes.Library), new FakePrompt("a", McpScopes.Admin) }));

        Assert.StartsWith("Duplicate prompt name: a", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ListFor_FiltersByScope_SortedOrdinal()
    {
        Assert.Equal(new[] { "a", "b" }, Registry().ListFor(LibraryPrincipal).Select(p => p.Name));
    }

    [Fact]
    public void Find_InScope_ReturnsPrompt()
    {
        Assert.Equal("a", Registry().Find("a", LibraryPrincipal)?.Name);
    }

    [Fact]
    public void Find_OutOfScope_ReturnsNull()
    {
        Assert.Null(Registry().Find("c", LibraryPrincipal));
    }

    [Fact]
    public void Find_Unknown_ReturnsNull()
    {
        Assert.Null(Registry().Find("zzz", LibraryPrincipal));
    }
}
