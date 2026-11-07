using System.Text.Json;
using Jellyfin.Plugin.Mcp.Mcp;
using Xunit;

namespace Jellyfin.Plugin.Mcp.Tests.Mcp;

public class PromptArgumentsTests
{
    private static readonly string[] Types = ["movies", "shows", "music", "all"];

    private static PromptArguments Args(string json) => new(JsonDocument.Parse(json).RootElement.Clone());

    [Fact]
    public void Null_And_Missing_Arguments_AreEmpty()
    {
        foreach (var args in new[] { new PromptArguments(default), Args("null"), PromptArguments.Empty })
        {
            Assert.False(args.Has("x"));
            Assert.Equal(5, args.GetInt("x", 1, 9, 5));
            Assert.Null(args.GetText("x", 100));
        }
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"s\"")]
    [InlineData("3")]
    public void NonObjectArguments_Throws(string json)
    {
        var ex = Assert.Throws<PromptException>(() => Args(json));

        Assert.Equal("Invalid params: 'arguments' must be an object", ex.Message);
    }

    [Fact]
    public void NonStringValue_Throws()
    {
        var args = Args("""{"days":7}""");

        var ex = Assert.Throws<PromptException>(() => args.GetInt("days", 1, 90, 7));

        Assert.Equal("Invalid argument 'days': must be a string", ex.Message);
        Assert.Throws<PromptException>(() => args.Has("days"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void GetInt_EmptyString_ReturnsDefault(string value)
    {
        var args = Args("{\"days\":\"" + value + "\"}");

        Assert.Equal(7, args.GetInt("days", 1, 90, 7));
        Assert.False(args.Has("days"));
    }

    [Fact]
    public void GetInt_Parses()
    {
        Assert.Equal(30, Args("""{"days":"30"}""").GetInt("days", 1, 90, 7));
        Assert.Equal(30, Args("""{"days":" 30 "}""").GetInt("days", 1, 90, 7));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("91")]
    [InlineData("7.5")]
    [InlineData("seven")]
    [InlineData("99999999999")]
    public void GetInt_InvalidOrOutOfRange_Throws(string value)
    {
        var args = Args("{\"days\":\"" + value + "\"}");

        var ex = Assert.Throws<PromptException>(() => args.GetInt("days", 1, 90, 7));

        Assert.Equal("Invalid argument 'days': must be an integer between 1 and 90", ex.Message);
    }

    [Fact]
    public void GetEnum_IsCaseInsensitive_AndDefaults()
    {
        Assert.Equal("movies", Args("""{"type":"Movies"}""").GetEnum("type", Types, "all"));
        Assert.Equal("all", Args("""{"type":""}""").GetEnum("type", Types, "all"));
        Assert.Equal("all", PromptArguments.Empty.GetEnum("type", Types, "all"));
    }

    [Fact]
    public void GetEnum_Unknown_Throws()
    {
        var ex = Assert.Throws<PromptException>(() => Args("""{"type":"films"}""").GetEnum("type", Types, "all"));

        Assert.Equal("Invalid argument 'type': must be one of movies, shows, music, all", ex.Message);
    }

    [Fact]
    public void GetText_StripsControlCharsAndTruncates()
    {
        Assert.Equal("a  b c", Args("{\"mood\":\"  a\\r\\nb\\tc  \"}").GetText("mood", 100));
        Assert.Equal(100, Args("{\"mood\":\"" + new string('x', 500) + "\"}").GetText("mood", 100)!.Length);
        Assert.Null(Args("""{"mood":" \t "}""").GetText("mood", 100));
    }

    [Fact]
    public void UnknownArguments_AreIgnored()
    {
        var args = Args("""{"foo":"bar"}""");

        Assert.Null(args.GetText("genre", 100));
        Assert.False(args.Has("genre"));
    }
}
