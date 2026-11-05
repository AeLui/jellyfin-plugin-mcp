using System.Text.Json;
using Jellyfin.Plugin.Mcp.Mcp;
using Xunit;

namespace Jellyfin.Plugin.Mcp.Tests.Mcp;

public class ToolResultTests
{
    [Fact]
    public void Json_KeepsAccentedAndCjkTextUnescaped()
    {
        var result = ToolResult.Json(new { Title = "Amélie", Other = "千と千尋の神隠し" });

        Assert.Contains("Amélie", result.Text, System.StringComparison.Ordinal);
        Assert.Contains("千と千尋の神隠し", result.Text, System.StringComparison.Ordinal);
        Assert.DoesNotContain("\\u", result.Text, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Json_KeepsStructuredValueEqualToText()
    {
        var result = ToolResult.Json(new { A = 1, B = (string?)null });

        Assert.NotNull(result.Structured);
        var structured = result.Structured!.Value;
        Assert.Equal(JsonValueKind.Object, structured.ValueKind);
        Assert.Equal(1, structured.GetProperty("a").GetInt32());
        Assert.False(structured.TryGetProperty("b", out _));
        Assert.Equal(result.Text, structured.GetRawText());
    }

    [Fact]
    public void Error_HasNoStructuredValue()
    {
        Assert.Null(ToolResult.Error("x").Structured);
    }

    [Fact]
    public void Json_UsesCamelCaseAndOmitsNulls()
    {
        var result = ToolResult.Json(new { ItemName = "x", Missing = (string?)null });

        using var doc = JsonDocument.Parse(result.Text);
        Assert.Equal("x", doc.RootElement.GetProperty("itemName").GetString());
        Assert.False(doc.RootElement.TryGetProperty("missing", out _));
    }
}
