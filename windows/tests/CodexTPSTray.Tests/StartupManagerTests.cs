using System;
using System.IO;
using Xunit;

namespace CodexTPSTray.Tests;

public class StartupManagerTests
{
    [Fact]
    public void TryEnable_ValidPath_WritesQuotedCommand()
    {
        string validPath = Path.GetFullPath("test.exe");
        var fakeKeyStore = new FakeRunKeyStore();
        var manager = new StartupManager(fakeKeyStore, () => validPath);

        bool result = manager.TryEnable();

        Assert.True(result);
        Assert.Equal($"\"{validPath}\"", fakeKeyStore.StoredValue);
    }

    [Fact]
    public void TryEnable_PathWithSpaces_ProperlyQuoted()
    {
        string pathWithSpaces = Path.GetFullPath(@"test app.exe");
        var fakeKeyStore = new FakeRunKeyStore();
        var manager = new StartupManager(fakeKeyStore, () => pathWithSpaces);

        bool result = manager.TryEnable();

        Assert.True(result);
        Assert.Equal($"\"{pathWithSpaces}\"", fakeKeyStore.StoredValue);
    }

    [Fact]
    public void TryEnable_NullPath_ReturnsFalse()
    {
        var fakeKeyStore = new FakeRunKeyStore();
        var manager = new StartupManager(fakeKeyStore, () => null);

        bool result = manager.TryEnable();

        Assert.False(result);
        Assert.Null(fakeKeyStore.StoredValue);
    }

    [Fact]
    public void TryEnable_EmptyPath_ReturnsFalse()
    {
        var fakeKeyStore = new FakeRunKeyStore();
        var manager = new StartupManager(fakeKeyStore, () => string.Empty);

        bool result = manager.TryEnable();

        Assert.False(result);
        Assert.Null(fakeKeyStore.StoredValue);
    }

    [Fact]
    public void TryEnable_PathWithQuotes_ReturnsFalse()
    {
        var fakeKeyStore = new FakeRunKeyStore();
        var manager = new StartupManager(fakeKeyStore, () => @"C:\test""app.exe");

        bool result = manager.TryEnable();

        Assert.False(result);
        Assert.Null(fakeKeyStore.StoredValue);
    }

    [Fact]
    public void TryEnable_PathTooLong_ReturnsFalse()
    {
        string longPath = new string('x', 270);
        var fakeKeyStore = new FakeRunKeyStore();
        var manager = new StartupManager(fakeKeyStore, () => longPath);

        bool result = manager.TryEnable();

        Assert.False(result);
        Assert.Null(fakeKeyStore.StoredValue);
    }

    [Fact]
    public void TryEnable_RelativePath_ReturnsFalse()
    {
        var fakeKeyStore = new FakeRunKeyStore();
        var manager = new StartupManager(fakeKeyStore, () => "relative.exe");

        bool result = manager.TryEnable();

        Assert.False(result);
        Assert.Null(fakeKeyStore.StoredValue);
    }

    [Fact]
    public void TryEnable_InvalidPath_ReturnsFalse()
    {
        var fakeKeyStore = new FakeRunKeyStore();
        var manager = new StartupManager(fakeKeyStore, () => @"C:\invalid|path.exe");

        bool result = manager.TryEnable();

        Assert.False(result);
        Assert.Null(fakeKeyStore.StoredValue);
    }

    [Fact]
    public void TryEnable_RegistryWriteFails_ReturnsFalse()
    {
        string validPath = Path.GetFullPath("test.exe");
        var fakeKeyStore = new FakeRunKeyStore { WriteThrows = true };
        var manager = new StartupManager(fakeKeyStore, () => validPath);

        bool result = manager.TryEnable();

        Assert.False(result);
        Assert.Null(fakeKeyStore.StoredValue);
    }

    [Fact]
    public void TryEnable_NullKey_ReturnsFalse()
    {
        string validPath = Path.GetFullPath("test.exe");
        var fakeKeyStore = new FakeRunKeyStore { WriteKeyIsNull = true };
        var manager = new StartupManager(fakeKeyStore, () => validPath);

        bool result = manager.TryEnable();

        Assert.False(result);
        Assert.Null(fakeKeyStore.StoredValue);
    }

    [Fact]
    public void TryDisable_DeletesValue()
    {
        var fakeKeyStore = new FakeRunKeyStore { StoredValue = "\"test.exe\"" };
        var manager = new StartupManager(fakeKeyStore, () => "test.exe");

        bool result = manager.TryDisable();

        Assert.True(result);
        Assert.Null(fakeKeyStore.StoredValue);
    }

