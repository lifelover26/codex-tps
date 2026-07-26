using System;

namespace CodexTPSCore;

// Validates a WSL distribution name before it is passed to wsl.exe via
// --distribution. Rejects anything that could be misinterpreted as a path
// component or that wsl.exe itself would refuse:
//   - null, empty, or whitespace-only
//   - leading or trailing whitespace (callers must Trim before calling; a
//     name with surrounding whitespace would otherwise be persisted verbatim
//     and break equality comparisons across runs)
//   - "." or ".." (path traversal)
//   - any control character (tab, newline, NUL, etc.)
//   - forward or backward slashes (path separators)
//
// Internal spaces are intentionally allowed: real distribution names can
// contain spaces ("My Distro"), and ProcessStartInfo.ArgumentList keeps a
// name with spaces as a single argv element. Quotes are also allowed
// because ArgumentList escapes them; wsl.exe unescapes and receives the
// literal name.
//
// Callers that obtain a name from untrusted input (e.g. user-typed strings)
// must Trim before calling IsValid. WslDistributionListParser already Trims
// list output, and CodexDataSourceSelection.ForWsl Trims its input before
// validating, so the stored name is always normalized.
public static class WslDistributionNameValidator
{
    public static bool IsValid(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }
        // Reject unnormalized input: a name with leading or trailing
        // whitespace would persist verbatim and break value equality. This
        // forces every caller to Trim before storing.
        if (name != name.Trim())
        {
            return false;
        }
        if (name == "." || name == "..")
        {
            return false;
        }
        foreach (var ch in name)
        {
            if (char.IsControl(ch))
            {
                return false;
            }
            if (ch == '/' || ch == '\\')
            {
                return false;
            }
        }
        return true;
    }
}
