using System;
using System.Linq;
using CodexTPSCore;
using Xunit;

namespace CodexTPSCore.Tests;

public class WslDistributionListParserTests
{
    [Fact]
    public void ParsesSimpleListAndSortsDeterministically()
    {
        var raw = "Ubuntu\nDebian\n";
        var result = WslDistributionListParser.Parse(raw);
        Assert.Equal(new[] { "Debian", "Ubuntu" }, result);
    }

    [Fact]
    public void ParsesCrlfLineEndings()
    {
        var raw = "Ubuntu\r\nDebian\r\n";
        var result = WslDistributionListParser.Parse(raw);
        Assert.Equal(new[] { "Debian", "Ubuntu" }, result);
    }

    [Fact]
    public void StripsNulBytesFromUtf16DecodedAsUtf8()
    {
        // wsl --list emits UTF-16LE. When decoded as UTF-8 the bytes appear as
        // ASCII chars with a NUL between each one. The parser must drop them.
        var raw = "U\0b\0u\0n\0t\0u\0\r\0\n\0D\0e\0b\0i\0a\0n\0\r\0\n\0";
        var result = WslDistributionListParser.Parse(raw);
        Assert.Equal(new[] { "Debian", "Ubuntu" }, result);
    }

    [Fact]
    public void DropsBlankLines()
    {
        var raw = "Ubuntu\n\nDebian\n\n";
        var result = WslDistributionListParser.Parse(raw);
        Assert.Equal(new[] { "Debian", "Ubuntu" }, result);
    }

    [Fact]
    public void DeduplicatesNames()
    {
        var raw = "Ubuntu\nDebian\nUbuntu\nUbuntu\n";
        var result = WslDistributionListParser.Parse(raw);
        Assert.Equal(new[] { "Debian", "Ubuntu" }, result);
    }

    [Fact]
    public void OrderIsDeterministicRegardlessOfInputOrder()
    {
        var first = WslDistributionListParser.Parse("Debian\nUbuntu\nkali-linux\n");
        var second = WslDistributionListParser.Parse("kali-linux\nUbuntu\nDebian\n");
        Assert.Equal(first, second);
        Assert.Equal(new[] { "Debian", "Ubuntu", "kali-linux" }, first);
    }

    [Fact]
    public void ReturnsEmptyForEmptyInput()
    {
        Assert.Empty(WslDistributionListParser.Parse(""));
    }

    [Fact]
    public void ReturnsEmptyForNullInput()
    {
        Assert.Empty(WslDistributionListParser.Parse(null));
    }

    [Fact]
    public void ReturnsSingleNameWithoutTrailingNewline()
    {
        Assert.Equal(new[] { "Ubuntu" }, WslDistributionListParser.Parse("Ubuntu"));
    }

    [Fact]
    public void PreservesSpacesInNames()
    {
        // 'My Distro' sorts before 'Other' because 'M' (0x4D) < 'O' (0x4F).
        var result = WslDistributionListParser.Parse("My Distro\nOther\n");
        Assert.Equal(new[] { "My Distro", "Other" }, result);
    }

    [Fact]
    public void HandlesLoneCrLineEndings()
    {
        var result = WslDistributionListParser.Parse("Ubuntu\rDebian\r");
        Assert.Equal(new[] { "Debian", "Ubuntu" }, result);
    }

    [Fact]
    public void TrimsSurroundingTabsAndSpaces()
    {
        var result = WslDistributionListParser.Parse("  Ubuntu  \n\tDebian\t\n");
        Assert.Equal(new[] { "Debian", "Ubuntu" }, result);
    }
}
