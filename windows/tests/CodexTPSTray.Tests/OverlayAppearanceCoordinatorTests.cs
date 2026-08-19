using System.Collections.Generic;
using System.Windows;
using CodexTPSCore;
using Xunit;

namespace CodexTPSTray.Tests;

public class OverlayAppearanceCoordinatorTests
{
    private static MonitorInfo Monitor(string stableId, bool isPrimary = false)
        => new(@"\\.\DISPLAY1", new Rect(0, 0, 1920, 1040), isPrimary, StableId: stableId);

    private static MonitorInfo MonitorA => Monitor("physical-a", true);
    private static MonitorInfo MonitorB => Monitor("physical-b", false);

    private static OverlayAppearanceState Appearance(
        OverlayThemePreference theme,
        OverlayOpacityPreference opacity)
        => new(theme, opacity);

    // --- Shared mode: resolve ---

    [Fact]
    public void ResolveEffectiveAppearance_SharedMode_ReturnsSharedAppearance()
    {
        var settings = TraySettings.Default with
        {
            AppearanceMemoryMode = OverlayAppearanceMemoryMode.SharedAcrossDisplays,
            SharedAppearance = Appearance(OverlayThemePreference.Dark, OverlayOpacityPreference.Percent55)
        };

        var result = OverlayAppearanceCoordinator.ResolveEffectiveAppearance(
            settings, MonitorA, out bool inherited);

        Assert.Equal(OverlayThemePreference.Dark, result.ThemePreference);
        Assert.Equal(OverlayOpacityPreference.Percent55, result.OpacityPreference);
        Assert.False(inherited);
    }

    [Fact]
    public void ResolveEffectiveAppearance_SharedMode_NullSharedAppearance_FallsBackToDefault()
    {
        var settings = TraySettings.Default with
        {
            AppearanceMemoryMode = OverlayAppearanceMemoryMode.SharedAcrossDisplays,
            SharedAppearance = null
        };

        var result = OverlayAppearanceCoordinator.ResolveEffectiveAppearance(
            settings, MonitorA, out _);

        Assert.Equal(OverlayThemePreference.FollowApplication, result.ThemePreference);
        Assert.Equal(OverlayOpacityPreference.Default, result.OpacityPreference);
    }

    // --- Per-display mode: resolve ---

    [Fact]
    public void ResolveEffectiveAppearance_PerDisplay_ExistingRecord_ReturnsRecord()
    {
        var settings = TraySettings.Default with
        {
            AppearanceMemoryMode = OverlayAppearanceMemoryMode.RememberPerDisplay,
            SharedAppearance = Appearance(OverlayThemePreference.Light, OverlayOpacityPreference.Default),
            PerDisplayAppearances = new Dictionary<string, OverlayAppearanceState>
            {
                ["physical-a"] = Appearance(OverlayThemePreference.Dark, OverlayOpacityPreference.Percent70)
            }
        };

        var result = OverlayAppearanceCoordinator.ResolveEffectiveAppearance(
            settings, MonitorA, out bool inherited);

        Assert.Equal(OverlayThemePreference.Dark, result.ThemePreference);
        Assert.Equal(OverlayOpacityPreference.Percent70, result.OpacityPreference);
        Assert.False(inherited);
    }

    [Fact]
    public void ResolveEffectiveAppearance_PerDisplay_NoRecord_InheritsFromShared()
    {
        var settings = TraySettings.Default with
        {
            AppearanceMemoryMode = OverlayAppearanceMemoryMode.RememberPerDisplay,
            SharedAppearance = Appearance(OverlayThemePreference.Dark, OverlayOpacityPreference.Percent55),
            PerDisplayAppearances = new Dictionary<string, OverlayAppearanceState>
            {
                ["physical-a"] = Appearance(OverlayThemePreference.Light, OverlayOpacityPreference.Default)
            }
        };

        var result = OverlayAppearanceCoordinator.ResolveEffectiveAppearance(
            settings, MonitorB, out bool inherited);

        Assert.Equal(OverlayThemePreference.Dark, result.ThemePreference);
        Assert.Equal(OverlayOpacityPreference.Percent55, result.OpacityPreference);
        Assert.True(inherited);
    }

