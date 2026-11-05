using System.Linq;
using System.Text.Json;
using Jellyfin.Plugin.Mcp.Mcp;
using Json.Schema;
using Xunit;

namespace Jellyfin.Plugin.Mcp.Tests.Tools;

/// <summary>
/// Validates tool results against the tool's declared output schema.
/// </summary>
internal static class SchemaAssert
{
    public static void Conforms(IMcpTool tool, ToolResult result)
    {
        Assert.False(result.IsError, result.Text);
        Assert.NotNull(tool.OutputSchema);
        Assert.NotNull(result.Structured);
        Assert.Equal("object", tool.OutputSchema!.Value.GetProperty("type").GetString());
        Valid(tool.OutputSchema.Value, result.Structured!.Value);
    }

    public static void Valid(JsonElement schema, JsonElement instance)
    {
        var parsed = JsonSchema.FromText(schema.GetRawText());
        var evaluation = parsed.Evaluate(instance, new EvaluationOptions { OutputFormat = OutputFormat.List });
        var errors = (evaluation.Details ?? [])
            .Where(d => d.Errors is not null)
            .SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation} ({d.EvaluationPath}): {e.Value}"));
        Assert.True(evaluation.IsValid, "Schema violations:\n" + string.Join('\n', errors) + "\nInstance: " + instance.GetRawText());
    }

    public static void Valid(string schemaJson, object value)
    {
        using var schema = JsonDocument.Parse(schemaJson);
        Valid(schema.RootElement, ToolResult.Json(value).Structured!.Value);
    }
}
