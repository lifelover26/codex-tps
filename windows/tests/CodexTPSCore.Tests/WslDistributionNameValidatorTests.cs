using CodexTPSCore;
using Xunit;

namespace CodexTPSCore.Tests;

public class WslDistributionNameValidatorTests
{
    [Theory]
    [InlineData("Ubuntu")]
    [InlineData("Debian")]
    [InlineData("Ubuntu-22.04")]
    [InlineData("My Distro")]
    [InlineData("kali-linux")]
    [InlineData("Distro'WithQuotes")]
    [InlineData("Distro\"WithDouble")]
    [InlineData("Ubuntu (Preview)")]
    public void AcceptsValidNames(string name)
    {
        Assert.True(WslDistributionNameValidator.IsValid(name));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("foo/bar")]
    [InlineData("foo\\bar")]
    [InlineData("foo\tbar")]
    [InlineData("foo\nbar")]
    [InlineData("foo\rbar")]
    [InlineData("foo\0bar")]
    public void RejectsInvalidNames(string name)
    {
        Assert.False(WslDistributionNameValidator.IsValid(name));
    }

    [Fact]
    public void RejectsNull()
    {
        Assert.False(WslDistributionNameValidator.IsValid(null));
    }

    [Theory]
    [InlineData(" Ubuntu")]
    [InlineData("Ubuntu ")]
    [InlineData("  Ubuntu  ")]
    [InlineData("\tUbuntu")]
    [InlineData("Ubuntu\t")]
    [InlineData(" Ubuntu ")]
    public void RejectsUntrimmedSurroundingWhitespace(string name)
    {
        // A name with leading or trailing whitespace would persist verbatim
        // and break value equality across runs. The validator forces callers
        // to Trim before storing. (Internal spaces are still allowed — see
        // AcceptsValidNames for "My Distro".)
        Assert.False(WslDistributionNameValidator.IsValid(name));
    }

    [Fact]
    public void AcceptsInternalSpacesAfterTrim()
    {
        // Internal spaces are valid; only surrounding whitespace is rejected.
        Assert.True(WslDistributionNameValidator.IsValid("My Distro"));
    }

    [Fact]
    public void DoesNotRejectEmbeddedDots()
    {
        // Only exact "." and ".." segments are rejected; "22.04" is fine.
        Assert.True(WslDistributionNameValidator.IsValid("Ubuntu.22.04"));
    }

    [Fact]
    public void DoesNotRejectLeadingDot()
    {
        // A leading dot is unusual but not a traversal segment on its own.
        Assert.True(WslDistributionNameValidator.IsValid(".hidden-distro"));
    }
}
