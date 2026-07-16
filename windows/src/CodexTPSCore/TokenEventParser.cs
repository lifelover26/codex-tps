using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace CodexTPSCore;

public class TokenParserState
{
    public string? CurrentModel { get; set; }
    public string? Provider { get; set; }
    public string? SessionIDFromMeta { get; set; }
    public string? ForkParentID { get; set; }
    public string? ChildSessionID { get; set; }
    public string? ChildProvider { get; set; }
    public string? ReplaySessionID { get; set; }
    public bool WaitingForChildTurn { get; set; }
    public bool ChildIsUserFork { get; set; }
    public HashSet<string> ChildTaskStartedTurnIDs { get; } = new(StringComparer.Ordinal);
    internal UsageTotals? PreviousTotals { get; set; }
    internal UsageTotals? InheritedBaseline { get; set; }
    public long? InheritedReportedTotal { get; set; }

    public TokenParserState() { }
}

public record TokenParseBatch(
    IReadOnlyList<UsageEvent> Events,
    int MalformedRelevantLines
);

public static class TokenEventParser
{
    private static readonly byte[][] RelevantMarkers = new byte[][]
    {
        "\"type\":\"token_count\",\"info\":"u8.ToArray(),
        "\"type\":\"session_meta\",\"payload\":"u8.ToArray(),
        "\"type\":\"turn_context\",\"payload\":"u8.ToArray(),
        "\"type\":\"task_started\""u8.ToArray()
    };

    public static readonly byte[][] RelevantMarkersRaw = RelevantMarkers;

    public static TokenParseBatch Parse(
        IEnumerable<byte[]> lines,
        TokenParserState state,
        string fallbackSessionID)
    {
        var events = new List<UsageEvent>();
        int malformed = 0;

        foreach (var line in lines)
        {
            if (!ShouldInspect(line))
            {
                continue;
            }

            CodexLogEntry? entry;
            try
            {
                entry = JsonSerializer.Deserialize<CodexLogEntry>(line);
                if (entry == null)
                {
                    malformed++;
                    continue;
                }
            }
            catch
            {
                malformed++;
                continue;
            }

            events.AddRange(Process(entry, state, fallbackSessionID));
        }

        return new TokenParseBatch(events, malformed);
    }

    public static bool ShouldInspect(byte[] line)
    {
        var span = line.AsSpan();
        foreach (var marker in RelevantMarkers)
        {
            if (span.IndexOf(marker) >= 0)
            {
                return true;
            }
        }
        return false;
    }

    private static IReadOnlyList<UsageEvent> Process(
        CodexLogEntry entry,
        TokenParserState state,
        string fallbackSessionID)
    {
        var payload = entry.Payload;
        if (payload == null) return Array.Empty<UsageEvent>();

        if (state.WaitingForChildTurn)
        {
            if (entry.Type == "turn_context" &&
                ChildTurnStartsOwnSession(state, payload.TurnID))
            {
                state.WaitingForChildTurn = false;
                state.ReplaySessionID = null;
                state.ChildTaskStartedTurnIDs.Clear();
                state.ChildIsUserFork = false;
                state.SessionIDFromMeta = state.ChildSessionID;
                state.Provider = state.ChildProvider ?? state.Provider;
                state.CurrentModel = payload.ResolvedModel ?? state.CurrentModel;
            }
            else
            {
                RememberReplayState(entry, payload, state);
                return Array.Empty<UsageEvent>();
            }
        }

        if (entry.Type == "session_meta")
        {
            ProcessSessionMeta(payload, state);
            return Array.Empty<UsageEvent>();
        }

        if (entry.Type == "turn_context")
        {
            state.CurrentModel = payload.ResolvedModel ?? state.CurrentModel;
            return Array.Empty<UsageEvent>();
        }

        if (entry.Type == "event_msg" && payload.Type == "token_count" && payload.Info != null)
        {
            state.CurrentModel = payload.ResolvedModel ?? state.CurrentModel;
            return ProcessTokenCount(entry, payload.Info, state, fallbackSessionID);
        }

        return Array.Empty<UsageEvent>();
    }