    // --- Save theme: preserves opacity ---

    [Fact]
    public void SaveFromThemeSelection_PerDisplay_PreservesOpacity()
    {
        var settings = TraySettings.Default with
        {
            AppearanceMemoryMode = OverlayAppearanceMemoryMode.RememberPerDisplay,
            SharedAppearance = Appearance(OverlayThemePreference.Light, OverlayOpacityPreference.Percent55),
            PerDisplayAppearances = new Dictionary<string, OverlayAppearanceState>
            {
                ["physical-a"] = Appearance(OverlayThemePreference.Dark, OverlayOpacityPreference.Percent55)
            }
        };

        var updated = OverlayAppearanceCoordinator.SaveFromThemeSelection(
            settings, OverlayThemePreference.Light, MonitorA);

        var record = updated.PerDisplayAppearances!["physical-a"];
        Assert.Equal(OverlayThemePreference.Light, record.ThemePreference);
        Assert.Equal(OverlayOpacityPreference.Percent55, record.OpacityPreference);
    }

    [Fact]
    public void SaveFromThemeSelection_SharedMode_UpdatesSharedAppearance()
    {
        var settings = TraySettings.Default with
        {
            AppearanceMemoryMode = OverlayAppearanceMemoryMode.SharedAcrossDisplays,
            SharedAppearance = Appearance(OverlayThemePreference.Light, OverlayOpacityPreference.Default)
        };

        var updated = OverlayAppearanceCoordinator.SaveFromThemeSelection(
            settings, OverlayThemePreference.Dark, MonitorA);

        Assert.Equal(OverlayThemePreference.Dark, updated.SharedAppearance!.ThemePreference);
        Assert.Equal(OverlayOpacityPreference.Default, updated.SharedAppearance.OpacityPreference);
    }

    [Fact]
    public void SaveFromThemeSelection_UpdatesScalarOverlayTheme()
    {
        var settings = TraySettings.Default with
        {
            SharedAppearance = Appearance(OverlayThemePreference.Light, OverlayOpacityPreference.Percent55)
        };

        var updated = OverlayAppearanceCoordinator.SaveFromThemeSelection(
            settings, OverlayThemePreference.Dark, MonitorA);

        Assert.Equal(OverlayThemePreference.Dark, updated.OverlayTheme);
        Assert.Equal(OverlayOpacityPreference.Percent55, updated.OverlayOpacity);
    }

    // --- Save opacity: preserves theme ---

    [Fact]
    public void SaveFromOpacitySelection_PerDisplay_PreservesTheme()
    {
        var settings = TraySettings.Default with
        {
            AppearanceMemoryMode = OverlayAppearanceMemoryMode.RememberPerDisplay,
            PerDisplayAppearances = new Dictionary<string, OverlayAppearanceState>
            {
                ["physical-a"] = Appearance(OverlayThemePreference.Dark, OverlayOpacityPreference.Percent55)
            }
        };

        var updated = OverlayAppearanceCoordinator.SaveFromOpacitySelection(
            settings, OverlayOpacityPreference.Opaque, MonitorA);

        var record = updated.PerDisplayAppearances!["physical-a"];
        Assert.Equal(OverlayThemePreference.Dark, record.ThemePreference);
        Assert.Equal(OverlayOpacityPreference.Opaque, record.OpacityPreference);
    }

