using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace CodexTPSCore;

// Converts an absolute Linux path returned by `sh -lc 'printf "%s" "..."'`
// inside a WSL distribution into a Windows UNC path under
// \\wsl.localhost\<distribution>\<linux path>. The resulting UNC is what
// .NET APIs (Directory.Exists, Path.Combine) consume to reach the Linux
// filesystem from Windows.
//
// Validation rules (per the data-source contract):
//   - distributionName must pass WslDistributionNameValidator.IsValid. Without
//     this guard a malicious name could break the UNC structure (e.g. inject
//     an extra path component). WslCodexHomeDiscovery validates the name
//     before calling, but the converter defends itself too.
//   - linuxPath must be non-empty and start with '/' (absolute Linux path).
//   - linuxPath must not contain NUL.
//   - No path segment may contain control characters.
//   - No path segment may contain a backslash. Linux permits '\' in file
//     names, but after conversion the UNC uses '\' as its separator, so any
//     embedded backslash would either inject an extra component or escape
//     the intended subtree (e.g. "/home/user\..\etc" or "/home/foo\bar").
//     Reject outright; do not attempt to sanitize.
//   - No path segment may be ".." (path traversal) or "." (redundant).
//
// Conversion rules:
//   - The leading '/' is consumed and replaced by the
//     \\wsl.localhost\<distribution>\ prefix.
//   - Remaining '/' separators become single '\'. Interior empty segments
//     (e.g. "/home//user") are skipped so the UNC never contains "\\" runs.
//   - Root ("/") maps to \\wsl.localhost\<distribution>\ (with trailing
//     backslash preserved so Path.Combine works as expected).
//   - For non-root paths, trailing slashes are dropped so CodexHome is stable
//     regardless of whether the Linux path ended with '/'.
public static class WslLinuxPathConverter
{
    private const string UncPrefix = @"\\wsl.localhost\";

    public static bool TryConvertToUnc(
        string? linuxPath,
        string distributionName,
        [NotNullWhen(true)] out string? uncPath)
    {
        uncPath = null;

        // Reuse the canonical validator so the converter and the data-source
        // selection enforce identical rules. No duplicated logic here.
        if (!WslDistributionNameValidator.IsValid(distributionName))
        {
            return false;
        }

        if (string.IsNullOrEmpty(linuxPath))
        {
            return false;
        }
        if (linuxPath[0] != '/')
        {
            return false;
        }
        if (linuxPath.Contains('\0'))
        {
            return false;
        }

        // Validate and collect non-empty segments. Splitting on '/' yields an
        // empty string for the leading slash and for any doubled slash; both
        // are structurally valid in Linux, so skip empty segments rather than
        // rejecting them, and rebuild the UNC from the surviving segments so
        // the result never contains "\\" runs.
        var rawSegments = linuxPath.Split('/');
        var segments = new List<string>(rawSegments.Length);
        foreach (var segment in rawSegments)
        {
            if (segment.Length == 0)
            {
                continue;
            }
            if (segment == "." || segment == "..")
            {
                return false;
            }
            foreach (var ch in segment)
            {
                if (char.IsControl(ch))
                {
                    return false;
                }
                // A backslash in a Linux file name is legal on the Linux side,
                // but once we emit a UNC path the backslash is the path
                // separator. Any embedded backslash would let a malicious or
                // polluted path escape the intended subtree, so reject the
                // whole path outright instead of trying to sanitize it.
                if (ch == '\\')
                {
                    return false;
                }
            }
            segments.Add(segment);
        }

        string unc;
        if (segments.Count == 0)
        {
            // Root of the distribution filesystem. This also covers paths that
            // are all slashes (e.g. "///") after empty-segment skipping.
            unc = UncPrefix + distributionName + @"\";
        }
        else
        {
            // Join surviving segments with a single backslash. No trailing
            // separator, so CodexHome is stable regardless of trailing slashes.
            unc = UncPrefix + distributionName + @"\" + string.Join('\\', segments);
        }

        uncPath = unc;
        return true;
    }
}