    private static void ProcessSessionMeta(CodexPayload payload, TokenParserState state)
    {
        var parentID = payload.ResolvedForkParentID;
        if (parentID != null)
        {
            bool repeatedChild =
                !state.WaitingForChildTurn
                && state.ChildSessionID != null
                && state.ChildSessionID == payload.Id;

            state.ForkParentID = parentID;
            state.ChildSessionID = payload.Id;
            state.ChildProvider = payload.ModelProvider.NonEmpty() ?? state.ChildProvider;
            state.SessionIDFromMeta = payload.Id ?? state.SessionIDFromMeta;
            state.Provider = payload.ModelProvider.NonEmpty() ?? state.Provider;

            if (!repeatedChild)
            {
                state.WaitingForChildTurn = true;
                state.ReplaySessionID = null;
                state.InheritedBaseline = null;
                state.InheritedReportedTotal = null;
                state.ChildTaskStartedTurnIDs.Clear();
                state.ChildIsUserFork = payload.ThreadSource == "user";
            }
            return;
        }

        state.SessionIDFromMeta = payload.Id ?? state.SessionIDFromMeta;
        state.Provider = payload.ModelProvider.NonEmpty() ?? state.Provider;
        state.CurrentModel = payload.ResolvedModel ?? state.CurrentModel;
    }

    private static void RememberReplayState(
        CodexLogEntry entry,
        CodexPayload payload,
        TokenParserState state)
    {
        if (entry.Type == "event_msg" && payload.Type == "task_started")
        {
            var turnID = payload.TurnID.NonEmpty();
            if (turnID != null)
            {
                state.ChildTaskStartedTurnIDs.Add(turnID);
            }
        }

        if (entry.Type == "session_meta")
        {
            var id = payload.Id.NonEmpty();
            if (id != null && id != state.ChildSessionID)
            {
                state.ReplaySessionID = id;
            }
        }

        if (entry.Type == "event_msg" && payload.Type == "token_count" && payload.Info?.TotalTokenUsage != null)
        {
            var totalUsage = payload.Info.TotalTokenUsage.Value;
            var totals = new UsageTotals(totalUsage);
            state.PreviousTotals = totals;
            state.InheritedBaseline = totals;
            state.InheritedReportedTotal = totalUsage.TotalTokens;
        }
    }

    private static IReadOnlyList<UsageEvent> ProcessTokenCount(
        CodexLogEntry entry,
        CodexUsageInfo info,
        TokenParserState state,
        string fallbackSessionID)
    {
        UsageTotals? total = info.TotalTokenUsage != null ? new UsageTotals(info.TotalTokenUsage.Value) : null;
        UsageTotals? last = info.LastTokenUsage != null ? new UsageTotals(info.LastTokenUsage.Value) : null;

        if (ShouldSkipInherited(total, state))
        {
            return Array.Empty<UsageEvent>();
        }
        state.InheritedBaseline = null;
        state.InheritedReportedTotal = null;

        TokenUsage usage;
        UsageTotals? nextTotals;

        if (total is UsageTotals current && last is UsageTotals increment && state.PreviousTotals is UsageTotals previous)
        {
            if (current.Equals(previous))
            {
                return Array.Empty<UsageEvent>();
            }
            if (current.Delta(previous) == null &&
                current.LooksLikeStaleRegression(previous, increment))
            {
                return Array.Empty<UsageEvent>();
            }
            usage = increment.AsUsage();
            nextTotals = current;
        }
        else if (total is UsageTotals current2 && last is UsageTotals increment2 && state.PreviousTotals == null)
        {
            usage = increment2.AsUsage();
            nextTotals = current2;
        }
        else if (total is UsageTotals current3 && last == null && state.PreviousTotals is UsageTotals previous3)
        {
            var delta = current3.Delta(previous3);
            if (delta == null)
            {
                state.PreviousTotals = current3;
                return Array.Empty<UsageEvent>();
            }
            usage = delta.Value.AsUsage();
            nextTotals = current3;
        }
        else if (total is UsageTotals current4 && last == null && state.PreviousTotals == null)
        {
            usage = current4.AsUsage();
            nextTotals = current4;
        }
        else if (total == null && last is UsageTotals increment5)
        {
            usage = increment5.AsUsage();
            nextTotals = state.PreviousTotals;
        }
        else
        {
            return Array.Empty<UsageEvent>();
        }

        if (usage.TotalTokens <= 0)
        {
            return Array.Empty<UsageEvent>();
        }

        state.PreviousTotals = nextTotals;

        DateTimeOffset timestamp = ParseTimestamp(entry.Timestamp) ?? DateTimeOffset.UtcNow;
        string sessionID = state.SessionIDFromMeta ?? fallbackSessionID;
        string scopeID = state.ForkParentID ?? sessionID;
        string provider = state.Provider ?? "unknown";
        string model = info.Model.NonEmpty() ?? state.CurrentModel ?? "unknown";
        string key = DeduplicationKey(
            timestamp: timestamp,
            usage: usage,
            total: total,
            scopeID: scopeID,
            provider: provider,
            model: model
        );

        return new[]
        {
            new UsageEvent(
                Timestamp: timestamp,
                Usage: usage,
                SessionID: sessionID,
                DeduplicationKey: key
            )
        };
    }