    [Fact]
    public void SaveFromOpacitySelection_UpdatesScalarOverlayOpacity()
    {
        var settings = TraySettings.Default with
        {
            SharedAppearance = Appearance(OverlayThemePreference.Dark, OverlayOpacityPreference.Percent55)
        };

        var updated = OverlayAppearanceCoordinator.SaveFromOpacitySelection(
            settings, OverlayOpacityPreference.Opaque, MonitorA);

        Assert.Equal(OverlayOpacityPreference.Opaque, updated.OverlayOpacity);
        Assert.Equal(OverlayThemePreference.Dark, updated.OverlayTheme);
    }

    // --- Preference preservation: FollowApplication and System ---

    [Fact]
    public void SaveFromThemeSelection_FollowApplication_SavedAsPreference()
    {
        var settings = TraySettings.Default with
        {
            SharedAppearance = Appearance(OverlayThemePreference.Dark, OverlayOpacityPreference.Default)
        };

        var updated = OverlayAppearanceCoordinator.SaveFromThemeSelection(
            settings, OverlayThemePreference.FollowApplication, MonitorA);

        Assert.Equal(OverlayThemePreference.FollowApplication, updated.SharedAppearance!.ThemePreference);
    }

    [Fact]
    public void SaveFromThemeSelection_System_SavedAsPreference()
    {
        var settings = TraySettings.Default with
        {
            SharedAppearance = Appearance(OverlayThemePreference.Dark, OverlayOpacityPreference.Default)
        };

        var updated = OverlayAppearanceCoordinator.SaveFromThemeSelection(
            settings, OverlayThemePreference.System, MonitorA);

        Assert.Equal(OverlayThemePreference.System, updated.SharedAppearance!.ThemePreference);
    }

    // --- Mode switch: Shared -> PerDisplay ---

    [Fact]
    public void SwitchMemoryMode_SharedToPerDisplay_WritesCurrentMonitorRecord()
    {
        var settings = TraySettings.Default with
        {
            AppearanceMemoryMode = OverlayAppearanceMemoryMode.SharedAcrossDisplays,
            SharedAppearance = Appearance(OverlayThemePreference.Dark, OverlayOpacityPreference.Percent55)
        };

        var updated = OverlayAppearanceCoordinator.SwitchMemoryMode(
            settings, OverlayAppearanceMemoryMode.RememberPerDisplay, MonitorA);

        Assert.Equal(OverlayAppearanceMemoryMode.RememberPerDisplay, updated.AppearanceMemoryMode);
        var record = updated.PerDisplayAppearances!["physical-a"];
        Assert.Equal(OverlayThemePreference.Dark, record.ThemePreference);
        Assert.Equal(OverlayOpacityPreference.Percent55, record.OpacityPreference);
    }

    [Fact]
    public void SwitchMemoryMode_SharedToPerDisplay_PreservesExistingRecords()
    {
        var settings = TraySettings.Default with
        {
            AppearanceMemoryMode = OverlayAppearanceMemoryMode.SharedAcrossDisplays,
            SharedAppearance = Appearance(OverlayThemePreference.Dark, OverlayOpacityPreference.Percent55),
            PerDisplayAppearances = new Dictionary<string, OverlayAppearanceState>
            {
                ["physical-b"] = Appearance(OverlayThemePreference.Light, OverlayOpacityPreference.Default)
            }
        };

        var updated = OverlayAppearanceCoordinator.SwitchMemoryMode(
            settings, OverlayAppearanceMemoryMode.RememberPerDisplay, MonitorA);

        Assert.Equal(OverlayThemePreference.Light,
            updated.PerDisplayAppearances!["physical-b"].ThemePreference);
    }

    // --- Mode switch: PerDisplay -> Shared ---

