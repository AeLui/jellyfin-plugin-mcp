using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Mcp.Mcp;

namespace Jellyfin.Plugin.Mcp.Tests.Mcp;

internal sealed class FakePrompt : IMcpPrompt
{
    private readonly Func<PromptArguments, McpPrincipal, PromptMessage> _render;

    public FakePrompt(
        string name,
        string scope,
        IReadOnlyList<PromptArgument>? arguments = null,
        Func<PromptArguments, McpPrincipal, PromptMessage>? render = null)
    {
        Name = name;
        RequiredScope = scope;
        Arguments = arguments ?? Array.Empty<PromptArgument>();
        _render = render ?? ((_, _) => new PromptMessage("Fake " + name, "Text of " + name));
    }

    public string Name { get; }

    public string Title => "Fake " + Name;

    public string Description => "Fake prompt " + Name;

    public string RequiredScope { get; }

    public IReadOnlyList<PromptArgument> Arguments { get; }

    public PromptMessage Render(PromptArguments args, McpPrincipal principal) => _render(args, principal);
}