    private static bool ShouldSkipInherited(UsageTotals? total, TokenParserState state)
    {
        if (total is UsageTotals t)
        {
            if (state.InheritedReportedTotal.HasValue && t.ReportedTotal <= state.InheritedReportedTotal.Value)
            {
                return true;
            }

            if (state.InheritedBaseline.HasValue && t.IsWithin(state.InheritedBaseline.Value))
            {
                return true;
            }
        }
        return false;
    }

    private static bool ChildTurnStartsOwnSession(TokenParserState state, string? turnID)
    {
        if (state.ReplaySessionID == null) return true;
        if (state.ChildSessionID == null ||
            turnID == null ||
            UuidV7MillisecondPrefix(state.ChildSessionID) is not string childPrefix ||
            UuidV7MillisecondPrefix(turnID) is not string turnPrefix)
        {
            return false;
        }

        int comparison = string.CompareOrdinal(turnPrefix, childPrefix);
        if (comparison > 0) return true;
        if (comparison < 0) return false;
        return state.ChildIsUserFork || state.ChildTaskStartedTurnIDs.Contains(turnID);
    }

    private static string? UuidV7MillisecondPrefix(string id)
    {
        var parts = id.Split('-');
        if (parts.Length != 5 ||
            parts[0].Length != 8 ||
            parts[1].Length != 4 ||
            parts[2].Length != 4 ||
            parts[2][0] != '7')
        {
            return null;
        }

        var prefix = (parts[0] + parts[1]).ToLowerInvariant();
        foreach (var c in prefix)
        {
            if (!Uri.IsHexDigit(c))
            {
                return null;
            }
        }
        return prefix;
    }

    private static DateTimeOffset? ParseTimestamp(string? value)
    {
        if (value == null) return null;
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var result))
        {
            return result;
        }
        return null;
    }

    private static string FormatTimestamp(DateTimeOffset timestamp)
    {
        return timestamp.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
    }

    private static string DeduplicationKey(
        DateTimeOffset timestamp,
        TokenUsage usage,
        UsageTotals? total,
        string scopeID,
        string provider,
        string model)
    {
        var tsStr = FormatTimestamp(timestamp);
        if (total is UsageTotals t)
        {
            return string.Join(":", new[]
            {
                "codex", "total", scopeID, provider, model, tsStr,
                t.Input.ToString(), t.Output.ToString(), t.Cached.ToString(),
                t.Reasoning.ToString(), t.ReportedTotal.ToString()
            });
        }

        return string.Join(":", new[]
        {
            "codex", "event", scopeID, provider, model, tsStr,
            usage.InputTokens.ToString(), usage.CachedInputTokens.ToString(),
            usage.OutputTokens.ToString(), usage.ReasoningOutputTokens.ToString()
        });
    }
}