    [Fact]
    public void SwitchMemoryMode_PerDisplayToShared_SetsSharedFromCurrentMonitor()
    {
        var settings = TraySettings.Default with
        {
            AppearanceMemoryMode = OverlayAppearanceMemoryMode.RememberPerDisplay,
            SharedAppearance = Appearance(OverlayThemePreference.Light, OverlayOpacityPreference.Default),
            PerDisplayAppearances = new Dictionary<string, OverlayAppearanceState>
            {
                ["physical-a"] = Appearance(OverlayThemePreference.Dark, OverlayOpacityPreference.Percent70)
            }
        };

        var updated = OverlayAppearanceCoordinator.SwitchMemoryMode(
            settings, OverlayAppearanceMemoryMode.SharedAcrossDisplays, MonitorA);

        Assert.Equal(OverlayAppearanceMemoryMode.SharedAcrossDisplays, updated.AppearanceMemoryMode);
        Assert.Equal(OverlayThemePreference.Dark, updated.SharedAppearance!.ThemePreference);
        Assert.Equal(OverlayOpacityPreference.Percent70, updated.SharedAppearance.OpacityPreference);
    }

    [Fact]
    public void SwitchMemoryMode_PerDisplayToShared_PreservesPerDisplayRecords()
    {
        var settings = TraySettings.Default with
        {
            AppearanceMemoryMode = OverlayAppearanceMemoryMode.RememberPerDisplay,
            PerDisplayAppearances = new Dictionary<string, OverlayAppearanceState>
            {
                ["physical-a"] = Appearance(OverlayThemePreference.Dark, OverlayOpacityPreference.Percent70),
                ["physical-b"] = Appearance(OverlayThemePreference.Light, OverlayOpacityPreference.Default)
            }
        };

        var updated = OverlayAppearanceCoordinator.SwitchMemoryMode(
            settings, OverlayAppearanceMemoryMode.SharedAcrossDisplays, MonitorA);

        Assert.Equal(OverlayThemePreference.Dark,
            updated.PerDisplayAppearances!["physical-a"].ThemePreference);
        Assert.Equal(OverlayThemePreference.Light,
            updated.PerDisplayAppearances!["physical-b"].ThemePreference);
    }

    [Fact]
    public void SwitchMemoryMode_SameMode_IsNoOp()
    {
        var settings = TraySettings.Default with
        {
            AppearanceMemoryMode = OverlayAppearanceMemoryMode.SharedAcrossDisplays
        };

        var updated = OverlayAppearanceCoordinator.SwitchMemoryMode(
            settings, OverlayAppearanceMemoryMode.SharedAcrossDisplays, MonitorA);

        Assert.Equal(settings, updated);
    }

    // --- RestoreForMonitor: first-seen inheritance ---

    [Fact]
    public void RestoreForMonitor_PerDisplay_FirstSeen_InheritsAndCreatesRecord()
    {
        var settings = TraySettings.Default with
        {
            AppearanceMemoryMode = OverlayAppearanceMemoryMode.RememberPerDisplay,
            SharedAppearance = Appearance(OverlayThemePreference.Dark, OverlayOpacityPreference.Percent55),
            PerDisplayAppearances = new Dictionary<string, OverlayAppearanceState>
            {
                ["physical-a"] = Appearance(OverlayThemePreference.Light, OverlayOpacityPreference.Default)
            }
        };

        var updated = OverlayAppearanceCoordinator.RestoreForMonitor(settings, MonitorB);

        Assert.Equal(OverlayThemePreference.Dark,
            updated.PerDisplayAppearances!["physical-b"].ThemePreference);
        Assert.Equal(OverlayOpacityPreference.Percent55,
            updated.PerDisplayAppearances!["physical-b"].OpacityPreference);
    }

    [Fact]
    public void RestoreForMonitor_PerDisplay_ExistingRecord_NotOverwrittenByShared()
    {
        var settings = TraySettings.Default with
        {
            AppearanceMemoryMode = OverlayAppearanceMemoryMode.RememberPerDisplay,
            SharedAppearance = Appearance(OverlayThemePreference.Dark, OverlayOpacityPreference.Percent55),
            PerDisplayAppearances = new Dictionary<string, OverlayAppearanceState>
            {
                ["physical-a"] = Appearance(OverlayThemePreference.Light, OverlayOpacityPreference.Default)
            }
        };

        var updated = OverlayAppearanceCoordinator.RestoreForMonitor(settings, MonitorA);

        Assert.Equal(OverlayThemePreference.Light,
            updated.PerDisplayAppearances!["physical-a"].ThemePreference);
        Assert.Equal(OverlayOpacityPreference.Default,
            updated.PerDisplayAppearances!["physical-a"].OpacityPreference);
    }

