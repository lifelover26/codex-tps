using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexTPSCore;

public record struct TokenUsage
{
    [JsonPropertyName("input_tokens")]
    public long InputTokens { get; init; }

    [JsonPropertyName("cached_input_tokens")]
    public long CachedInputTokens { get; init; }

    [JsonPropertyName("output_tokens")]
    public long OutputTokens { get; init; }

    [JsonPropertyName("reasoning_output_tokens")]
    public long ReasoningOutputTokens { get; init; }

    [JsonPropertyName("total_tokens")]
    public long TotalTokens { get; init; }

    public TokenUsage(
        long inputTokens,
        long cachedInputTokens = 0,
        long outputTokens = 0,
        long reasoningOutputTokens = 0,
        long? totalTokens = null)
    {
        InputTokens = Math.Max(inputTokens, 0);
        CachedInputTokens = Math.Max(Math.Min(cachedInputTokens, inputTokens), 0);
        OutputTokens = Math.Max(outputTokens, 0);
        ReasoningOutputTokens = Math.Max(Math.Min(reasoningOutputTokens, outputTokens), 0);
        TotalTokens = Math.Max(totalTokens ?? (inputTokens + outputTokens), 0);
    }
}

internal record struct UsageTotals
{
    public long Input { get; }
    public long Output { get; }
    public long Cached { get; }
    public long Reasoning { get; }
    public long ReportedTotal { get; }

    public UsageTotals(TokenUsage usage)
    {
        Input = usage.InputTokens;
        Output = usage.OutputTokens;
        Cached = usage.CachedInputTokens;
        Reasoning = usage.ReasoningOutputTokens;
        ReportedTotal = usage.TotalTokens;
    }

    public UsageTotals(long input, long output, long cached, long reasoning, long reportedTotal)
    {
        Input = Math.Max(input, 0);
        Output = Math.Max(output, 0);
        Cached = Math.Max(cached, 0);
        Reasoning = Math.Max(reasoning, 0);
        ReportedTotal = Math.Max(reportedTotal, 0);
    }

    public long ComparisonTotal => Input + Output;

    public UsageTotals? Delta(UsageTotals previous)
    {
        if (Input < previous.Input ||
            Output < previous.Output ||
            Cached < previous.Cached ||
            Reasoning < previous.Reasoning)
        {
            return null;
        }

        return new UsageTotals(
            input: Input - previous.Input,
            output: Output - previous.Output,
            cached: Cached - previous.Cached,
            reasoning: Reasoning - previous.Reasoning,
            reportedTotal: Math.Max(ReportedTotal - previous.ReportedTotal, 0)
        );
    }

    public bool IsWithin(UsageTotals baseline)
    {
        return Input <= baseline.Input
            && Output <= baseline.Output
            && Cached <= baseline.Cached
            && Reasoning <= baseline.Reasoning;
    }

    public bool LooksLikeStaleRegression(UsageTotals previous, UsageTotals last)
    {
        long old = previous.ComparisonTotal;
        long current = ComparisonTotal;
        long increment = last.ComparisonTotal;
        if (old <= 0 || current <= 0 || increment <= 0) return false;
        return current * 100 >= old * 98 || current + increment * 2 >= old;
    }

    public TokenUsage AsUsage()
    {
        return new TokenUsage(
            inputTokens: Input,
            cachedInputTokens: Cached,
            outputTokens: Output,
            reasoningOutputTokens: Reasoning,
            totalTokens: ReportedTotal > 0 ? ReportedTotal : Input + Output
        );
    }
}

public record struct UsageEvent(
    DateTimeOffset Timestamp,
    TokenUsage Usage,
    string SessionID,
    string DeduplicationKey
);

internal record CodexLogEntry(
    [property: JsonPropertyName("timestamp")] string? Timestamp,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("payload")] CodexPayload? Payload
);

internal record CodexPayload(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("forked_from_id")] string? ForkedFromID,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("model")] string? Model,
    [property: JsonPropertyName("model_name")] string? ModelName,
    [property: JsonPropertyName("model_info")] CodexModelInfo? ModelInfo,
    [property: JsonPropertyName("info")] CodexUsageInfo? Info,
    [property: JsonPropertyName("turn_id")] string? TurnID,
    [property: JsonPropertyName("thread_source")] string? ThreadSource,
    [property: JsonPropertyName("model_provider")] string? ModelProvider,
    [property: JsonPropertyName("source")] CodexSource? Source
)
{
    public string? ResolvedModel =>
        ModelInfo?.Slug.NonEmpty() ??
        Model.NonEmpty() ??
        ModelName.NonEmpty() ??
        Info?.Model.NonEmpty();

    public string? ResolvedForkParentID =>
        ForkedFromID.NonEmpty() ??
        Source?.ParentThreadID.NonEmpty();
}

internal record CodexModelInfo(
    [property: JsonPropertyName("slug")] string? Slug
);

internal record CodexUsageInfo(
    [property: JsonPropertyName("model")] string? Model,
    [property: JsonPropertyName("last_token_usage")] TokenUsage? LastTokenUsage,
    [property: JsonPropertyName("total_token_usage")] TokenUsage? TotalTokenUsage
);

[JsonConverter(typeof(CodexSourceJsonConverter))]
internal record CodexSource(string? ParentThreadID);

internal class CodexSourceJsonConverter : JsonConverter<CodexSource>
{
    public override CodexSource? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            string? value = reader.GetString();
            return new CodexSource(null);
        }

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            using (var doc = JsonDocument.ParseValue(ref reader))
            {
                var root = doc.RootElement;
                if (root.TryGetProperty("subagent", out var subagent) && subagent.ValueKind == JsonValueKind.Object)
                {
                    if (subagent.TryGetProperty("thread_spawn", out var threadSpawn) && threadSpawn.ValueKind == JsonValueKind.Object)
                    {
                        if (threadSpawn.TryGetProperty("parent_thread_id", out var parentThreadId) && parentThreadId.ValueKind == JsonValueKind.String)
                        {
                            return new CodexSource(parentThreadId.GetString());
                        }
                    }
                }
            }
            return new CodexSource(null);
        }

        throw new JsonException($"Unexpected token type {reader.TokenType} for CodexSource");
    }

    public override void Write(Utf8JsonWriter writer, CodexSource value, JsonSerializerOptions options)
    {
        if (value.ParentThreadID == null)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteStartObject();
            writer.WriteStartObject("subagent");
            writer.WriteStartObject("thread_spawn");
            writer.WriteString("parent_thread_id", value.ParentThreadID);
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
    }
}

public static class StringExtensions
{
    public static string? NonEmpty(this string? value)
    {
        if (value == null) return null;
        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
