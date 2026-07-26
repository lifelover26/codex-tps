using System;

namespace CodexTPSCore;

// Stable, persistable user selection of where Codex session data comes from.
// Windows keeps the existing SessionScanner.DefaultCodexHome() semantics
// (CODEX_HOME env var or %USERPROFILE%\.codex). WSL adds the distribution
// name the user wants to read from. The selection intentionally carries no
// resolved runtime path: a WSL distribution's UNC prefix can change across
// reboots or after wsl --import/--export, so only the kind and distribution
// name are persisted; the UNC path is re-resolved on every run.
public enum CodexDataSourceKind
{
    Windows,
    Wsl
}

// Represents the user's durable choice. WslDistributionName is only meaningful
// when Kind == Wsl; for Windows it must be null. Callers must not persist any
// resolved UNC path alongside this selection.
//
// This type is immutable and self-validating: every constructor invocation
// (including by System.Text.Json deserialization) runs the same validation,
// so an instance can never represent an illegal combination such as
// "Windows + non-null distribution name" or "WSL + empty/slashy name". The
// init accessors are private so `with` expressions cannot bypass validation
// either; `with` only produces an exact clone.
//
// Persistence contract: the on-disk shape is exactly { Kind, WslDistributionName }.
// Do not add computed properties with public getters to this type — System.Text.Json
// would serialize them by default and the persisted shape would silently grow.
public sealed record CodexDataSourceSelection
{
    public CodexDataSourceKind Kind { get; private init; }

    // Already normalized (trimmed) at construction. Stored normalized so that
    // two selections created from "  Ubuntu  " and "Ubuntu" compare equal and
    // persist identically.
    public string? WslDistributionName { get; private init; }

    public CodexDataSourceSelection(CodexDataSourceKind kind, string? wslDistributionName = null)
    {
        switch (kind)
        {
            case CodexDataSourceKind.Windows:
                if (wslDistributionName is not null)
                {
                    throw new ArgumentException(
                        "A Windows data source must not carry a WSL distribution name.",
                        nameof(wslDistributionName));
                }
                break;

            case CodexDataSourceKind.Wsl:
                if (string.IsNullOrWhiteSpace(wslDistributionName))
                {
                    throw new ArgumentException(
                        "A WSL data source requires a non-empty distribution name.",
                        nameof(wslDistributionName));
                }
                var normalized = wslDistributionName!.Trim();
                if (!WslDistributionNameValidator.IsValid(normalized))
                {
                    throw new ArgumentException(
                        "Invalid WSL distribution name.",
                        nameof(wslDistributionName));
                }
                wslDistributionName = normalized;
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(kind), kind, "Undefined CodexDataSourceKind value.");
        }

        Kind = kind;
        WslDistributionName = wslDistributionName;
    }

    public static CodexDataSourceSelection Windows { get; } =
        new(CodexDataSourceKind.Windows, wslDistributionName: null);

    public static CodexDataSourceSelection ForWsl(string distributionName) =>
        new(CodexDataSourceKind.Wsl, distributionName);
}

// Represents a data source that has been resolved for the current run only.
// CodexHome is a concrete local path (Windows path or \\wsl.localhost\<dist>\...
// UNC path) and must never be persisted: re-resolve from the selection on the
// next launch instead. DisplayName is safe to show in UI/debug text and does
// not include the resolved Linux user name or private Linux path.
public sealed record ResolvedCodexDataSource(
    CodexDataSourceSelection Selection,
    string DisplayName,
    string CodexHome);
