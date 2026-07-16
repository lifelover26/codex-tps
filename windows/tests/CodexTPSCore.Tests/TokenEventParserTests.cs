using System;
using System.Linq;
using System.Text;
using Xunit;

namespace CodexTPSCore.Tests;

public class TokenEventParserTests
{
    [Fact]
    public void TestUsesLastUsageWithoutDoubleCountingCachedOrReasoningSubsets()
    {
        var state = new TokenParserState();
        var lines = FixtureLines(
            """{"timestamp":"2026-07-14T00:00:00Z","type":"session_meta","payload":{"id":"session-a","model_provider":"test-provider"}}""",
            """{"timestamp":"2026-07-14T00:00:01Z","type":"turn_context","payload":{"model":"gpt-test"}}""",
            """{"timestamp":"2026-07-14T00:00:02Z","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":100,"cached_input_tokens":80,"output_tokens":20,"reasoning_output_tokens":10,"total_tokens":120},"last_token_usage":{"input_tokens":100,"cached_input_tokens":80,"output_tokens":20,"reasoning_output_tokens":10,"total_tokens":120}}}}"""
        );

        var batch = TokenEventParser.Parse(lines, state, "fallback");

        Assert.Single(batch.Events);
        Assert.Equal(120, batch.Events[0].Usage.TotalTokens);
        Assert.Equal(80, batch.Events[0].Usage.CachedInputTokens);
        Assert.Equal(10, batch.Events[0].Usage.ReasoningOutputTokens);
    }

    [Fact]
    public void TestRepeatedCumulativeSnapshotIsSuppressed()
    {
        var state = new TokenParserState();
        var tokenLine = """{"timestamp":"2026-07-14T00:00:02Z","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":100,"output_tokens":20,"total_tokens":120},"last_token_usage":{"input_tokens":100,"output_tokens":20,"total_tokens":120}}}}""";
        var lines = FixtureLines(
            """{"timestamp":"2026-07-14T00:00:00Z","type":"session_meta","payload":{"id":"session-a","model_provider":"test-provider"}}""",
            tokenLine,
            tokenLine
        );

        var batch = TokenEventParser.Parse(lines, state, "fallback");

        Assert.Single(batch.Events);
    }

    [Fact]
    public void TestForkedChildSkipsInheritedHistoryUntilOwnTurn()
    {
        var state = new TokenParserState();
        var childID = "019f5e41-117d-7000-8000-000000000001";
        var childTurnID = "019f5e41-117e-7000-8000-000000000001";
        var lines = FixtureLines(
            """{"timestamp":"2026-07-14T00:00:00Z","type":"session_meta","payload":{"id":"CHILD_ID","forked_from_id":"parent","thread_source":"subagent","model_provider":"test-provider"}}""".Replace("CHILD_ID", childID),
            """{"timestamp":"2026-07-14T00:00:00Z","type":"session_meta","payload":{"id":"parent","model_provider":"test-provider"}}""",
            """{"timestamp":"2026-07-14T00:00:01Z","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":100,"output_tokens":20,"total_tokens":120},"last_token_usage":{"input_tokens":100,"output_tokens":20,"total_tokens":120}}}}""",
            """{"timestamp":"2026-07-14T00:00:02Z","type":"event_msg","payload":{"type":"task_started","turn_id":"CHILD_TURN_ID"}}""".Replace("CHILD_TURN_ID", childTurnID),
            """{"timestamp":"2026-07-14T00:00:03Z","type":"turn_context","payload":{"turn_id":"CHILD_TURN_ID","model":"gpt-test"}}""".Replace("CHILD_TURN_ID", childTurnID),
            """{"timestamp":"2026-07-14T00:00:04Z","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":100,"output_tokens":20,"total_tokens":120},"last_token_usage":{"input_tokens":100,"output_tokens":20,"total_tokens":120}}}}""",
            """{"timestamp":"2026-07-14T00:00:05Z","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":120,"output_tokens":22,"total_tokens":142},"last_token_usage":{"input_tokens":20,"output_tokens":2,"total_tokens":22}}}}"""
        );

        var batch = TokenEventParser.Parse(lines, state, childID);

        Assert.Single(batch.Events);
        Assert.Equal(22, batch.Events[0].Usage.TotalTokens);
        Assert.Equal(childID, batch.Events[0].SessionID);
    }

