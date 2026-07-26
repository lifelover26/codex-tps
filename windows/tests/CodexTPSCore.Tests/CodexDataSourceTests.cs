using System;
using System.Collections.Generic;
using System.Text.Json;
using CodexTPSCore;
using Xunit;

namespace CodexTPSCore.Tests;

public class CodexDataSourceTests
{
    [Fact]
    public void WindowsSelectionHasNoDistributionName()
    {
        var selection = CodexDataSourceSelection.Windows;
        Assert.Equal(CodexDataSourceKind.Windows, selection.Kind);
        Assert.Null(selection.WslDistributionName);
    }

    [Fact]
    public void ForWslCarriesDistributionName()
    {
        var selection = CodexDataSourceSelection.ForWsl("Ubuntu");
        Assert.Equal(CodexDataSourceKind.Wsl, selection.Kind);
        Assert.Equal("Ubuntu", selection.WslDistributionName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ForWslRejectsNullOrWhitespaceName(string? name)
    {
        Assert.Throws<ArgumentException>(() => CodexDataSourceSelection.ForWsl(name!));
    }

    [Theory]
    [InlineData("foo/bar")]
    [InlineData("foo\\bar")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("foo\tbar")]
    [InlineData("foo\nbar")]
    [InlineData("foo\rbar")]
    [InlineData("foo\0bar")]
    public void ForWslRejectsInvalidDistributionName(string name)
    {
        Assert.Throws<ArgumentException>(() => CodexDataSourceSelection.ForWsl(name));
    }

    [Fact]
    public void ForWslTrimsSurroundingWhitespaceAndStoresNormalized()
    {
        // The selection must store the trimmed name so that two selections
        // built from "  Ubuntu  " and "Ubuntu" compare equal and persist
        // identically. The validator rejects unnormalized input, so the
        // constructor must Trim before validating.
        var selection = CodexDataSourceSelection.ForWsl("  Ubuntu  ");
        Assert.Equal("Ubuntu", selection.WslDistributionName);
    }

    [Fact]
    public void ConstructorRejectsWindowsWithNonNullDistributionName()
    {
        Assert.Throws<ArgumentException>(() =>
            new CodexDataSourceSelection(CodexDataSourceKind.Windows, "Ubuntu"));
    }

    [Fact]
    public void ConstructorRejectsWslWithNullDistributionName()
    {
        Assert.Throws<ArgumentException>(() =>
            new CodexDataSourceSelection(CodexDataSourceKind.Wsl, null));
    }

    [Fact]
    public void ConstructorRejectsWslWithEmptyDistributionName()
    {
        Assert.Throws<ArgumentException>(() =>
            new CodexDataSourceSelection(CodexDataSourceKind.Wsl, ""));
    }

    [Fact]
    public void ConstructorRejectsWslWithWhitespaceOnlyDistributionName()
    {
        Assert.Throws<ArgumentException>(() =>
            new CodexDataSourceSelection(CodexDataSourceKind.Wsl, "   "));
    }

    [Fact]
    public void ConstructorRejectsWslWithSlashInDistributionName()
    {
        Assert.Throws<ArgumentException>(() =>
            new CodexDataSourceSelection(CodexDataSourceKind.Wsl, "foo/bar"));
    }

    [Fact]
    public void ConstructorRejectsWslWithBackslashInDistributionName()
    {
        Assert.Throws<ArgumentException>(() =>
            new CodexDataSourceSelection(CodexDataSourceKind.Wsl, "foo\\bar"));
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    public void ConstructorRejectsWslWithTraversalLikeDistributionName(string name)
    {
        Assert.Throws<ArgumentException>(() =>
            new CodexDataSourceSelection(CodexDataSourceKind.Wsl, name));
    }

    [Fact]
    public void ConstructorRejectsWslWithControlCharacterInDistributionName()
    {
        Assert.Throws<ArgumentException>(() =>
            new CodexDataSourceSelection(CodexDataSourceKind.Wsl, "foo\tbar"));
    }

    [Fact]
    public void ConstructorRejectsWslWithUntrimmedDistributionName()
    {
        // The validator rejects unnormalized input; the constructor must Trim
        // before validating so " Ubuntu " is accepted as "Ubuntu". This test
        // documents that direct callers of the public constructor are also
        // normalized (not just ForWsl).
        var selection = new CodexDataSourceSelection(CodexDataSourceKind.Wsl, " Ubuntu ");
        Assert.Equal("Ubuntu", selection.WslDistributionName);
    }

    [Fact]
    public void ConstructorRejectsUndefinedEnumValue()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CodexDataSourceSelection((CodexDataSourceKind)999, null));
    }

    [Fact]
    public void ConstructorRejectsUndefinedEnumValueEvenWithDistributionName()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CodexDataSourceSelection((CodexDataSourceKind)999, "Ubuntu"));
    }

    [Fact]
    public void ResolvedDataSourceCarriesAllFields()
    {
        var selection = CodexDataSourceSelection.ForWsl("Debian");
        var resolved = new ResolvedCodexDataSource(
            Selection: selection,
            DisplayName: "WSL: Debian",
            CodexHome: @"\\wsl.localhost\Debian\home\user\.codex");
        Assert.Same(selection, resolved.Selection);
        Assert.Equal("WSL: Debian", resolved.DisplayName);
        Assert.Equal(@"\\wsl.localhost\Debian\home\user\.codex", resolved.CodexHome);
    }

    [Fact]
    public void SelectionEqualityIsValueBased()
    {
        var a = CodexDataSourceSelection.ForWsl("Ubuntu");
        var b = CodexDataSourceSelection.ForWsl("Ubuntu");
        Assert.Equal(a, b);
        Assert.True(a == b);
    }

    [Fact]
    public void SelectionEqualityIsValueBasedAfterNormalization()
    {
        // Two inputs that differ only by surrounding whitespace must produce
        // equal selections because the stored name is normalized at creation.
        var a = CodexDataSourceSelection.ForWsl("  Ubuntu  ");
        var b = CodexDataSourceSelection.ForWsl("Ubuntu");
        Assert.Equal(a, b);
        Assert.True(a == b);
    }

    [Fact]
    public void WindowsAndWslSelectionsAreDistinct()
    {
        Assert.NotEqual(CodexDataSourceSelection.Windows, CodexDataSourceSelection.ForWsl("Ubuntu"));
    }

    [Fact]
    public void WithExpressionProducesExactClone()
    {
        // The init accessors are private, so a `with` expression cannot mutate
        // any property; it must produce an exact clone. This guarantees
        // validation cannot be bypassed via `with`.
        var original = CodexDataSourceSelection.ForWsl("Ubuntu");
        var clone = original with { };
        Assert.Equal(original, clone);
        Assert.Equal("Ubuntu", clone.WslDistributionName);
    }

    // ---------------------------------------------------------------------
    // System.Text.Json persistence contract.
    //
    // The on-disk shape MUST be exactly { Kind, WslDistributionName } so the
    // persisted blob stays stable across versions and round-trips through
    // deserialization without producing invalid objects. Property-set checks
    // use JsonDocument so they do not depend on textual property ordering.
    // ---------------------------------------------------------------------

    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNamingPolicy = null };

    private static HashSet<string> PropertyNames(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            names.Add(prop.Name);
        }
        return names;
    }

    [Fact]
    public void WindowsSelectionSerializesToOnlyKindAndWslDistributionName()
    {
        var json = JsonSerializer.Serialize(CodexDataSourceSelection.Windows, JsonOptions);
        var names = PropertyNames(json);
        Assert.Equal(new[] { "Kind", "WslDistributionName" }, names);
    }

    [Fact]
    public void WslSelectionSerializesToOnlyKindAndWslDistributionName()
    {
        // If a computed property such as NormalizedWslDistributionName were
        // re-added, this test would fail because it would appear in the set.
        var selection = CodexDataSourceSelection.ForWsl("Ubuntu");
        var json = JsonSerializer.Serialize(selection, JsonOptions);
        var names = PropertyNames(json);
        Assert.Equal(new[] { "Kind", "WslDistributionName" }, names);
    }

    [Fact]
    public void WindowsSelectionRoundTripsThroughJson()
    {
        var json = JsonSerializer.Serialize(CodexDataSourceSelection.Windows, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<CodexDataSourceSelection>(json, JsonOptions);
        Assert.NotNull(deserialized);
        Assert.Equal(CodexDataSourceSelection.Windows, deserialized);
    }

    [Fact]
    public void WslSelectionRoundTripsThroughJson()
    {
        var original = CodexDataSourceSelection.ForWsl("Ubuntu");
        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<CodexDataSourceSelection>(json, JsonOptions);
        Assert.NotNull(deserialized);
        Assert.Equal(original, deserialized);
        Assert.Equal("Ubuntu", deserialized!.WslDistributionName);
    }

    [Theory]
    [InlineData(@"{""Kind"":0,""WslDistributionName"":""Ubuntu""}")]
    public void DeserializationRejectsWindowsWithNonNullDistributionName(string json)
    {
        // The constructor's Windows branch rejects non-null names, so a
        // tampered or corrupted JSON blob cannot produce a Windows selection
        // carrying a distribution name. System.Text.Json in .NET 10 lets the
        // constructor's ArgumentException propagate to the caller.
        Assert.Throws<ArgumentException>(() =>
            JsonSerializer.Deserialize<CodexDataSourceSelection>(json, JsonOptions));
    }

    [Theory]
    [InlineData(@"{""Kind"":1,""WslDistributionName"":null}")]
    [InlineData(@"{""Kind"":1,""WslDistributionName"":""""}")]
    [InlineData(@"{""Kind"":1,""WslDistributionName"":""   ""}")]
    [InlineData(@"{""Kind"":1,""WslDistributionName"":""foo/bar""}")]
    [InlineData(@"{""Kind"":1,""WslDistributionName"":""foo\\bar""}")]
    [InlineData(@"{""Kind"":1,""WslDistributionName"":"".""}")]
    [InlineData(@"{""Kind"":1,""WslDistributionName"":""..""}")]
    public void DeserializationRejectsInvalidWslCombinations(string json)
    {
        // The constructor's Wsl branch rejects null/empty/whitespace and any
        // name that fails WslDistributionNameValidator. ArgumentException
        // propagates from the constructor through System.Text.Json.
        Assert.Throws<ArgumentException>(() =>
            JsonSerializer.Deserialize<CodexDataSourceSelection>(json, JsonOptions));
    }

    [Fact]
    public void DeserializationRejectsUndefinedEnumValue()
    {
        // The constructor's default branch throws ArgumentOutOfRangeException
        // for any value outside the defined enum. ArgumentOutOfRangeException
        // derives from ArgumentException; System.Text.Json in .NET 10 lets it
        // propagate to the caller.
        var json = @"{""Kind"":999,""WslDistributionName"":null}";
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            JsonSerializer.Deserialize<CodexDataSourceSelection>(json, JsonOptions));
    }

    [Fact]
    public void DeserializationNormalizesWslDistributionName()
    {
        // Even when reading from JSON, the constructor Trims the name so the
        // deserialized instance stores the normalized value and compares equal
        // to one constructed directly from the trimmed name.
        var json = @"{""Kind"":1,""WslDistributionName"":""  Ubuntu  ""}";
        var deserialized = JsonSerializer.Deserialize<CodexDataSourceSelection>(json, JsonOptions);
        Assert.NotNull(deserialized);
        Assert.Equal("Ubuntu", deserialized!.WslDistributionName);
        Assert.Equal(CodexDataSourceSelection.ForWsl("Ubuntu"), deserialized);
    }
}
