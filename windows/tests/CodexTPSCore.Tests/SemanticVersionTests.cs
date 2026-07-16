using Xunit;

namespace CodexTPSCore.Tests;

public class SemanticVersionTests
{
    [Fact]
    public void TestParsesAppAndTagVersions()
    {
        Assert.Equal(new SemanticVersion(0, 2, 0), SemanticVersion.Parse("0.2.0"));
        Assert.Equal(new SemanticVersion(12, 34, 56), SemanticVersion.Parse("v12.34.56"));
    }

    [Fact]
    public void TestComparesEachVersionComponent()
    {
        Assert.True(SemanticVersion.Parse("0.1.9") < SemanticVersion.Parse("0.2.0"));
        Assert.True(SemanticVersion.Parse("0.9.9") < SemanticVersion.Parse("1.0.0"));
        Assert.Equal(SemanticVersion.Parse("2.3.4"), SemanticVersion.Parse("v2.3.4"));
    }

    [Fact]
    public void TestRejectsMalformedVersions()
    {
        Assert.Null(SemanticVersion.Parse("0.2"));
        Assert.Null(SemanticVersion.Parse("release-0.2.0"));
        Assert.Null(SemanticVersion.Parse("0.2.beta"));
        Assert.Null(SemanticVersion.Parse("-1.2.3"));
    }
}