    [Fact]
    public void TryDisable_ValueAlreadyMissing_ReturnsTrue()
    {
        var fakeKeyStore = new FakeRunKeyStore { StoredValue = null };
        var manager = new StartupManager(fakeKeyStore, () => "test.exe");

        bool result = manager.TryDisable();

        Assert.True(result);
        Assert.Null(fakeKeyStore.StoredValue);
    }

    [Fact]
    public void TryDisable_RegistryDeleteFails_ReturnsFalse()
    {
        var fakeKeyStore = new FakeRunKeyStore { StoredValue = "\"test.exe\"", DeleteThrows = true };
        var manager = new StartupManager(fakeKeyStore, () => "test.exe");

        bool result = manager.TryDisable();

        Assert.False(result);
        Assert.Equal("\"test.exe\"", fakeKeyStore.StoredValue);
    }

    [Fact]
    public void TryDisable_NullKey_ReturnsFalse()
    {
        var fakeKeyStore = new FakeRunKeyStore { StoredValue = "\"test.exe\"", DeleteKeyIsNull = true };
        var manager = new StartupManager(fakeKeyStore, () => "test.exe");

        bool result = manager.TryDisable();

        Assert.False(result);
        Assert.Equal("\"test.exe\"", fakeKeyStore.StoredValue);
    }

    [Fact]
    public void TryGetIsEnabled_MatchingPath_ReturnsTrue()
    {
        string validPath = Path.GetFullPath("test.exe");
        string quotedPath = $"\"{validPath}\"";
        var fakeKeyStore = new FakeRunKeyStore { StoredValue = quotedPath };
        var manager = new StartupManager(fakeKeyStore, () => validPath);

        bool success = manager.TryGetIsEnabled(out bool isEnabled);

        Assert.True(success);
        Assert.True(isEnabled);
    }

    [Fact]
    public void TryGetIsEnabled_DifferentCase_MatchingPath_ReturnsTrue()
    {
        string validPath = Path.GetFullPath("test.exe");
        var fakeKeyStore = new FakeRunKeyStore { StoredValue = $"\"{validPath.ToUpper()}\"" };
        var manager = new StartupManager(fakeKeyStore, () => validPath);

        bool success = manager.TryGetIsEnabled(out bool isEnabled);

        Assert.True(success);
        Assert.True(isEnabled);
    }

    [Fact]
    public void TryGetIsEnabled_StalePath_ReturnsFalse()
    {
        var fakeKeyStore = new FakeRunKeyStore { StoredValue = "\"C:\\stale\\path.exe\"" };
        var manager = new StartupManager(fakeKeyStore, () => "C:\\current\\path.exe");

        bool success = manager.TryGetIsEnabled(out bool isEnabled);

        Assert.True(success);
        Assert.False(isEnabled);
    }

    [Fact]
    public void TryGetIsEnabled_MissingValue_ReturnsFalse()
    {
        var fakeKeyStore = new FakeRunKeyStore { StoredValue = null };
        var manager = new StartupManager(fakeKeyStore, () => "test.exe");

        bool success = manager.TryGetIsEnabled(out bool isEnabled);

        Assert.True(success);
        Assert.False(isEnabled);
    }

    [Fact]
    public void TryGetIsEnabled_RegistryReadFails_ReturnsFalseWithFalseResult()
    {
        var fakeKeyStore = new FakeRunKeyStore { ReadThrows = true };
        var manager = new StartupManager(fakeKeyStore, () => "test.exe");

        bool success = manager.TryGetIsEnabled(out bool isEnabled);

        Assert.False(success);
        Assert.False(isEnabled);
    }

    [Fact]
    public void TryGetIsEnabled_NullKey_ReturnsFalseWithFalseResult()
    {
        var fakeKeyStore = new FakeRunKeyStore { ReadKeyIsNull = true };
        var manager = new StartupManager(fakeKeyStore, () => "test.exe");

        bool success = manager.TryGetIsEnabled(out bool isEnabled);

        Assert.False(success);
        Assert.False(isEnabled);
    }

    [Fact]
    public void TryGetIsEnabled_NullExecutablePath_ReturnsFalse()
    {
        var fakeKeyStore = new FakeRunKeyStore { StoredValue = "\"test.exe\"" };
        var manager = new StartupManager(fakeKeyStore, () => null);

        bool success = manager.TryGetIsEnabled(out bool isEnabled);

        Assert.True(success);
        Assert.False(isEnabled);
    }
}