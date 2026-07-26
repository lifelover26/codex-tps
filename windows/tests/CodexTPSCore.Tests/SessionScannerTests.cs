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

    [Fact]
    public void TestScannerFindsActiveOldDateFile()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "codex-tps-tests-" + Guid.NewGuid().ToString());
        try
        {
            var now = DateTimeOffset.UtcNow;
            var oldDate = now.AddDays(-3);
            var oldYear = oldDate.Year.ToString("D4");
            var oldMonth = oldDate.Month.ToString("D2");
            var oldDay = oldDate.Day.ToString("D2");

            var sessionsDir = Path.Combine(tempRoot, "sessions", oldYear, oldMonth, oldDay);
            Directory.CreateDirectory(sessionsDir);

            var logFile = Path.Combine(sessionsDir, "rollout-session-a.jsonl");
            string timestamp = now.AddSeconds(-20).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);

            var lines = new[]
            {
                """{"timestamp":"TS","type":"session_meta","payload":{"id":"session-a","model_provider":"test-provider"}}""".Replace("TS", timestamp),
                """{"timestamp":"TS","type":"turn_context","payload":{"model":"gpt-test"}}""".Replace("TS", timestamp),
                """{"timestamp":"TS","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":100,"output_tokens":20,"total_tokens":120},"last_token_usage":{"input_tokens":100,"output_tokens":20,"total_tokens":120}}}}""".Replace("TS", timestamp)
            };
            File.WriteAllText(logFile, string.Join("\n", lines) + "\n", Utf8NoBom);

            File.SetLastWriteTimeUtc(logFile, now.UtcDateTime.AddSeconds(-10));

            var scanner = new SessionScanner(tempRoot);
            var snapshot = scanner.Refresh(now);

            Assert.Equal(120, snapshot.OneMinute.TotalTokens);
            Assert.Equal(1, snapshot.OneMinute.RequestCount);
            Assert.Equal(1, snapshot.ActiveSessions);
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch { }
        }
    }

    [Fact]
    public void TestScannerSkipsStaleOldDateFile()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "codex-tps-tests-" + Guid.NewGuid().ToString());
        try
        {
            var now = DateTimeOffset.UtcNow;
            var oldDate = now.AddDays(-3);
            var oldYear = oldDate.Year.ToString("D4");
            var oldMonth = oldDate.Month.ToString("D2");
            var oldDay = oldDate.Day.ToString("D2");

            var sessionsDir = Path.Combine(tempRoot, "sessions", oldYear, oldMonth, oldDay);
            Directory.CreateDirectory(sessionsDir);

            var logFile = Path.Combine(sessionsDir, "rollout-session-a.jsonl");
            string timestamp = now.AddSeconds(-20).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);

            var lines = new[]
            {
                """{"timestamp":"TS","type":"session_meta","payload":{"id":"session-a","model_provider":"test-provider"}}""".Replace("TS", timestamp),
                """{"timestamp":"TS","type":"turn_context","payload":{"model":"gpt-test"}}""".Replace("TS", timestamp),
                """{"timestamp":"TS","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":100,"output_tokens":20,"total_tokens":120},"last_token_usage":{"input_tokens":100,"output_tokens":20,"total_tokens":120}}}}""".Replace("TS", timestamp)
            };
            File.WriteAllText(logFile, string.Join("\n", lines) + "\n", Utf8NoBom);

            File.SetLastWriteTimeUtc(logFile, now.UtcDateTime.AddHours(-2));

            var scanner = new SessionScanner(tempRoot);
            var snapshot = scanner.Refresh(now);

            Assert.Equal(0, snapshot.OneMinute.TotalTokens);
            Assert.Equal(0, snapshot.OneMinute.RequestCount);
            Assert.Equal(0, snapshot.ActiveSessions);
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch { }
        }
    }

    [Fact]
    public void TestScannerHandlesIncrementalOldDateAppend()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "codex-tps-tests-" + Guid.NewGuid().ToString());
        try
        {
            var now = DateTimeOffset.UtcNow;
            var oldDate = now.AddDays(-3);
            var oldYear = oldDate.Year.ToString("D4");
            var oldMonth = oldDate.Month.ToString("D2");
            var oldDay = oldDate.Day.ToString("D2");

            var sessionsDir = Path.Combine(tempRoot, "sessions", oldYear, oldMonth, oldDay);
            Directory.CreateDirectory(sessionsDir);

            var logFile = Path.Combine(sessionsDir, "rollout-session-a.jsonl");
            string firstTimestamp = now.AddSeconds(-20).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);
            string secondTimestamp = now.AddSeconds(-5).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);

            var initialLines = new[]
            {
                """{"timestamp":"TS","type":"session_meta","payload":{"id":"session-a","model_provider":"test-provider"}}""".Replace("TS", firstTimestamp),
                """{"timestamp":"TS","type":"turn_context","payload":{"model":"gpt-test"}}""".Replace("TS", firstTimestamp),
                """{"timestamp":"TS","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":100,"output_tokens":20,"total_tokens":120},"last_token_usage":{"input_tokens":100,"output_tokens":20,"total_tokens":120}}}}""".Replace("TS", firstTimestamp)
            };
            File.WriteAllText(logFile, string.Join("\n", initialLines) + "\n", Utf8NoBom);
            File.SetLastWriteTimeUtc(logFile, now.UtcDateTime.AddSeconds(-15));

            var scanner = new SessionScanner(tempRoot);
            var firstSnapshot = scanner.Refresh(now);
            Assert.Equal(120, firstSnapshot.OneMinute.TotalTokens);
            Assert.Equal(1, firstSnapshot.OneMinute.RequestCount);

            var appendLine = """{"timestamp":"TS","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":150,"output_tokens":30,"total_tokens":180},"last_token_usage":{"input_tokens":50,"output_tokens":10,"total_tokens":60}}}}""".Replace("TS", secondTimestamp) + "\n";
            File.AppendAllText(logFile, appendLine, Utf8NoBom);
            File.SetLastWriteTimeUtc(logFile, now.UtcDateTime.AddSeconds(-3));

            var secondSnapshot = scanner.Refresh(now);
            Assert.Equal(180, secondSnapshot.OneMinute.TotalTokens);
            Assert.Equal(2, secondSnapshot.OneMinute.RequestCount);

            var unchangedSnapshot = scanner.Refresh(now);
            Assert.Equal(180, unchangedSnapshot.OneMinute.TotalTokens);
            Assert.Equal(2, unchangedSnapshot.OneMinute.RequestCount);
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch { }
        }
    }

    [Fact]
    public void TestScannerExcludesStaleFileWithCursor()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "codex-tps-tests-" + Guid.NewGuid().ToString());
        try
        {
            var now = DateTimeOffset.UtcNow;
            var oldDate = now.AddDays(-3);
            var oldYear = oldDate.Year.ToString("D4");
            var oldMonth = oldDate.Month.ToString("D2");
            var oldDay = oldDate.Day.ToString("D2");

            var sessionsDir = Path.Combine(tempRoot, "sessions", oldYear, oldMonth, oldDay);
            Directory.CreateDirectory(sessionsDir);

            var logFile = Path.Combine(sessionsDir, "rollout-session-a.jsonl");
            string firstTimestamp = now.AddSeconds(-20).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);
            string secondTimestamp = now.AddSeconds(-5).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);

            var initialLines = new[]
            {
                """{"timestamp":"TS","type":"session_meta","payload":{"id":"session-a","model_provider":"test-provider"}}""".Replace("TS", firstTimestamp),
                """{"timestamp":"TS","type":"turn_context","payload":{"model":"gpt-test"}}""".Replace("TS", firstTimestamp),
                """{"timestamp":"TS","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":100,"output_tokens":20,"total_tokens":120},"last_token_usage":{"input_tokens":100,"output_tokens":20,"total_tokens":120}}}}""".Replace("TS", firstTimestamp)
            };
            File.WriteAllText(logFile, string.Join("\n", initialLines) + "\n", Utf8NoBom);
            File.SetLastWriteTimeUtc(logFile, now.UtcDateTime.AddSeconds(-10));

            var scanner = new SessionScanner(tempRoot);
            var firstSnapshot = scanner.Refresh(now);
            Assert.Equal(120, firstSnapshot.OneMinute.TotalTokens);
            Assert.Equal(1, firstSnapshot.OneMinute.RequestCount);

            var appendLine = """{"timestamp":"TS","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":150,"output_tokens":30,"total_tokens":180},"last_token_usage":{"input_tokens":50,"output_tokens":10,"total_tokens":60}}}}""".Replace("TS", secondTimestamp) + "\n";
            File.AppendAllText(logFile, appendLine, Utf8NoBom);

            File.SetLastWriteTimeUtc(logFile, now.UtcDateTime.AddHours(-2));

            var staleSnapshot = scanner.Refresh(now);
            Assert.Equal(120, staleSnapshot.OneMinute.TotalTokens);
            Assert.Equal(1, staleSnapshot.OneMinute.RequestCount);

            File.SetLastWriteTimeUtc(logFile, now.UtcDateTime.AddSeconds(-3));

            var resumedSnapshot = scanner.Refresh(now);
            Assert.Equal(180, resumedSnapshot.OneMinute.TotalTokens);
            Assert.Equal(2, resumedSnapshot.OneMinute.RequestCount);

            var finalSnapshot = scanner.Refresh(now);
            Assert.Equal(180, finalSnapshot.OneMinute.TotalTokens);
            Assert.Equal(2, finalSnapshot.OneMinute.RequestCount);
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch { }
        }
    }

    [Fact]
    public void TestScannerIncrementalAppendStaysStableAcrossRefreshes()
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
            string ts = now.AddSeconds(-30).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);

            var initialLines = new[]
            {
                """{"timestamp":"TS","type":"session_meta","payload":{"id":"session-a","model_provider":"test-provider"}}""".Replace("TS", ts),
                """{"timestamp":"TS","type":"turn_context","payload":{"model":"gpt-test"}}""".Replace("TS", ts),
                """{"timestamp":"TS","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":100,"output_tokens":20,"total_tokens":120},"last_token_usage":{"input_tokens":100,"output_tokens":20,"total_tokens":120}}}}""".Replace("TS", ts)
            };
            File.WriteAllText(logFile, string.Join("\n", initialLines) + "\n", Utf8NoBom);

            var scanner = new SessionScanner(tempRoot);
            var firstSnapshot = scanner.Refresh(now);
            Assert.Equal(120, firstSnapshot.OneMinute.TotalTokens);
            Assert.Equal(1, firstSnapshot.OneMinute.RequestCount);

            // Three incremental appends; each adds 25 tokens (input 20, output 5).
            long runningTotal = 120;
            int runningRequests = 1;
            for (int i = 0; i < 3; i++)
            {
                string appendTs = now.AddSeconds(-25 + i * 5).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);
                long nextTotal = runningTotal + 25;
                string appendLine = """{"timestamp":"APPEND_TS","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":NEXT_IN,"output_tokens":NEXT_OUT,"total_tokens":NEXT_TOTAL},"last_token_usage":{"input_tokens":20,"output_tokens":5,"total_tokens":25}}}}"""
                    .Replace("APPEND_TS", appendTs)
                    .Replace("NEXT_IN", (runningTotal + 20).ToString())
                    .Replace("NEXT_OUT", (runningTotal + 5).ToString())
                    .Replace("NEXT_TOTAL", nextTotal.ToString());
                File.AppendAllText(logFile, appendLine + "\n", Utf8NoBom);

                var snapshot = scanner.Refresh(now);
                runningTotal = nextTotal;
                runningRequests += 1;
                Assert.Equal(runningTotal, snapshot.OneMinute.TotalTokens);
                Assert.Equal(runningRequests, snapshot.OneMinute.RequestCount);
            }

            // Repeated refresh with no file changes must be stable and must not
            // trigger a full rescan (the cursor is reused via its digest).
            var stableSnapshot = scanner.Refresh(now);
            Assert.Equal(runningTotal, stableSnapshot.OneMinute.TotalTokens);
            Assert.Equal(runningRequests, stableSnapshot.OneMinute.RequestCount);

            var stableSnapshot2 = scanner.Refresh(now);
            Assert.Equal(runningTotal, stableSnapshot2.OneMinute.TotalTokens);
            Assert.Equal(runningRequests, stableSnapshot2.OneMinute.RequestCount);
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch { }
        }
    }

    [Fact]
    public void TestScannerRebuildsAfterEqualLengthRewrite()
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
            string tsA = now.AddSeconds(-20).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);
            string tsB = now.AddSeconds(-5).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);

            // Content A: 120 tokens, session "session-aaa".
            var linesA = new[]
            {
                """{"timestamp":"TSA","type":"session_meta","payload":{"id":"session-aaa","model_provider":"test-provider"}}""".Replace("TSA", tsA),
                """{"timestamp":"TSA","type":"turn_context","payload":{"model":"gpt-test"}}""".Replace("TSA", tsA),
                """{"timestamp":"TSA","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":100,"output_tokens":20,"total_tokens":120},"last_token_usage":{"input_tokens":100,"output_tokens":20,"total_tokens":120}}}}""".Replace("TSA", tsA)
            };
            string contentA = string.Join("\n", linesA) + "\n";

            // Content B: 230 tokens, session "session-bbb". All digit groups keep
            // the same width (100->200, 20->30, 120->230) so the byte length is
            // identical to A. This is the case the old file.Size guard missed.
            var linesB = new[]
            {
                """{"timestamp":"TSB","type":"session_meta","payload":{"id":"session-bbb","model_provider":"test-provider"}}""".Replace("TSB", tsB),
                """{"timestamp":"TSB","type":"turn_context","payload":{"model":"gpt-test"}}""".Replace("TSB", tsB),
                """{"timestamp":"TSB","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":200,"output_tokens":30,"total_tokens":230},"last_token_usage":{"input_tokens":200,"output_tokens":30,"total_tokens":230}}}}""".Replace("TSB", tsB)
            };
            string contentB = string.Join("\n", linesB) + "\n";

            Assert.Equal(Utf8NoBom.GetByteCount(contentA), Utf8NoBom.GetByteCount(contentB));

            File.WriteAllText(logFile, contentA, Utf8NoBom);
            File.SetLastWriteTimeUtc(logFile, now.UtcDateTime.AddSeconds(-15));

            var scanner = new SessionScanner(tempRoot);
            var firstSnapshot = scanner.Refresh(now);
            Assert.Equal(120, firstSnapshot.OneMinute.TotalTokens);
            Assert.Equal(1, firstSnapshot.OneMinute.RequestCount);

            // Equal-length rewrite at the same path.
            File.WriteAllText(logFile, contentB, Utf8NoBom);
            File.SetLastWriteTimeUtc(logFile, now.UtcDateTime.AddSeconds(-3));

            var rebuiltSnapshot = scanner.Refresh(now);
            // Must report only B's content (230), not A's stale events (120) and
            // not A+B (350).
            Assert.Equal(230, rebuiltSnapshot.OneMinute.TotalTokens);
            Assert.Equal(1, rebuiltSnapshot.OneMinute.RequestCount);

            // A further refresh with no changes stays stable.
            var stableSnapshot = scanner.Refresh(now);
            Assert.Equal(230, stableSnapshot.OneMinute.TotalTokens);
            Assert.Equal(1, stableSnapshot.OneMinute.RequestCount);
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch { }
        }
    }

    [Fact]
    public void TestScannerRebuildsAfterTruncateAndRegrow()
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
            string tsA = now.AddSeconds(-20).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);
            string tsC = now.AddSeconds(-5).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);

            // Content A: 120 tokens.
            var linesA = new[]
            {
                """{"timestamp":"TSA","type":"session_meta","payload":{"id":"session-a","model_provider":"test-provider"}}""".Replace("TSA", tsA),
                """{"timestamp":"TSA","type":"turn_context","payload":{"model":"gpt-test"}}""".Replace("TSA", tsA),
                """{"timestamp":"TSA","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":100,"output_tokens":20,"total_tokens":120},"last_token_usage":{"input_tokens":100,"output_tokens":20,"total_tokens":120}}}}""".Replace("TSA", tsA)
            };
            string contentA = string.Join("\n", linesA) + "\n";
            File.WriteAllText(logFile, contentA, Utf8NoBom);
            File.SetLastWriteTimeUtc(logFile, now.UtcDateTime.AddSeconds(-15));

            var scanner = new SessionScanner(tempRoot);
            var firstSnapshot = scanner.Refresh(now);
            Assert.Equal(120, firstSnapshot.OneMinute.TotalTokens);
            Assert.Equal(1, firstSnapshot.OneMinute.RequestCount);

            long oldOffset = Utf8NoBom.GetByteCount(contentA);

            // Truncate to a short header, then regrow past the old Offset with
            // different content (175 tokens) plus a non-marker padding line.
            File.WriteAllText(logFile, """{"timestamp":"TSC","type":"session_meta","payload":{"id":"session-c","model_provider":"test-provider"}}""".Replace("TSC", tsC) + "\n", Utf8NoBom);
            File.AppendAllText(logFile, """{"timestamp":"TSC","type":"turn_context","payload":{"model":"gpt-test"}}""".Replace("TSC", tsC) + "\n", Utf8NoBom);
            File.AppendAllText(logFile, """{"timestamp":"TSC","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":150,"output_tokens":25,"total_tokens":175},"last_token_usage":{"input_tokens":150,"output_tokens":25,"total_tokens":175}}}}""".Replace("TSC", tsC) + "\n", Utf8NoBom);
            // Padding line: no relevant marker, so the parser ignores it. Its only
            // purpose is to push the file length beyond the old Offset.
            File.AppendAllText(logFile, """{"timestamp":"TSC","type":"user_message","payload":{"content":"padding-to-regrow-past-old-offset"}}""".Replace("TSC", tsC) + "\n", Utf8NoBom);

            long newLength = new FileInfo(logFile).Length;
            Assert.True(newLength >= oldOffset, "regrown file must reach or exceed the old Offset");

            File.SetLastWriteTimeUtc(logFile, now.UtcDateTime.AddSeconds(-3));

            var rebuiltSnapshot = scanner.Refresh(now);
            // Continuity must break and rebuild: only C's 175 tokens are counted,
            // not A's stale 120 and not A+C.
            Assert.Equal(175, rebuiltSnapshot.OneMinute.TotalTokens);
            Assert.Equal(1, rebuiltSnapshot.OneMinute.RequestCount);

            var stableSnapshot = scanner.Refresh(now);
            Assert.Equal(175, stableSnapshot.OneMinute.TotalTokens);
            Assert.Equal(1, stableSnapshot.OneMinute.RequestCount);
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch { }
        }
    }

    [Fact]
    public void TestScannerRebuildsMultipleFilesWithoutDoubleCounting()
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

            var file1 = Path.Combine(sessionsDir, "rollout-file-1.jsonl");
            var file2 = Path.Combine(sessionsDir, "rollout-file-2.jsonl");
            string ts1 = now.AddSeconds(-20).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);
            string ts2 = now.AddSeconds(-10).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);
            string ts1b = now.AddSeconds(-5).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);

            // File 1: 120 tokens, session "sess-aaaa".
            var lines1 = new[]
            {
                """{"timestamp":"TS1","type":"session_meta","payload":{"id":"sess-aaaa","model_provider":"test-provider"}}""".Replace("TS1", ts1),
                """{"timestamp":"TS1","type":"turn_context","payload":{"model":"gpt-test"}}""".Replace("TS1", ts1),
                """{"timestamp":"TS1","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":100,"output_tokens":20,"total_tokens":120},"last_token_usage":{"input_tokens":100,"output_tokens":20,"total_tokens":120}}}}""".Replace("TS1", ts1)
            };
            string content1 = string.Join("\n", lines1) + "\n";

            // File 2: 60 tokens, session "sess-bbbb".
            var lines2 = new[]
            {
                """{"timestamp":"TS2","type":"session_meta","payload":{"id":"sess-bbbb","model_provider":"test-provider"}}""".Replace("TS2", ts2),
                """{"timestamp":"TS2","type":"turn_context","payload":{"model":"gpt-test"}}""".Replace("TS2", ts2),
                """{"timestamp":"TS2","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":50,"output_tokens":10,"total_tokens":60},"last_token_usage":{"input_tokens":50,"output_tokens":10,"total_tokens":60}}}}""".Replace("TS2", ts2)
            };
            string content2 = string.Join("\n", lines2) + "\n";

            File.WriteAllText(file1, content1, Utf8NoBom);
            File.WriteAllText(file2, content2, Utf8NoBom);
            File.SetLastWriteTimeUtc(file1, now.UtcDateTime.AddSeconds(-15));
            File.SetLastWriteTimeUtc(file2, now.UtcDateTime.AddSeconds(-8));

            var scanner = new SessionScanner(tempRoot);
            var firstSnapshot = scanner.Refresh(now);
            Assert.Equal(180, firstSnapshot.OneMinute.TotalTokens);
            Assert.Equal(2, firstSnapshot.OneMinute.RequestCount);

            // Equal-length rewrite of file 1 -> 230 tokens, session "sess-bbbb"
            // (digit groups preserve width: 100->200, 20->30, 120->230).
            var lines1b = new[]
            {
                """{"timestamp":"TS1B","type":"session_meta","payload":{"id":"sess-bbbb","model_provider":"test-provider"}}""".Replace("TS1B", ts1b),
                """{"timestamp":"TS1B","type":"turn_context","payload":{"model":"gpt-test"}}""".Replace("TS1B", ts1b),
                """{"timestamp":"TS1B","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":200,"output_tokens":30,"total_tokens":230},"last_token_usage":{"input_tokens":200,"output_tokens":30,"total_tokens":230}}}}""".Replace("TS1B", ts1b)
            };
            string content1b = string.Join("\n", lines1b) + "\n";
            Assert.Equal(Utf8NoBom.GetByteCount(content1), Utf8NoBom.GetByteCount(content1b));

            File.WriteAllText(file1, content1b, Utf8NoBom);
            File.SetLastWriteTimeUtc(file1, now.UtcDateTime.AddSeconds(-3));

            var rebuiltSnapshot = scanner.Refresh(now);
            // After rebuild: file1 contributes 230, file2 contributes 60, total 290.
            // Not 410 (180 + 230) which would indicate stale events were retained.
            Assert.Equal(290, rebuiltSnapshot.OneMinute.TotalTokens);
            Assert.Equal(2, rebuiltSnapshot.OneMinute.RequestCount);

            var stableSnapshot = scanner.Refresh(now);
            Assert.Equal(290, stableSnapshot.OneMinute.TotalTokens);
            Assert.Equal(2, stableSnapshot.OneMinute.RequestCount);
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch { }
        }
    }
}
