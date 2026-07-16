# Codex TPS - Windows Portable

A Windows 11 tray application for monitoring Codex token throughput. This is a **no-install, self-contained Portable build**.

## Portable Runtime Requirements

- Windows 11 x64
- No .NET runtime installation required (self-contained)

## Build-from-Source Requirements

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
- `windows/artifacts/Codex-TPS-Windows-x64-Portable-<version>.zip`
- `windows/artifacts/Codex-TPS-Windows-x64-Portable-<version>.zip.sha256`

## Artifact Layout

The ZIP contains:
- `CodexTPSTray.exe` - Self-contained single-file executable
- `LICENSE` - MIT license
- `README.md` - This file

## Installation

This is a **Portable** build - no installer is required:

1. Extract the ZIP to a stable writable directory, for example:
   ```
   %USERPROFILE%\Apps\CodexTPS
   ```

2. Do **not** run it directly from inside the ZIP or from a temporary directory.

3. No separately installed .NET runtime is required - the executable is self-contained.

## Settings

Settings are stored per-user at:
```
%LOCALAPPDATA%\CodexTPS\settings.json
```

## Launch at Login

The "Launch at Login" menu option writes the current executable path to the current-user Run key in the registry.

**Important:** Disable Launch at Login before moving or deleting the portable directory. Moving the directory while Launch at Login is enabled will leave a stale path in the registry.

## Manual Update Workflow

1. Exit Codex TPS.
2. Extract the new package.
3. Replace the old portable files.
4. Start the new executable.
5. Existing settings remain preserved in `%LOCALAPPDATA%\CodexTPS\`.

## Removal Workflow

1. Disable Launch at Login while the app is still running (via the tray menu).
2. Exit Codex TPS.
3. Delete the portable directory.
4. Optionally delete `%LOCALAPPDATA%\CodexTPS` to remove settings.

## Manual Run for Testing

```powershell
$testHome = Join-Path $env:TEMP "codex-tps-empty"
New-Item -ItemType Directory -Force -Path "$testHome\sessions" | Out-Null
$env:CODEX_HOME = $testHome
.\CodexTPSTray.exe
```

After running, **exit the app first**, then you can remove the temporary CODEX_HOME:

```powershell
Remove-Item -Recurse -Force $testHome
Remove-Item Env:CODEX_HOME -ErrorAction SilentlyContinue
```

## Checksum Verification

Verify the downloaded ZIP using PowerShell before extracting or running. **Do not run or extract the package if verification fails.**

```powershell
$zipPath = "Codex-TPS-Windows-x64-Portable-<version>.zip"
$checksumPath = "$zipPath.sha256"

$expectedHash = (Get-Content $checksumPath -Raw).Split('  ')[0].Trim()
$actualHash = (Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLower()

if ($expectedHash -eq $actualHash) {
    Write-Host "Checksum verified successfully"
} else {
    throw "Checksum verification failed"
}
```

## Status

- Windows x64 only
- Currently unsigned build - Windows may show a security warning
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