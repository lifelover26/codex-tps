# Codex TPS - Windows

A Windows 11 tray application for monitoring Codex token throughput.

## Prerequisites

- .NET 10 SDK (https://dotnet.microsoft.com/download/dotnet/10.0)
- PowerShell 7+ (for release packaging)

## Build

```powershell
dotnet build windows/CodexTPS.slnx
```

## Test

```powershell
dotnet test windows/CodexTPS.slnx
```

## Release Package

```powershell
.\windows\scripts\New-Release.ps1 -Version 0.1.0
```

This creates:
- `windows/artifacts/Codex-TPS-Windows-x64-<version>.zip`
- `windows/artifacts/Codex-TPS-Windows-x64-<version>.zip.sha256`

## Artifact Layout

The ZIP contains:
- `CodexTPSTray.exe` - Self-contained single-file executable
- `LICENSE` - MIT license
- `README.md` - This file

## Manual Run

```powershell
$testHome = Join-Path $env:TEMP "codex-tps-empty"
New-Item -ItemType Directory -Force -Path "$testHome\sessions" | Out-Null
$env:CODEX_HOME = $testHome
.\CodexTPSTray.exe
```

After running, **exit the app first**, then you can remove the temporary CODEX_HOME:

```powershell
Remove-Item -Recurse -Force $testHome
$env:CODEX_HOME = $null
```

## Status

- Windows x64 only
- Currently unsigned build
- No installer (manual deployment)
- No automatic updates

## Build from Source

```powershell
# Build
dotnet build windows/CodexTPS.slnx

# Test
dotnet test windows/CodexTPS.slnx

# Create release
.\windows\scripts\New-Release.ps1 -Version 0.1.0
```