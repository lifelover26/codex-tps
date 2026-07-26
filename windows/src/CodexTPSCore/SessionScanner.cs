using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace CodexTPSCore;

public class SessionScanner
{
    private static readonly byte[] TimestampPrefix = "{\"timestamp\":\""u8.ToArray();

    private const int ContinuityWindowSize = 4_096;

    private class FileCursor
    {
        public long Offset { get; set; } = 0;
        public byte[] Remainder { get; set; } = Array.Empty<byte>();
        public TokenParserState ParserState { get; } = new();
        public DateTimeOffset LastModified { get; set; } = DateTimeOffset.MinValue;
        // In-memory only: SHA-256 of the last up to ContinuityWindowSize bytes before
        // Offset. Never persisted, logged, or rendered. Used solely to detect mid-file
        // rewrites that the Offset/Length guard cannot catch.
        public byte[]? ContinuityDigest { get; set; }
    }

    private record SessionFile(
        string Path,
        long Size,
        DateTimeOffset ModifiedAt
    );

    public string CodexHome { get; }
    public string SessionsRoot { get; }

    private readonly double _activeFileHorizonSeconds;
    private readonly int _readChunkSize = 1_048_576;
    private readonly int _markerOverlapSize = 4_096;

    private readonly Dictionary<string, FileCursor> _cursors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _seenDeduplicationKeys = new(StringComparer.Ordinal);
    private readonly List<UsageEvent> _events = new();
    private int _malformedRelevantLines = 0;

    private readonly object _syncLock = new();

    public SessionScanner(
        string? codexHome = null,
        double activeFileHorizonSeconds = 65 * 60)
    {
        CodexHome = codexHome ?? DefaultCodexHome();
        SessionsRoot = Path.Combine(CodexHome, "sessions");
        _activeFileHorizonSeconds = activeFileHorizonSeconds;
    }

    public static string DefaultCodexHome()
    {
        var configured = Environment.GetEnvironmentVariable("CODEX_HOME")?.Trim();
        if (!string.IsNullOrEmpty(configured))
        {
            return Path.GetFullPath(configured);
        }
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(userProfile, ".codex");
    }

    public UsageSnapshot Refresh(DateTimeOffset now)
    {
        lock (_syncLock)
        {
            if (!Directory.Exists(SessionsRoot))
            {
                return UsageSnapshot.Empty(now, CollectionStatus.SessionsDirectoryMissing);
            }

            try
            {
                var retentionStart = now.AddSeconds(-65 * 60);
                var files = DiscoverSessionFiles(now);

                foreach (var file in files)
                {
                    if (ReadAppendedContent(file, retentionStart))
                    {
                        // Continuity broken: clear all scan-derived state and rebuild
                        // once from a fresh discovery pass. Old events and dedup keys
                        // are discarded because they may have been derived from the
                        // replaced file. At most one rebuild per Refresh.
                        _cursors.Clear();
                        _events.Clear();
                        _seenDeduplicationKeys.Clear();
                        _malformedRelevantLines = 0;

                        files = DiscoverSessionFiles(now);
                        foreach (var rebuildFile in files)
                        {
                            ReadAppendedContent(rebuildFile, retentionStart);
                        }
                        break;
                    }
                }

                // Clean up old events:
                _events.RemoveAll(e => e.Timestamp < retentionStart);

                // Clean up old deduplication keys:
                var expiredKeys = _seenDeduplicationKeys.Where(kvp => kvp.Value < retentionStart).Select(kvp => kvp.Key).ToList();
                foreach (var key in expiredKeys)
                {
                    _seenDeduplicationKeys.Remove(key);
                }

                var activeStart = now.AddSeconds(-2 * 60);
                int activeSessions = files.Count(f => f.ModifiedAt >= activeStart);

                return UsageMetricsCalculator.Snapshot(
                    events: _events,
                    now: now,
                    activeSessions: activeSessions,
                    malformedRelevantLines: _malformedRelevantLines,
                    status: CollectionStatus.Ready
                );
            }
            catch
            {
                return new UsageSnapshot(
                    GeneratedAt: now,
                    OneMinute: WindowMetrics.Empty(60),
                    FiveMinutes: WindowMetrics.Empty(300),
                    ThirtyMinutes: WindowMetrics.Empty(1800),
                    OneHour: WindowMetrics.Empty(3600),
                    ActiveSessions: 0,
                    MalformedRelevantLines: _malformedRelevantLines,
                    Status: CollectionStatus.ReadFailed
                );
            }
        }
    }

