using System;
using System.Windows.Forms;
using Xunit;

namespace CodexTPSTray.Tests;

public class WindowsFormsThemeApplierTests
{
    [Fact]
    public void TryApply_Light_AppliesClassic()
    {
        SystemColorMode? appliedMode = null;
        var applier = new WindowsFormsThemeApplier(mode => appliedMode = mode);

        bool result = applier.TryApply(EffectiveTheme.Light);

        Assert.True(result);
        Assert.Equal(SystemColorMode.Classic, appliedMode);
    }

    [Fact]
    public void TryApply_Dark_AppliesDark()
    {
        SystemColorMode? appliedMode = null;
        var applier = new WindowsFormsThemeApplier(mode => appliedMode = mode);

        bool result = applier.TryApply(EffectiveTheme.Dark);

        Assert.True(result);
        Assert.Equal(SystemColorMode.Dark, appliedMode);
    }

    [Fact]
    public void TryApply_Unknown_AppliesClassic()
    {
        SystemColorMode? appliedMode = null;
        var applier = new WindowsFormsThemeApplier(mode => appliedMode = mode);

        bool result = applier.TryApply((EffectiveTheme)99);

        Assert.True(result);
        Assert.Equal(SystemColorMode.Classic, appliedMode);
    }

    [Fact]
    public void TryApply_CallsSetterOnce()
    {
        int callCount = 0;
        var applier = new WindowsFormsThemeApplier(_ => callCount++);

        applier.TryApply(EffectiveTheme.Dark);

        Assert.Equal(1, callCount);
    }

    [Fact]
    public void TryApply_SetterThrows_ReturnsFalse()
    {
        var applier = new WindowsFormsThemeApplier(_ => throw new InvalidOperationException("test"));

        bool result = applier.TryApply(EffectiveTheme.Dark);

        Assert.False(result);
    }
}