using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.Mcp.Mcp;
using Jellyfin.Plugin.Mcp.Tests.Api;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Jellyfin.Plugin.Mcp.Tests.Prompts;

public partial class PromptToolConsistencyTests
{
    private static readonly HashSet<string> PlaybackOfferTools = new(StringComparer.Ordinal) { "jellyfin_list_sessions", "jellyfin_play" };

    [GeneratedRegex("jellyfin_[a-z_]+")]
    private static partial Regex ToolName();

    [Fact]
    public void EveryReferencedToolExistsInAllowedScope()
    {
        using var provider = RegisteredServices.Build();
        using var scope = provider.CreateScope();
        var tools = scope.ServiceProvider.GetServices<IMcpTool>().ToDictionary(t => t.Name, StringComparer.Ordinal);
        var prompts = scope.ServiceProvider.GetServices<IMcpPrompt>().ToList();
        var all = new McpPrincipal(Guid.NewGuid(), McpScopes.All);

        Assert.Equal(4, prompts.Count);
        foreach (var prompt in prompts)
        {
            var text = prompt.Render(PromptArguments.Empty, all).Text;
            var referenced = ToolName().Matches(text).Select(m => m.Value).Distinct().ToList();
            Assert.NotEmpty(referenced);
            foreach (var name in referenced)
            {
                Assert.True(tools.TryGetValue(name, out var tool), prompt.Name + " references unknown tool " + name);
                var allowed = tool!.RequiredScope == prompt.RequiredScope
                    || (tool.RequiredScope == McpScopes.Playback && PlaybackOfferTools.Contains(name));
                Assert.True(allowed, prompt.Name + " references " + name + " from scope " + tool.RequiredScope);
            }
        }
    }

    [Fact]
    public void PlaybackToolsAppearOnlyWithPlaybackScope()
    {
        using var provider = RegisteredServices.Build();
        using var scope = provider.CreateScope();
        var withoutPlayback = new McpPrincipal(Guid.NewGuid(), new HashSet<string> { McpScopes.Library, McpScopes.Admin });

        foreach (var prompt in scope.ServiceProvider.GetServices<IMcpPrompt>())
        {
            var text = prompt.Render(PromptArguments.Empty, withoutPlayback).Text;
            Assert.DoesNotContain(ToolName().Matches(text), m => PlaybackOfferTools.Contains(m.Value));
        }
    }
}