    [Fact]
    public void RestoreForMonitor_SyncsScalarsToEffectiveAppearance()
    {
        var settings = TraySettings.Default with
        {
            AppearanceMemoryMode = OverlayAppearanceMemoryMode.RememberPerDisplay,
            SharedAppearance = Appearance(OverlayThemePreference.Dark, OverlayOpacityPreference.Percent55),
            PerDisplayAppearances = new Dictionary<string, OverlayAppearanceState>
            {
                ["physical-a"] = Appearance(OverlayThemePreference.Light, OverlayOpacityPreference.Opaque)
            },
            OverlayTheme = OverlayThemePreference.Dark,
            OverlayOpacity = OverlayOpacityPreference.Percent55
        };

        var updated = OverlayAppearanceCoordinator.RestoreForMonitor(settings, MonitorA);

        Assert.Equal(OverlayThemePreference.Light, updated.OverlayTheme);
        Assert.Equal(OverlayOpacityPreference.Opaque, updated.OverlayOpacity);
    }

    [Fact]
    public void RestoreForMonitor_SharedMode_DoesNotCreateRecord()
    {
        var settings = TraySettings.Default with
        {
            AppearanceMemoryMode = OverlayAppearanceMemoryMode.SharedAcrossDisplays,
            SharedAppearance = Appearance(OverlayThemePreference.Dark, OverlayOpacityPreference.Percent55)
        };

        var updated = OverlayAppearanceCoordinator.RestoreForMonitor(settings, MonitorA);

        Assert.Null(updated.PerDisplayAppearances);
        Assert.Equal(OverlayThemePreference.Dark, updated.OverlayTheme);
    }

    // --- Per-display A/B different appearance ---

    [Fact]
    public void PerDisplay_AB_DifferentAppearance_RestoreCorrectly()
    {
        var settings = TraySettings.Default with
        {
            AppearanceMemoryMode = OverlayAppearanceMemoryMode.RememberPerDisplay,
            SharedAppearance = Appearance(OverlayThemePreference.Light, OverlayOpacityPreference.Default),
            PerDisplayAppearances = new Dictionary<string, OverlayAppearanceState>
            {
                ["physical-a"] = Appearance(OverlayThemePreference.Dark, OverlayOpacityPreference.Percent55),
                ["physical-b"] = Appearance(OverlayThemePreference.Light, OverlayOpacityPreference.Opaque)
            }
        };

        var restoredA = OverlayAppearanceCoordinator.RestoreForMonitor(settings, MonitorA);
        Assert.Equal(OverlayThemePreference.Dark, restoredA.OverlayTheme);
        Assert.Equal(OverlayOpacityPreference.Percent55, restoredA.OverlayOpacity);

        var restoredB = OverlayAppearanceCoordinator.RestoreForMonitor(settings, MonitorB);
        Assert.Equal(OverlayThemePreference.Light, restoredB.OverlayTheme);
        Assert.Equal(OverlayOpacityPreference.Opaque, restoredB.OverlayOpacity);
    }

    // --- GetActive preferences ---

    [Fact]
    public void GetActiveThemePreference_PerDisplay_ReturnsMonitorRecord()
    {
        var settings = TraySettings.Default with
        {
            AppearanceMemoryMode = OverlayAppearanceMemoryMode.RememberPerDisplay,
            PerDisplayAppearances = new Dictionary<string, OverlayAppearanceState>
            {
                ["physical-a"] = Appearance(OverlayThemePreference.Dark, OverlayOpacityPreference.Percent55)
            }
        };

        Assert.Equal(OverlayThemePreference.Dark,
            OverlayAppearanceCoordinator.GetActiveThemePreference(settings, MonitorA));
        Assert.Equal(OverlayOpacityPreference.Percent55,
            OverlayAppearanceCoordinator.GetActiveOpacityPreference(settings, MonitorA));
    }

