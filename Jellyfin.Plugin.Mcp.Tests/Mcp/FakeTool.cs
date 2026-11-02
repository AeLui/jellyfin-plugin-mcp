using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Mcp.Mcp;

namespace Jellyfin.Plugin.Mcp.Tests.Mcp;

internal sealed class FakeTool : IMcpTool
{
    private readonly Func<ToolArguments, CancellationToken, Task<ToolResult>> _execute;

    public FakeTool(string name, string scope, Func<ToolArguments, CancellationToken, Task<ToolResult>>? execute = null)
    {
        Name = name;
        RequiredScope = scope;
        _execute = execute ?? ((_, _) => Task.FromResult(ToolResult.Json(new { ok = true })));
    }

    public string Name { get; }

    public string Description => "Fake " + Name;

    public string RequiredScope { get; }

    public JsonElement InputSchema { get; } = JsonDocument.Parse("""{"type":"object","properties":{"q":{"type":"string"}}}""").RootElement.Clone();

    public ToolAnnotations Annotations { get; } = new(true, false, true);

    public Task<ToolResult> ExecuteAsync(ToolArguments args, McpPrincipal principal, CancellationToken ct) => _execute(args, ct);
}
