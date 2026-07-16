using Xunit;

namespace CodexTPSTray.Tests;

public class SingleInstanceGuardTests
{
    [Fact]
    public void FirstInstance_AcquiresMutex_IsOwnedTrue()
    {
        string mutexName = $"Local\\gaofeng21cn.CodexTPS.Test.{Guid.NewGuid()}";

        using var guard = new SingleInstanceGuard(mutexName);

        Assert.True(guard.IsOwned);
    }

    [Fact]
    public void SecondInstance_CannotAcquireMutex_IsOwnedFalse()
    {
        string mutexName = $"Local\\gaofeng21cn.CodexTPS.Test.{Guid.NewGuid()}";

        using var firstGuard = new SingleInstanceGuard(mutexName);
        Assert.True(firstGuard.IsOwned);

        using var secondGuard = new SingleInstanceGuard(mutexName);
        Assert.False(secondGuard.IsOwned);
    }

    [Fact]
    public void AfterFirstDisposed_ThirdInstance_CanAcquireMutex()
    {
        string mutexName = $"Local\\gaofeng21cn.CodexTPS.Test.{Guid.NewGuid()}";

        var firstGuard = new SingleInstanceGuard(mutexName);
        Assert.True(firstGuard.IsOwned);

        var secondGuard = new SingleInstanceGuard(mutexName);
        Assert.False(secondGuard.IsOwned);

        secondGuard.Dispose();
        firstGuard.Dispose();

        using var thirdGuard = new SingleInstanceGuard(mutexName);
        Assert.True(thirdGuard.IsOwned);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        string mutexName = $"Local\\gaofeng21cn.CodexTPS.Test.{Guid.NewGuid()}";

        using var guard = new SingleInstanceGuard(mutexName);
        Assert.True(guard.IsOwned);

        guard.Dispose();
        guard.Dispose();
    }

    [Fact]
    public void NonOwningGuard_Dispose_ReleasesHandle()
    {
        string mutexName = $"Local\\gaofeng21cn.CodexTPS.Test.{Guid.NewGuid()}";

        using var firstGuard = new SingleInstanceGuard(mutexName);
        Assert.True(firstGuard.IsOwned);

        var secondGuard = new SingleInstanceGuard(mutexName);
        Assert.False(secondGuard.IsOwned);

        secondGuard.Dispose();

        firstGuard.Dispose();

        using var thirdGuard = new SingleInstanceGuard(mutexName);
        Assert.True(thirdGuard.IsOwned);
    }
}