using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace CodexTPSCore;

public record struct WindowMetrics(
    [property: JsonPropertyName("windowSeconds")] int WindowSeconds,
    [property: JsonPropertyName("requestCount")] int RequestCount,
    [property: JsonPropertyName("requestsPerMinute")] double RequestsPerMinute,
    [property: JsonPropertyName("tokensPerSecond")] double TokensPerSecond,
    [property: JsonPropertyName("inputTokensPerSecond")] double InputTokensPerSecond,
    [property: JsonPropertyName("cachedInputTokensPerSecond")] double CachedInputTokensPerSecond,
    [property: JsonPropertyName("outputTokensPerSecond")] double OutputTokensPerSecond,
    [property: JsonPropertyName("reasoningTokensPerSecond")] double ReasoningTokensPerSecond,
    [property: JsonPropertyName("cacheRatio")] double CacheRatio,
    [property: JsonPropertyName("totalTokens")] long TotalTokens
)
{
    public static WindowMetrics Empty(int windowSeconds)
    {
        return new WindowMetrics(
            WindowSeconds: windowSeconds,
            RequestCount: 0,
            RequestsPerMinute: 0,
            TokensPerSecond: 0,
            InputTokensPerSecond: 0,
            CachedInputTokensPerSecond: 0,
            OutputTokensPerSecond: 0,
            ReasoningTokensPerSecond: 0,
            CacheRatio: 0,
            TotalTokens: 0
        );
    }
}

[JsonConverter(typeof(JsonStringEnumConverter<CollectionStatus>))]
public enum CollectionStatus
{
    [JsonPropertyName("ready")]
    Ready,
    [JsonPropertyName("sessionsDirectoryMissing")]
    SessionsDirectoryMissing,
    [JsonPropertyName("readFailed")]
    ReadFailed
}

public record struct UsageSnapshot(
    [property: JsonPropertyName("generatedAt")] DateTimeOffset GeneratedAt,
    [property: JsonPropertyName("oneMinute")] WindowMetrics OneMinute,
    [property: JsonPropertyName("fiveMinutes")] WindowMetrics FiveMinutes,
    [property: JsonPropertyName("thirtyMinutes")] WindowMetrics ThirtyMinutes,
    [property: JsonPropertyName("oneHour")] WindowMetrics OneHour,
    [property: JsonPropertyName("activeSessions")] int ActiveSessions,
    [property: JsonPropertyName("malformedRelevantLines")] int MalformedRelevantLines,
    [property: JsonPropertyName("status")] CollectionStatus Status
)
{
    public static UsageSnapshot Empty(DateTimeOffset date, CollectionStatus status)
    {
        return new UsageSnapshot(
            GeneratedAt: date,
            OneMinute: WindowMetrics.Empty(60),
            FiveMinutes: WindowMetrics.Empty(300),
            ThirtyMinutes: WindowMetrics.Empty(1800),
            OneHour: WindowMetrics.Empty(3600),
            ActiveSessions: 0,
            MalformedRelevantLines: 0,
            Status: status
        );
    }
}

public static class UsageMetricsCalculator
{
    public static UsageSnapshot Snapshot(
        IReadOnlyList<UsageEvent> events,
        DateTimeOffset now,
        int activeSessions,
        int malformedRelevantLines,
        CollectionStatus status = CollectionStatus.Ready)
    {
        return new UsageSnapshot(
            GeneratedAt: now,
            OneMinute: Metrics(events, now, 60),
            FiveMinutes: Metrics(events, now, 300),
            ThirtyMinutes: Metrics(events, now, 1800),
            OneHour: Metrics(events, now, 3600),
            ActiveSessions: activeSessions,
            MalformedRelevantLines: malformedRelevantLines,
            Status: status
        );
    }

    private static WindowMetrics Metrics(IReadOnlyList<UsageEvent> events, DateTimeOffset now, int seconds)
    {
        var start = now.AddSeconds(-seconds);
        var end = now.AddSeconds(5);
        var matching = events.Where(e => e.Timestamp > start && e.Timestamp <= end).ToList();

        long input = matching.Sum(e => e.Usage.InputTokens);
        long cached = matching.Sum(e => e.Usage.CachedInputTokens);
        long output = matching.Sum(e => e.Usage.OutputTokens);
        long reasoning = matching.Sum(e => e.Usage.ReasoningOutputTokens);
        long total = matching.Sum(e => e.Usage.TotalTokens);
        double duration = seconds;

        return new WindowMetrics(
            WindowSeconds: seconds,
            RequestCount: matching.Count,
            RequestsPerMinute: (double)matching.Count * 60.0 / duration,
            TokensPerSecond: (double)total / duration,
            InputTokensPerSecond: (double)input / duration,
            CachedInputTokensPerSecond: (double)cached / duration,
            OutputTokensPerSecond: (double)output / duration,
            ReasoningTokensPerSecond: (double)reasoning / duration,
            CacheRatio: input > 0 ? (double)cached / (double)input : 0.0,
            TotalTokens: total
        );
    }
}