    private List<SessionFile> DiscoverSessionFiles(DateTimeOffset now)
    {
        var cutoff = now.AddSeconds(-_activeFileHorizonSeconds);
        var files = new List<SessionFile>();

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false,
            MatchCasing = MatchCasing.CaseInsensitive,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint
        };

        var sessionsDir = new DirectoryInfo(SessionsRoot);
        foreach (var fileInfo in sessionsDir.EnumerateFiles("*.jsonl", options))
        {
            var modifiedAt = fileInfo.LastWriteTimeUtc;
            if (modifiedAt >= cutoff)
            {
                files.Add(new SessionFile(fileInfo.FullName, fileInfo.Length, modifiedAt));
            }
        }

        return files.OrderBy(f => Path.GetFileName(f.Path), StringComparer.Ordinal)
                    .ThenBy(f => f.Path, StringComparer.Ordinal)
                    .ToList();
    }

    // Returns true when the cursor's content continuity is broken and the caller
    // must rebuild all scan-derived state once. Returns false after a normal read
    // (including the no-new-content case). The same FileStream is used for both
    // continuity verification and incremental reading to narrow the race window
    // between checking and opening the file.
    private bool ReadAppendedContent(SessionFile file, DateTimeOffset retentionStart)
    {
        if (!_cursors.TryGetValue(file.Path, out var cursor))
        {
            cursor = new FileCursor();
        }

        byte[] appendedBuffer = new byte[_readChunkSize];
        string fallbackSessionID = Path.GetFileNameWithoutExtension(file.Path);
        string minimumTimestamp = retentionStart.AddSeconds(-2).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        using (var stream = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            long actualLength = stream.Length;

            // Verify content continuity before reusing a non-zero cursor. This
            // catches same-length rewrites and truncate-then-regrow scenarios
            // that the Offset/Length guard alone cannot detect.
            if (cursor.Offset > 0)
            {
                if (actualLength < cursor.Offset || cursor.ContinuityDigest == null)
                {
                    return true;
                }

                if (!VerifyBoundaryDigest(stream, cursor.Offset, cursor.ContinuityDigest))
                {
                    return true;
                }
            }

            if (actualLength <= cursor.Offset)
            {
                cursor.LastModified = file.ModifiedAt;
                _cursors[file.Path] = cursor;
                return false;
            }

            stream.Seek(cursor.Offset, SeekOrigin.Begin);

            while (true)
            {
                int bytesRead = stream.Read(appendedBuffer, 0, appendedBuffer.Length);
                if (bytesRead <= 0)
                {
                    break;
                }

                byte[] appended = new byte[bytesRead];
                Buffer.BlockCopy(appendedBuffer, 0, appended, 0, bytesRead);

                byte[] combined = new byte[cursor.Remainder.Length + appended.Length];
                Buffer.BlockCopy(cursor.Remainder, 0, combined, 0, cursor.Remainder.Length);
                Buffer.BlockCopy(appended, 0, combined, cursor.Remainder.Length, appended.Length);

                var (lines, remainder) = SplitRelevantCompleteLines(combined, minimumTimestamp);
                cursor.Remainder = remainder;

                var batch = TokenEventParser.Parse(lines, cursor.ParserState, fallbackSessionID);
                _malformedRelevantLines += batch.MalformedRelevantLines;

                foreach (var @event in batch.Events)
                {
                    if (@event.Timestamp >= retentionStart)
                    {
                        if (!_seenDeduplicationKeys.ContainsKey(@event.DeduplicationKey))
                        {
                            _seenDeduplicationKeys[@event.DeduplicationKey] = @event.Timestamp;
                            _events.Add(@event);
                        }
                    }
                }
            }

            // Advance to the actual read endpoint, not the discovery-time file.Size
            // snapshot, so concurrent appends are not re-read on the next refresh.
            long endOffset = stream.Position;
            cursor.Offset = endOffset;
            cursor.ContinuityDigest = ComputeBoundaryDigest(stream, endOffset);
            cursor.LastModified = file.ModifiedAt;
            _cursors[file.Path] = cursor;
            return false;
        }
    }

    // Computes the SHA-256 of the last up to ContinuityWindowSize bytes ending at
    // endOffset. In-memory only: never persisted or logged. After this call the
    // stream position is at endOffset, but callers must not reassign cursor.Offset
    // from stream.Position here.
    private static byte[] ComputeBoundaryDigest(Stream stream, long endOffset)
    {
        long anchorLen = Math.Min(ContinuityWindowSize, endOffset);
        if (anchorLen <= 0)
        {
            return Array.Empty<byte>();
        }

        long start = endOffset - anchorLen;
        stream.Seek(start, SeekOrigin.Begin);

        byte[] buffer = new byte[anchorLen];
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            int read = stream.Read(buffer, totalRead, buffer.Length - totalRead);
            if (read <= 0)
            {
                break;
            }
            totalRead += read;
        }

        if (totalRead == 0)
        {
            return Array.Empty<byte>();
        }

        if (totalRead < buffer.Length)
        {
            byte[] trimmed = new byte[totalRead];
            Buffer.BlockCopy(buffer, 0, trimmed, 0, totalRead);
            buffer = trimmed;
        }

        return SHA256.HashData(buffer);
    }

    private static bool VerifyBoundaryDigest(Stream stream, long endOffset, byte[] expected)
    {
        byte[] actual = ComputeBoundaryDigest(stream, endOffset);
        if (actual.Length != expected.Length)
        {
            return false;
        }
        for (int i = 0; i < actual.Length; i++)
        {
            if (actual[i] != expected[i])
            {
                return false;
            }
        }
        return true;
    }

    private (List<byte[]> lines, byte[] remainder) SplitRelevantCompleteLines(byte[] data, string minimumTimestamp)
    {
        int finalNewline = Array.LastIndexOf(data, (byte)0x0A);
        if (finalNewline < 0)
        {
            return (new List<byte[]>(), RetainedIncompleteLine(data));
        }

        int completeEnd = finalNewline + 1;
        var lineStarts = new HashSet<int>();

        var span = data.AsSpan(0, completeEnd);
        foreach (var marker in TokenEventParser.RelevantMarkersRaw)
        {
            int searchStart = 0;
            while (searchStart < span.Length)
            {
                int index = span.Slice(searchStart).IndexOf(marker);
                if (index < 0)
                {
                    break;
                }

                int matchPos = searchStart + index;
                int prevNewline = -1;
                for (int i = matchPos - 1; i >= 0; i--)
                {
                    if (data[i] == 0x0A)
                    {
                        prevNewline = i;
                        break;
                    }
                }

                int lineStart = prevNewline >= 0 ? prevNewline + 1 : 0;
                lineStarts.Add(lineStart);
                searchStart = matchPos + marker.Length;
            }
        }

        var sortedStarts = lineStarts.OrderBy(x => x).ToList();
        var lines = new List<byte[]>();

        foreach (int start in sortedStarts)
        {
            int nextNewline = -1;
            for (int i = start; i < completeEnd; i++)
            {
                if (data[i] == 0x0A)
                {
                    nextNewline = i;
                    break;
                }
            }

            if (nextNewline >= 0 && start < nextNewline)
            {
                byte[] line = new byte[nextNewline - start];
                Buffer.BlockCopy(data, start, line, 0, line.Length);
                if (EventMayBeRecent(line, minimumTimestamp))
                {
                    lines.Add(line);
                }
            }
        }

        byte[] remainder;
        if (completeEnd < data.Length)
        {
            int remainderLen = data.Length - completeEnd;
            byte[] remainderRaw = new byte[remainderLen];
            Buffer.BlockCopy(data, completeEnd, remainderRaw, 0, remainderLen);
            remainder = RetainedIncompleteLine(remainderRaw);
        }
        else
        {
            remainder = Array.Empty<byte>();
        }

        return (lines, remainder);
    }

    private byte[] RetainedIncompleteLine(byte[] data)
    {
        var span = data.AsSpan();
        foreach (var marker in TokenEventParser.RelevantMarkersRaw)
        {
            if (span.IndexOf(marker) >= 0)
            {
                return data;
            }
        }

        if (data.Length > _markerOverlapSize)
        {
            byte[] suffix = new byte[_markerOverlapSize];
            Buffer.BlockCopy(data, data.Length - _markerOverlapSize, suffix, 0, _markerOverlapSize);
            return suffix;
        }
        return data;
    }

    private bool EventMayBeRecent(byte[] line, string minimumTimestamp)
    {
        var span = line.AsSpan();
        if (!span.StartsWith(TimestampPrefix))
        {
            return true;
        }

        int start = TimestampPrefix.Length;
        if (start >= span.Length)
        {
            return true;
        }

        int end = -1;
        for (int i = start; i < span.Length; i++)
        {
            if (span[i] == 0x22)
            {
                end = i;
                break;
            }
        }

        if (end < 0)
        {
            return true;
        }

        string timestamp = System.Text.Encoding.UTF8.GetString(line, start, end - start);
        return string.CompareOrdinal(timestamp, minimumTimestamp) >= 0;
    }
}
