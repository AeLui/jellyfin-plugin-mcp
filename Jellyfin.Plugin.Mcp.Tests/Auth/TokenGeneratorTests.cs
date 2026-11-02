using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.Mcp.Auth;
using Xunit;

namespace Jellyfin.Plugin.Mcp.Tests.Auth;

public class TokenGeneratorTests
{
    [Fact]
    public void NewToken_HasPrefixAndLength()
    {
        var token = TokenGenerator.NewToken();

        Assert.StartsWith("jmcp_", token, StringComparison.Ordinal);
        var secret = token["jmcp_".Length..];
        Assert.Equal(43, secret.Length);
        Assert.DoesNotContain('=', secret);
        Assert.Matches(new Regex("^[A-Za-z0-9_-]+$"), secret);
    }

    [Fact]
    public void NewToken_IsUnique()
    {
        var set = new HashSet<string>();
        for (var i = 0; i < 1000; i++)
        {
            set.Add(TokenGenerator.NewToken());
        }

        Assert.Equal(1000, set.Count);
    }

    [Fact]
    public void Hash_IsLowercaseSha256Hex()
    {
        // SHA-256 of "abc".
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", TokenGenerator.Hash("abc"));
    }

    [Fact]
    public void DisplayPrefix_IsFirstEightCharsAfterPrefix()
    {
        Assert.Equal("abcdefgh", TokenGenerator.DisplayPrefix("jmcp_abcdefghijkl"));
    }

    [Fact]
    public void HashEquals_ComparesValues()
    {
        Assert.True(TokenGenerator.HashEquals("abc", "abc"));
        Assert.False(TokenGenerator.HashEquals("abc", "abd"));
        Assert.False(TokenGenerator.HashEquals("abc", "abcd"));
    }
}
