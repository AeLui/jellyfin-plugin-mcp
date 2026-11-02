using System;
using System.Text.Json;
using Jellyfin.Plugin.Mcp.Mcp;
using Xunit;

namespace Jellyfin.Plugin.Mcp.Tests.Mcp;

public class ToolArgumentsTests
{
    public enum Sample
    {
        Movie,
        MusicAlbum,
    }

    private static ToolArguments Args(string json) => new(JsonDocument.Parse(json).RootElement.Clone());

    [Theory]
    [InlineData("""{"limit":0}""", 1)]
    [InlineData("""{"limit":10000}""", 100)]
    [InlineData("""{}""", 20)]
    [InlineData("""{"limit":-3}""", 1)]
    [InlineData("""{"limit":50}""", 50)]
    public void GetPaging_ClampsLimit(string json, int expected)
    {
        Assert.Equal(expected, Args(json).GetPaging(100).Limit);
    }

    [Fact]
    public void GetPaging_NegativeStartIndex_ClampsToZero()
    {
        Assert.Equal(0, Args("""{"startIndex":-5}""").GetPaging(100).StartIndex);
        Assert.Equal(7, Args("""{"startIndex":7}""").GetPaging(100).StartIndex);
    }

    [Fact]
    public void GetPaging_NonIntegerLimit_ThrowsNamingArgument()
    {
        var ex = Assert.Throws<ToolException>(() => Args("""{"limit":"ten"}""").GetPaging(100));

        Assert.Contains("'limit'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingArguments_AreTreatedAsEmpty()
    {
        var args = new ToolArguments(default);

        Assert.Null(args.GetString("x"));
        Assert.Equal(20, args.GetPaging(100).Limit);
    }

    [Fact]
    public void GetRequiredGuid_Invalid_ThrowsNamingArgument()
    {
        var ex = Assert.Throws<ToolException>(() => Args("""{"itemId":"nope"}""").GetRequiredGuid("itemId"));

        Assert.Equal("Invalid argument 'itemId': must be a valid GUID", ex.Message);
    }

    [Fact]
    public void GetRequiredGuid_Missing_ThrowsNamingArgument()
    {
        var ex = Assert.Throws<ToolException>(() => Args("{}").GetRequiredGuid("itemId"));

        Assert.Equal("Invalid argument 'itemId': is required", ex.Message);
    }

    [Fact]
    public void GetRequiredString_MissingOrWrongType_Throws()
    {
        Assert.Throws<ToolException>(() => Args("{}").GetRequiredString("query"));
        Assert.Throws<ToolException>(() => Args("""{"query":5}""").GetRequiredString("query"));
        Assert.Equal("abc", Args("""{"query":"abc"}""").GetRequiredString("query"));
    }

    [Fact]
    public void GetGuid_ParsesValueAndReturnsNullWhenAbsent()
    {
        var id = Guid.NewGuid();

        Assert.Equal(id, Args($$"""{"id":"{{id}}"}""").GetGuid("id"));
        Assert.Null(Args("{}").GetGuid("id"));
    }

    [Fact]
    public void GetIntAndBool_ValidateTypes()
    {
        Assert.Equal(3, Args("""{"n":3}""").GetInt("n"));
        Assert.True(Args("""{"b":true}""").GetBool("b"));
        Assert.Null(Args("{}").GetInt("n"));
        Assert.Throws<ToolException>(() => Args("""{"n":1.5}""").GetInt("n"));
        Assert.Throws<ToolException>(() => Args("""{"b":"yes"}""").GetBool("b"));
    }

    [Fact]
    public void GetStringArray_ReturnsItemsAndValidates()
    {
        Assert.Equal(new[] { "a", "b" }, Args("""{"x":["a","b"]}""").GetStringArray("x"));
        Assert.Empty(Args("{}").GetStringArray("x"));
        Assert.Throws<ToolException>(() => Args("""{"x":"a"}""").GetStringArray("x"));
        Assert.Throws<ToolException>(() => Args("""{"x":[1]}""").GetStringArray("x"));
    }

    [Fact]
    public void GetGuidArray_ParsesAndRejectsInvalidItems()
    {
        var id = Guid.NewGuid();

        Assert.Equal(new[] { id }, Args($$"""{"ids":["{{id}}"]}""").GetGuidArray("ids"));
        Assert.Throws<ToolException>(() => Args("""{"ids":["bad"]}""").GetGuidArray("ids"));
    }

    [Theory]
    [InlineData("music_album", Sample.MusicAlbum)]
    [InlineData("MusicAlbum", Sample.MusicAlbum)]
    [InlineData("movie", Sample.Movie)]
    public void GetEnum_AcceptsSnakeCase(string value, Sample expected)
    {
        Assert.Equal(expected, Args($$"""{"type":"{{value}}"}""").GetEnum<Sample>("type"));
    }

    [Fact]
    public void GetEnum_UnknownValue_ThrowsNamingArgument()
    {
        var ex = Assert.Throws<ToolException>(() => Args("""{"type":"banana"}""").GetEnum<Sample>("type"));

        Assert.Contains("'type'", ex.Message, StringComparison.Ordinal);
        Assert.Null(Args("{}").GetEnum<Sample>("type"));
    }
}
