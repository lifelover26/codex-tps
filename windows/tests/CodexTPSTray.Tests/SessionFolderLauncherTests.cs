using System;
using System.IO;
using Xunit;

namespace CodexTPSTray.Tests;

public class SessionFolderLauncherTests : IDisposable
{
    private readonly string _tempDirectory;

    public SessionFolderLauncherTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"CodexTPSTest_{Guid.NewGuid()}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void OpenSessionsFolder_ExistingDirectory_ShellLauncherCalled()
    {
        Directory.CreateDirectory(_tempDirectory);

        var fakeShellLauncher = new FakeShellLauncher();
        var launcher = new SessionFolderLauncher(() => _tempDirectory, fakeShellLauncher);

        bool result = launcher.OpenSessionsFolder();

        Assert.True(result);
        Assert.Single(fakeShellLauncher.LaunchCalls);
        Assert.Equal(_tempDirectory, fakeShellLauncher.LaunchCalls[0]);
    }

    [Fact]
    public void OpenSessionsFolder_MissingDirectory_ShellLauncherNotCalled()
    {
        string missingPath = Path.Combine(_tempDirectory, "missing");

        var fakeShellLauncher = new FakeShellLauncher();
        var launcher = new SessionFolderLauncher(() => missingPath, fakeShellLauncher);

        bool result = launcher.OpenSessionsFolder();

        Assert.False(result);
        Assert.Empty(fakeShellLauncher.LaunchCalls);
    }

    [Fact]
    public void OpenSessionsFolder_ShellLauncherThrows_ReturnsFalse()
    {
        Directory.CreateDirectory(_tempDirectory);

        var fakeShellLauncher = new FakeShellLauncher { ThrowException = true };
        var launcher = new SessionFolderLauncher(() => _tempDirectory, fakeShellLauncher);

        bool result = launcher.OpenSessionsFolder();

        Assert.False(result);
    }

    [Fact]
    public void OpenSessionsFolder_ShellLauncherReturnsFalse_ReturnsFalse()
    {
        Directory.CreateDirectory(_tempDirectory);

        var fakeShellLauncher = new FakeShellLauncher { LaunchResult = false };
        var launcher = new SessionFolderLauncher(() => _tempDirectory, fakeShellLauncher);

        bool result = launcher.OpenSessionsFolder();

        Assert.False(result);
        Assert.Single(fakeShellLauncher.LaunchCalls);
    }
}