    [Fact]
    public void TestForkedChildKeepsSkippingReplayWithLegacyTurnIDs()
    {
        var state = new TokenParserState();
        var childID = "019f602b-1e2f-7d60-ac59-83fa4dd52c92";
        var childTurnID = "019f602b-4e8a-7360-ad43-2e65035ba716";
        var legacyTurnID = "49b1eb54-d964-4272-8c71-01c9eed13679";
        var lines = FixtureLines(
            """{"timestamp":"2026-07-14T10:27:58Z","type":"session_meta","payload":{"id":"CHILD_ID","forked_from_id":"parent","thread_source":"subagent","model_provider":"test-provider"}}""".Replace("CHILD_ID", childID),
            """{"timestamp":"2026-07-14T10:27:58Z","type":"session_meta","payload":{"id":"parent","model_provider":"test-provider"}}""",
            """{"timestamp":"2026-07-14T10:27:58Z","type":"event_msg","payload":{"type":"task_started","turn_id":"LEGACY_TURN_ID"}}""".Replace("LEGACY_TURN_ID", legacyTurnID),
            """{"timestamp":"2026-07-14T10:27:58Z","type":"turn_context","payload":{"turn_id":"LEGACY_TURN_ID","model":"gpt-test"}}""".Replace("LEGACY_TURN_ID", legacyTurnID),
            """{"timestamp":"2026-07-14T10:27:58Z","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":100,"output_tokens":20,"total_tokens":120},"last_token_usage":{"input_tokens":100,"output_tokens":20,"total_tokens":120}}}}""",
            """{"timestamp":"2026-07-14T10:27:59Z","type":"event_msg","payload":{"type":"task_started","turn_id":"CHILD_TURN_ID"}}""".Replace("CHILD_TURN_ID", childTurnID),
            """{"timestamp":"2026-07-14T10:28:00Z","type":"turn_context","payload":{"turn_id":"CHILD_TURN_ID","model":"gpt-test"}}""".Replace("CHILD_TURN_ID", childTurnID),
            """{"timestamp":"2026-07-14T10:28:01Z","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":120,"output_tokens":22,"total_tokens":142},"last_token_usage":{"input_tokens":20,"output_tokens":2,"total_tokens":22}}}}"""
        );

        var batch = TokenEventParser.Parse(lines, state, childID);

        Assert.Single(batch.Events);
        Assert.Equal(22, batch.Events[0].Usage.TotalTokens);
        Assert.Equal(childID, batch.Events[0].SessionID);
    }

    [Fact]
    public void TestCrossFileReplayUsesStableEventIdentity()
    {
        var parentState = new TokenParserState();
        var childState = new TokenParserState();
        var token = """{"timestamp":"2026-07-14T00:00:02Z","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":100,"output_tokens":20,"total_tokens":120},"last_token_usage":{"input_tokens":100,"output_tokens":20,"total_tokens":120}}}}""";

        var parentBatch = TokenEventParser.Parse(
            FixtureLines(
                """{"timestamp":"2026-07-14T00:00:00Z","type":"session_meta","payload":{"id":"parent","model_provider":"test-provider"}}""",
                """{"timestamp":"2026-07-14T00:00:01Z","type":"turn_context","payload":{"model":"gpt-test"}}""",
                token
            ), parentState, "parent");

        var childBatch = TokenEventParser.Parse(
            FixtureLines(
                """{"timestamp":"2026-07-14T00:00:00Z","type":"session_meta","payload":{"id":"child","forked_from_id":"parent","model_provider":"test-provider"}}""",
                """{"timestamp":"2026-07-14T00:00:01Z","type":"turn_context","payload":{"model":"gpt-test"}}""",
                token
            ), childState, "child");

        Assert.Single(parentBatch.Events);
        Assert.Single(childBatch.Events);
        Assert.Equal(parentBatch.Events[0].DeduplicationKey, childBatch.Events[0].DeduplicationKey);
    }

    private byte[][] FixtureLines(params string[] lines)
    {
        return lines.Select(Encoding.UTF8.GetBytes).ToArray();
    }
}