    // --- ApplyMemoryModeSwitch (no monitor context) ---

    [Fact]
    public void ApplyMemoryModeSwitch_SameMode_IsNoOp()
    {
        var settings = TraySettings.Default with
        {
            AppearanceMemoryMode = OverlayAppearanceMemoryMode.SharedAcrossDisplays
        };

        var updated = OverlayAppearanceCoordinator.ApplyMemoryModeSwitch(
            settings, OverlayAppearanceMemoryMode.SharedAcrossDisplays);

        Assert.Equal(settings, updated);
    }

    [Fact]
    public void ApplyMemoryModeSwitch_DifferentMode_ChangesMode()
    {
        var settings = TraySettings.Default with
        {
            AppearanceMemoryMode = OverlayAppearanceMemoryMode.SharedAcrossDisplays
        };

        var updated = OverlayAppearanceCoordinator.ApplyMemoryModeSwitch(
            settings, OverlayAppearanceMemoryMode.RememberPerDisplay);

        Assert.Equal(OverlayAppearanceMemoryMode.RememberPerDisplay, updated.AppearanceMemoryMode);
    }

    // --- Save updates SharedAppearance as inheritance seed in per-display mode ---

    [Fact]
    public void SaveFromThemeSelection_PerDisplay_UpdatesSharedAppearanceAsSeed()
    {
        var settings = TraySettings.Default with
        {
            AppearanceMemoryMode = OverlayAppearanceMemoryMode.RememberPerDisplay,
            SharedAppearance = Appearance(OverlayThemePreference.Light, OverlayOpacityPreference.Default),
            PerDisplayAppearances = new Dictionary<string, OverlayAppearanceState>
            {
                ["physical-a"] = Appearance(OverlayThemePreference.Dark, OverlayOpacityPreference.Percent55)
            }
        };

        var updated = OverlayAppearanceCoordinator.SaveFromThemeSelection(
            settings, OverlayThemePreference.Light, MonitorA);

        // SharedAppearance is updated to the new state as the inheritance seed.
        Assert.Equal(OverlayThemePreference.Light, updated.SharedAppearance!.ThemePreference);
        Assert.Equal(OverlayOpacityPreference.Percent55, updated.SharedAppearance.OpacityPreference);
    }

    [Fact]
    public void FirstSeenDisplayInheritsLatestSharedAppearance()
    {
        var settings = TraySettings.Default with
        {
            AppearanceMemoryMode = OverlayAppearanceMemoryMode.RememberPerDisplay,
            SharedAppearance = Appearance(OverlayThemePreference.Dark, OverlayOpacityPreference.Percent55),
            PerDisplayAppearances = new Dictionary<string, OverlayAppearanceState>
            {
                ["physical-a"] = Appearance(OverlayThemePreference.Dark, OverlayOpacityPreference.Percent55)
            }
        };

        // User changes monitor A's theme to Light. SharedAppearance becomes the seed.
        settings = OverlayAppearanceCoordinator.SaveFromThemeSelection(
            settings, OverlayThemePreference.Light, MonitorA);

        // A new monitor B appears and inherits from the seed.
        var restored = OverlayAppearanceCoordinator.RestoreForMonitor(settings, MonitorB);
        Assert.Equal(OverlayThemePreference.Light,
            restored.PerDisplayAppearances!["physical-b"].ThemePreference);
        Assert.Equal(OverlayOpacityPreference.Percent55,
            restored.PerDisplayAppearances!["physical-b"].OpacityPreference);
    }
}
