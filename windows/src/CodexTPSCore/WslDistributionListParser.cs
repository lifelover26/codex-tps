using System;
using System.Collections.Generic;
using System.Linq;

namespace CodexTPSCore;

// Parses the stdout of `wsl.exe --list --quiet` into a clean, deduplicated,
// deterministically ordered list of distribution names.
//
// Real wsl.exe --list output is encoded as UTF-16LE and may contain:
//   - NUL bytes (one between every ASCII byte when decoded as UTF-8)
//   - CR, LF, and CRLF line endings
//   - Blank lines
//   - A trailing line with no content
// This parser strips NULs defensively, splits on LF, trims CR/whitespace,
// drops empty entries, dedupes with an ordinal comparer, and returns the
// names sorted with StringComparer.Ordinal so output is stable across runs
// regardless of dictionary iteration order.
public static class WslDistributionListParser
{
    public static IReadOnlyList<string> Parse(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return Array.Empty<string>();
        }

        // Strip NULs that appear when UTF-16LE bytes are decoded as UTF-8.
        var cleaned = raw.Replace("\0", string.Empty);
        // Normalize CRLF to LF so the split does not leave trailing CR on each
        // line; also handle a lone trailing CR.
        cleaned = cleaned.Replace("\r\n", "\n").Replace('\r', '\n');

        var lines = cleaned.Split('\n');
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            // Trim spaces and tabs only. Do not trim other whitespace that
            // might legitimately be inside a name (none expected, but safe).
            var name = line.Trim(' ', '\t');
            if (name.Length == 0)
            {
                continue;
            }
            seen.Add(name);
        }

        var sorted = seen.ToList();
        sorted.Sort(StringComparer.Ordinal);
        return sorted;
    }
}
