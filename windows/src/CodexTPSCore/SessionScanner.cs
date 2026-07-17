using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace CodexTPSCore;

public class SessionScanner
{
    private static readonly byte[] TimestampPrefix = "{\"timestamp\":\""u8.ToArray();

    private class FileCursor
    {
        public long Offset { get; set; } = 0;
        public byte[] Remainder { get; set; } = Array.Empty<byte>();
        public TokenParserState ParserState { get; } = new();
        public DateTimeOffset LastModified { get; set; } = DateTimeOffset.MinValue;
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
                    ReadAppendedContent(file, retentionStart);
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

    private void ReadAppendedContent(SessionFile file, DateTimeOffset retentionStart)
    {
        if (!_cursors.TryGetValue(file.Path, out var cursor))
        {
            cursor = new FileCursor();
        }

        if (file.Size < cursor.Offset)
        {
            cursor = new FileCursor();
        }

        if (file.Size <= cursor.Offset)
        {
            cursor.LastModified = file.ModifiedAt;
            _cursors[file.Path] = cursor;
            return;
        }

        byte[] appendedBuffer = new byte[_readChunkSize];
        string fallbackSessionID = Path.GetFileNameWithoutExtension(file.Path);
        string minimumTimestamp = retentionStart.AddSeconds(-2).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        using (var stream = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
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
        }

        cursor.Offset = file.Size;
        cursor.LastModified = file.ModifiedAt;
        _cursors[file.Path] = cursor;
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
