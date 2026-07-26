using CodexTPSCore;
using Xunit;

namespace CodexTPSCore.Tests;

public class WslLinuxPathConverterTests
{
    [Fact]
    public void ConvertsTypicalHomeCodexPath()
    {
        var ok = WslLinuxPathConverter.TryConvertToUnc(
            "/home/user/.codex", "Ubuntu", out var unc);
        Assert.True(ok);
        Assert.Equal(@"\\wsl.localhost\Ubuntu\home\user\.codex", unc);
    }

    [Fact]
    public void ConvertsExplicitCodexHomeUnderVar()
    {
        var ok = WslLinuxPathConverter.TryConvertToUnc(
            "/srv/codex/data", "Debian", out var unc);
        Assert.True(ok);
        Assert.Equal(@"\\wsl.localhost\Debian\srv\codex\data", unc);
    }

    [Fact]
    public void RootPathBecomesDistributionRootWithTrailingSeparator()
    {
        var ok = WslLinuxPathConverter.TryConvertToUnc(
            "/", "Ubuntu", out var unc);
        Assert.True(ok);
        Assert.Equal(@"\\wsl.localhost\Ubuntu\", unc);
    }

    [Fact]
    public void DropsTrailingSlashForNonRootPath()
    {
        var ok = WslLinuxPathConverter.TryConvertToUnc(
            "/home/user/.codex/", "Ubuntu", out var unc);
        Assert.True(ok);
        Assert.Equal(@"\\wsl.localhost\Ubuntu\home\user\.codex", unc);
    }

    [Fact]
    public void HandlesLeadingDotSegmentInPath()
    {
        var ok = WslLinuxPathConverter.TryConvertToUnc(
            "/.codex", "Ubuntu", out var unc);
        Assert.True(ok);
        Assert.Equal(@"\\wsl.localhost\Ubuntu\.codex", unc);
    }

    [Fact]
    public void PreservesSpacesInDistributionName()
    {
        var ok = WslLinuxPathConverter.TryConvertToUnc(
            "/home/user/.codex", "My Distro", out var unc);
        Assert.True(ok);
        Assert.Equal(@"\\wsl.localhost\My Distro\home\user\.codex", unc);
    }

    [Fact]
    public void RejectsEmptyPath()
    {
        Assert.False(WslLinuxPathConverter.TryConvertToUnc("", "Ubuntu", out _));
    }

    [Fact]
    public void RejectsNullPath()
    {
        Assert.False(WslLinuxPathConverter.TryConvertToUnc(null, "Ubuntu", out _));
    }

    [Fact]
    public void RejectsRelativePath()
    {
        Assert.False(WslLinuxPathConverter.TryConvertToUnc("home/user/.codex", "Ubuntu", out _));
    }

    [Fact]
    public void RejectsTraversalSegment()
    {
        Assert.False(WslLinuxPathConverter.TryConvertToUnc("/home/../etc", "Ubuntu", out _));
    }

    [Fact]
    public void RejectsDotSegment()
    {
        Assert.False(WslLinuxPathConverter.TryConvertToUnc("/home/./user", "Ubuntu", out _));
    }

    [Fact]
    public void RejectsNulInPath()
    {
        Assert.False(WslLinuxPathConverter.TryConvertToUnc("/home/user\0/.codex", "Ubuntu", out _));
    }

    [Fact]
    public void RejectsControlCharacterInSegment()
    {
        Assert.False(WslLinuxPathConverter.TryConvertToUnc("/home/us\ter", "Ubuntu", out _));
    }

    [Fact]
    public void RejectsBackslashInPathSegment()
    {
        // Linux permits '\' in file names, but after conversion '\' becomes the
        // UNC separator. Any embedded backslash must be rejected outright so it
        // cannot inject an extra path component.
        Assert.False(WslLinuxPathConverter.TryConvertToUnc(
            "/home/user\\bar/.codex", "Ubuntu", out _));
    }

    [Fact]
    public void RejectsBackslashTraversalInPathSegment()
    {
        // A backslash followed by '..' would otherwise escape the intended
        // subtree after UNC conversion. Must be rejected before that happens.
        Assert.False(WslLinuxPathConverter.TryConvertToUnc(
            "/home/user\\..\\etc/.codex", "Ubuntu", out _));
    }

    [Fact]
    public void RejectsBackslashEvenWhenSegmentIsOtherwiseValid()
    {
        // A single trailing backslash in the last segment also counts: it
        // would become an empty trailing UNC component after conversion.
        Assert.False(WslLinuxPathConverter.TryConvertToUnc(
            "/home/user/.codex\\", "Ubuntu", out _));
    }

    [Fact]
    public void CollapsesInteriorEmptySegmentsWithoutDoubleBackslashes()
    {
        // Linux treats "//" as a single separator. The UNC must not contain a
        // "\\" run, which would be a malformed (though tolerated) path.
        var ok = WslLinuxPathConverter.TryConvertToUnc(
            "/home//user/.codex", "Ubuntu", out var unc);
        Assert.True(ok);
        Assert.Equal(@"\\wsl.localhost\Ubuntu\home\user\.codex", unc);
    }

    [Fact]
    public void TreatsAllSlashesPathAsRoot()
    {
        var ok = WslLinuxPathConverter.TryConvertToUnc(
            "///", "Ubuntu", out var unc);
        Assert.True(ok);
        Assert.Equal(@"\\wsl.localhost\Ubuntu\", unc);
    }

    [Fact]
    public void RejectsEmptyDistributionName()
    {
        Assert.False(WslLinuxPathConverter.TryConvertToUnc("/home/user/.codex", "", out _));
    }

    [Fact]
    public void RejectsNullDistributionName()
    {
        Assert.False(WslLinuxPathConverter.TryConvertToUnc("/home/user/.codex", null!, out _));
    }

    [Theory]
    [InlineData(" Ubuntu")]
    [InlineData("Ubuntu ")]
    [InlineData("  Ubuntu  ")]
    [InlineData("\tUbuntu")]
    [InlineData("Ubuntu\t")]
    public void RejectsUntrimmedDistributionName(string distributionName)
    {
        // The converter calls the validator directly, so unnormalized names
        // must be rejected here too. Callers (CodexDataSourceSelection and
        // WslDistributionListParser) Trim before calling; if they forget, the
        // converter refuses rather than emitting a UNC with a trailing space
        // that would silently point at a different path.
        Assert.False(WslLinuxPathConverter.TryConvertToUnc(
            "/home/user/.codex", distributionName, out _));
    }

    [Fact]
    public void RejectsWhitespaceOnlyDistributionName()
    {
        // Pure whitespace is rejected by IsNullOrWhiteSpace before the
        // leading/trailing check, but the converter must still refuse it.
        Assert.False(WslLinuxPathConverter.TryConvertToUnc(
            "/home/user/.codex", "   ", out _));
    }

    [Fact]
    public void AcceptsDistributionNameWithInternalSpaces()
    {
        // Internal spaces are valid (real distributions like "My Distro"
        // exist); only surrounding whitespace is rejected.
        var ok = WslLinuxPathConverter.TryConvertToUnc(
            "/home/user/.codex", "My Distro", out var unc);
        Assert.True(ok);
        Assert.Equal(@"\\wsl.localhost\My Distro\home\user\.codex", unc);
    }

    [Fact]
    public void RejectsBackslashInDistributionName()
    {
        // A backslash in the name would inject an extra UNC component.
        Assert.False(WslLinuxPathConverter.TryConvertToUnc("/home/user/.codex", "evil\\path", out _));
    }

    [Fact]
    public void RejectsForwardSlashInDistributionName()
    {
        Assert.False(WslLinuxPathConverter.TryConvertToUnc("/home/user/.codex", "evil/path", out _));
    }

    [Fact]
    public void RejectsControlCharacterInDistributionName()
    {
        Assert.False(WslLinuxPathConverter.TryConvertToUnc("/home/user/.codex", "evil\tname", out _));
    }
}
