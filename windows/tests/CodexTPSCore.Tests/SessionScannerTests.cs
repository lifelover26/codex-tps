using System;
using System.IO;
using System.Text;
using Xunit;

namespace CodexTPSCore.Tests;

public class SessionScannerTests
{
    // BOM-free UTF-8, matching what real Codex log files contain.
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    [Fact]
    public void TestScannerReadsOnlyAppendedEventsAfterBootstrap()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "codex-tps-tests-" + Guid.NewGuid().ToString());
        try
        {
            var now = DateTimeOffset.UtcNow;
            var year = now.Year.ToString("D4");
            var month = now.Month.ToString("D2");
            var day = now.Day.ToString("D2");

            var sessionsDir = Path.Combine(tempRoot, "sessions", year, month, day);
            Directory.CreateDirectory(sessionsDir);

            var logFile = Path.Combine(sessionsDir, "rollout-session-a.jsonl");
            string firstTimestamp = now.AddSeconds(-20).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);
            string secondTimestamp = now.AddSeconds(-5).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);

            var initialLines = new[]
            {
                """{"timestamp":"FIRST_TS","type":"session_meta","payload":{"id":"session-a","model_provider":"test-provider"}}""".Replace("FIRST_TS", firstTimestamp),
                """{"timestamp":"FIRST_TS","type":"turn_context","payload":{"model":"gpt-test"}}""".Replace("FIRST_TS", firstTimestamp),
                """{"timestamp":"FIRST_TS","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":100,"output_tokens":20,"total_tokens":120},"last_token_usage":{"input_tokens":100,"output_tokens":20,"total_tokens":120}}}}""".Replace("FIRST_TS", firstTimestamp)
            };
            File.WriteAllText(logFile, string.Join("\n", initialLines) + "\n", Utf8NoBom);

            var scanner = new SessionScanner(tempRoot);
            var firstSnapshot = scanner.Refresh(now);
            Assert.Equal(120, firstSnapshot.OneMinute.TotalTokens);

            var appendLine = """{"timestamp":"SECOND_TS","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":150,"output_tokens":30,"total_tokens":180},"last_token_usage":{"input_tokens":50,"output_tokens":10,"total_tokens":60}}}}""".Replace("SECOND_TS", secondTimestamp) + "\n";
            File.AppendAllText(logFile, appendLine, Utf8NoBom);

            var secondSnapshot = scanner.Refresh(now);
            var unchangedSnapshot = scanner.Refresh(now);

            Assert.Equal(180, secondSnapshot.OneMinute.TotalTokens);
            Assert.Equal(180, unchangedSnapshot.OneMinute.TotalTokens);
            Assert.Equal(2, unchangedSnapshot.OneMinute.RequestCount);
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch { }
        }
    }

    [Fact]
    public void TestScannerDeduplicatesReplayedEventAcrossFiles()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "codex-tps-tests-" + Guid.NewGuid().ToString());
        try
        {
            var now = DateTimeOffset.UtcNow;
            var year = now.Year.ToString("D4");
            var month = now.Month.ToString("D2");
            var day = now.Day.ToString("D2");

            var sessionsDir = Path.Combine(tempRoot, "sessions", year, month, day);
            Directory.CreateDirectory(sessionsDir);

            string timestamp = now.AddSeconds(-5).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);
            var tokenEvent = """{"timestamp":"TS","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":100,"output_tokens":20,"total_tokens":120},"last_token_usage":{"input_tokens":100,"output_tokens":20,"total_tokens":120}}}}""".Replace("TS", timestamp);

            var parentLines = new[]
            {
                """{"timestamp":"TS","type":"session_meta","payload":{"id":"parent","model_provider":"test-provider"}}""".Replace("TS", timestamp),
                """{"timestamp":"TS","type":"turn_context","payload":{"model":"gpt-test"}}""".Replace("TS", timestamp),
                tokenEvent
            };

            var childLines = new[]
            {
                """{"timestamp":"TS","type":"session_meta","payload":{"id":"child","forked_from_id":"parent","thread_source":"subagent","model_provider":"test-provider"}}""".Replace("TS", timestamp),
                """{"timestamp":"TS","type":"turn_context","payload":{"model":"gpt-test"}}""".Replace("TS", timestamp),
                tokenEvent
            };

            File.WriteAllText(Path.Combine(sessionsDir, "rollout-parent.jsonl"), string.Join("\n", parentLines) + "\n", Utf8NoBom);
            File.WriteAllText(Path.Combine(sessionsDir, "rollout-child.jsonl"), string.Join("\n", childLines) + "\n", Utf8NoBom);

            var scanner = new SessionScanner(tempRoot);
            var snapshot = scanner.Refresh(now);

            Assert.Equal(1, snapshot.OneMinute.RequestCount);
            Assert.Equal(120, snapshot.OneMinute.TotalTokens);
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch { }
        }
    }

    [Fact]
    public void TestScannerSkipsForkHistoryWhenReplayTimestampsAreRewritten()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "codex-tps-tests-" + Guid.NewGuid().ToString());
        try
        {
            var now = DateTimeOffset.UtcNow;
            var year = now.Year.ToString("D4");
            var month = now.Month.ToString("D2");
            var day = now.Day.ToString("D2");

            var sessionsDir = Path.Combine(tempRoot, "sessions", year, month, day);
            Directory.CreateDirectory(sessionsDir);

            string parentTimestamp = now.AddSeconds(-10).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);
            string replayTimestamp = now.AddSeconds(-5).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);
            string childID = "019f5e41-117d-7000-8000-000000000001";
            string childTurnID = "019f5e41-117e-7000-8000-000000000001";
            string legacyTurnID = "49b1eb54-d964-4272-8c71-01c9eed13679";

            var parentEvent = """{"timestamp":"PARENT_TS","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":100,"output_tokens":20,"total_tokens":120},"last_token_usage":{"input_tokens":100,"output_tokens":20,"total_tokens":120}}}}""".Replace("PARENT_TS", parentTimestamp) + "\n";
            File.WriteAllText(Path.Combine(sessionsDir, "rollout-parent.jsonl"), parentEvent, Utf8NoBom);

            var childLines = new[]
            {
                """{"timestamp":"REPLAY_TS","type":"session_meta","payload":{"id":"CHILD_ID","forked_from_id":"parent","thread_source":"subagent","model_provider":"test-provider"}}""",
                """{"timestamp":"REPLAY_TS","type":"session_meta","payload":{"id":"parent","thread_source":"user","model_provider":"test-provider"}}""",
                """{"timestamp":"REPLAY_TS","type":"event_msg","payload":{"type":"task_started","turn_id":"LEGACY_TURN_ID"}}""",
                """{"timestamp":"REPLAY_TS","type":"turn_context","payload":{"turn_id":"LEGACY_TURN_ID","model":"gpt-test"}}""",
                """{"timestamp":"REPLAY_TS","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":100,"output_tokens":20,"total_tokens":120},"last_token_usage":{"input_tokens":100,"output_tokens":20,"total_tokens":120}}}}""",
                """{"timestamp":"REPLAY_TS","type":"event_msg","payload":{"type":"task_started","turn_id":"CHILD_TURN_ID"}}""",
                """{"timestamp":"REPLAY_TS","type":"turn_context","payload":{"turn_id":"CHILD_TURN_ID","model":"gpt-test"}}""",
                """{"timestamp":"REPLAY_TS","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":100,"output_tokens":20,"total_tokens":120},"last_token_usage":{"input_tokens":100,"output_tokens":20,"total_tokens":120}}}}""",
                """{"timestamp":"REPLAY_TS","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":120,"output_tokens":22,"total_tokens":142},"last_token_usage":{"input_tokens":20,"output_tokens":2,"total_tokens":22}}}}"""
            };

            for (int i = 0; i < childLines.Length; i++)
            {
                childLines[i] = childLines[i]
                    .Replace("REPLAY_TS", replayTimestamp)
                    .Replace("CHILD_ID", childID)
                    .Replace("CHILD_TURN_ID", childTurnID)
                    .Replace("LEGACY_TURN_ID", legacyTurnID);
            }

            File.WriteAllText(Path.Combine(sessionsDir, "rollout-child.jsonl"), string.Join("\n", childLines) + "\n", Utf8NoBom);

            var scanner = new SessionScanner(tempRoot);
            var snapshot = scanner.Refresh(now);

            Assert.Equal(2, snapshot.OneMinute.RequestCount);
            Assert.Equal(142, snapshot.OneMinute.TotalTokens);
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch { }
        }
    }
}
