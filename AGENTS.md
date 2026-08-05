# Codex TPS Repository Guide

This repository owns a local-only macOS menu bar monitor for Codex token
throughput. It reads Codex session JSONL files and never uploads conversation
content.

## Runtime Contract

- Live input is `$CODEX_HOME/sessions` when `CODEX_HOME` is set, otherwise
  `~/.codex/sessions`.
- Count only `event_msg` entries whose payload type is `token_count`.
- Treat `last_token_usage` as the request increment. Use
  `total_token_usage` only for replay and duplicate detection.
- `total_tokens` is authoritative for throughput. Cached input and reasoning
  output are subsets used for breakdowns and must not be added again.
- Forked and subagent logs can rewrite parent history timestamps during replay.
  Legacy replay can mix UUIDv4 turn IDs with current UUIDv7 IDs. Preserve the
  fork state machine and cross-file deduplication tests.
- Do not persist, log, transmit, or render prompt or response bodies.
- Network access is limited to GitHub release metadata and assets for automatic
  update checks and user-confirmed installation.

## Development

- Build: `swift build`
- Test: `swift test`
- Snapshot: `swift run codex-tps-snapshot --json`
- Package: `./scripts/build-app.sh`
- Universal DMG: `./scripts/build-dmg.sh`
- Install: `./scripts/install.sh`
- Install latest release: `./scripts/install-release.sh`

## Windows Command Handoff

- Do not execute PowerShell (`powershell.exe` or `pwsh.exe`) from an agent
  sandbox and do not request permission to run it outside the sandbox.
- When Windows-host validation, process control, packaging, release, or cleanup
  requires PowerShell, provide a complete, copy-paste-ready PowerShell block and
  explicitly label it for the user to run manually.
- Continue only from the output the user returns. Do not describe a PowerShell
  command as already executed unless the user supplied its successful output.

## Temporary Files And Cleanup

- Put all task-specific test, extraction, staging, and smoke-test output beneath
  one dedicated root directory, preferably
  `$env:TEMP\CodexTPS\<task-name-or-version>`. Do not scatter temporary files
  across multiple locations.
- Create a unique safety marker inside that root before writing generated files.
  Cleanup scripts must verify both the expected absolute root path and marker
  before deleting anything recursively.
- Prefer permanent deletion of the verified disposable root with
  `Remove-Item -LiteralPath $root -Recurse -Force`; this must be handed to the
  user as PowerShell under the Windows command rule above.
- Never delete generated files one by one through an IDE or file tool that sends
  each file separately to the Windows Recycle Bin. If permanent deletion is not
  appropriate, move everything under the single disposable root first and send
  only that containing folder to the Recycle Bin.
- Preserve user settings, release artifacts, source files, and unrelated build
  outputs unless the current task explicitly places them in scope. Use backup
  and restoration markers for tests that temporarily replace user settings.

Runtime and packaging claims require a real installed-app readback in addition
to unit tests.

<!-- CODEGRAPH_START -->
## CodeGraph

- This repository uses the local `.codegraph/` index; it must remain Git ignored.
- Prefer CodeGraph for symbol, caller, impact, and flow queries. Use `rg` for
  literal text searches.
- Run `codegraph init .` or `codegraph sync .` when the index is missing or stale.
<!-- CODEGRAPH_END -->
