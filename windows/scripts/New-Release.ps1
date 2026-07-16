param(
    [Parameter(Mandatory=$true)]
    [string]$Version
)

$ErrorActionPreference = 'Stop'

if ($Version -notmatch '^\d+\.\d+\.\d+(-[a-zA-Z0-9]+)?$') {
    Write-Error "Invalid version format. Use x.y.z or x.y.z-prerelease"
    exit 1
}

$versionParts = $Version -split '\.'
$major = $versionParts[0]
$minor = $versionParts[1]
$patchPart = $versionParts[2] -split '-'
$patch = $patchPart[0]
$assemblyVersion = "$major.$minor.$patch.0"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$windowsDir = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($repoRoot, 'windows'))
$artifactsDir = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($windowsDir, 'artifacts'))
$stagingDir = Join-Path $artifactsDir ".staging\$([guid]::NewGuid())"

$windowsDirWithSeparator = if ($windowsDir.EndsWith([System.IO.Path]::DirectorySeparatorChar)) { $windowsDir } else { $windowsDir + [System.IO.Path]::DirectorySeparatorChar }
$artifactsDirWithSeparator = if ($artifactsDir.EndsWith([System.IO.Path]::DirectorySeparatorChar)) { $artifactsDir } else { $artifactsDir + [System.IO.Path]::DirectorySeparatorChar }

if (-not $artifactsDirWithSeparator.StartsWith($windowsDirWithSeparator, [System.StringComparison]::OrdinalIgnoreCase)) {
    Write-Error "Artifacts directory is not inside windows directory"
    exit 1
}

$publishDir = Join-Path $stagingDir 'publish'
$zipContentDir = Join-Path $stagingDir 'zip-content'
$backupDir = Join-Path $stagingDir 'backup'

$zipName = "Codex-TPS-Windows-x64-Portable-$Version.zip"
$zipPath = Join-Path $artifactsDir $zipName
$checksumPath = "$zipPath.sha256"

$stagingZipPath = Join-Path $stagingDir $zipName
$stagingChecksumPath = "$stagingZipPath.sha256"

$backupZipPath = $null
$backupChecksumPath = $null
$promotionStarted = $false
$rollbackCompleted = $false

try {
    New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
    New-Item -ItemType Directory -Path $zipContentDir -Force | Out-Null
    New-Item -ItemType Directory -Path $backupDir -Force | Out-Null

    Write-Host "Publishing version $Version (AssemblyVersion: $assemblyVersion)"

    dotnet publish `
        ([System.IO.Path]::Combine($repoRoot, 'windows\src\CodexTPSTray\CodexTPSTray.csproj')) `
        /p:PublishProfile=Properties\PublishProfiles\win-x64.pubxml `
        --output $publishDir `
        /p:Version=$Version `
        /p:AssemblyVersion=$assemblyVersion `
        /p:FileVersion=$assemblyVersion `
        /p:InformationalVersion=$Version `
        /p:IncludeSourceRevisionInInformationalVersion=false `
        /p:DebugType=None `
        /p:DebugSymbols=false

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE"
    }

    $publishItems = Get-ChildItem -Path $publishDir
    if ($publishItems.Count -ne 1) {
        throw "Expected exactly 1 publish entry, found $($publishItems.Count): $($publishItems.Name -join ', ')"
    }

    $publishItem = $publishItems[0]
    if (-not $publishItem.PSIsContainer -and $publishItem.Name -eq 'CodexTPSTray.exe') {
        Write-Host "Publish validation passed: single executable found"
    } else {
        throw "Publish output is not a regular file named CodexTPSTray.exe: $($publishItem.Name) (is directory: $($publishItem.PSIsContainer))"
    }

    Write-Host "Copying files to ZIP content..."

    Copy-Item ([System.IO.Path]::Combine($publishDir, 'CodexTPSTray.exe')) $zipContentDir -Force
    Copy-Item ([System.IO.Path]::Combine($repoRoot, 'LICENSE')) $zipContentDir -Force
    Copy-Item ([System.IO.Path]::Combine($windowsDir, 'README.md')) ([System.IO.Path]::Combine($zipContentDir, 'README.md')) -Force

    Write-Host "Creating ZIP..."
    Compress-Archive -Path ([System.IO.Path]::Combine($zipContentDir, '*')) -DestinationPath $stagingZipPath -Force

    Write-Host "Validating ZIP archive..."
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zipArchive = [System.IO.Compression.ZipFile]::OpenRead($stagingZipPath)
    try {
        $zipEntries = $zipArchive.Entries | Where-Object { $_.FullName -notmatch '^/' }
        $entryNames = $zipEntries | ForEach-Object { $_.FullName }
        $expectedEntries = @('CodexTPSTray.exe', 'LICENSE', 'README.md')

        if ($entryNames.Count -ne 3) {
            throw "Expected exactly 3 ZIP entries, found $($entryNames.Count): $($entryNames -join ', ')"
        }

        foreach ($expected in $expectedEntries) {
            if ($entryNames -notcontains $expected) {
                throw "Missing expected ZIP entry: $expected"
            }
        }

        foreach ($entry in $entryNames) {
            if ($expectedEntries -notcontains $entry) {
                throw "Unexpected ZIP entry: $entry"
            }
        }

        Write-Host "ZIP archive validation passed"
    } finally {
        $zipArchive.Dispose()
    }

    Write-Host "Validating version in executable..."
    $exePath = [System.IO.Path]::Combine($publishDir, 'CodexTPSTray.exe')
    $exeFileInfo = Get-Item $exePath
    $fileVersion = $exeFileInfo.VersionInfo.FileVersion
    $productVersion = $exeFileInfo.VersionInfo.ProductVersion

    if ($fileVersion -ne $assemblyVersion) {
        throw "FileVersion mismatch: expected $assemblyVersion, got $fileVersion"
    }

    if ($productVersion -ne $Version) {
        throw "ProductVersion mismatch: expected $Version, got $productVersion"
    }

    Write-Host "Version validation passed"

    Write-Host "Generating SHA-256 checksum..."
    $hash = (Get-FileHash $stagingZipPath -Algorithm SHA256).Hash.ToLower()
    $checksumLine = "$hash  $zipName`n"
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($stagingChecksumPath, $checksumLine, $utf8NoBom)

    Write-Host "Validating checksum..."
    $readHash = (Get-Content $stagingChecksumPath -Raw).Split('  ')[0].Trim()
    if ($readHash -ne $hash) {
        throw "Checksum validation failed: expected $hash, got $readHash"
    }

    Write-Host "Backing up existing artifacts..."
    if (Test-Path $zipPath) {
        $backupZipPath = Join-Path $backupDir $zipName
        Move-Item $zipPath $backupZipPath -Force
        Write-Host "Backed up existing ZIP to $backupZipPath"
    }

    if (Test-Path $checksumPath) {
        $backupChecksumPath = Join-Path $backupDir (Split-Path $checksumPath -Leaf)
        Move-Item $checksumPath $backupChecksumPath -Force
        Write-Host "Backed up existing checksum to $backupChecksumPath"
    }

    Write-Host "Promoting artifacts..."
    $promotionStarted = $true

    Move-Item $stagingZipPath $zipPath -Force
    Write-Host "Promoted ZIP"

    Move-Item $stagingChecksumPath $checksumPath -Force
    Write-Host "Promoted checksum"

    Write-Host "Final validation..."
    if (-not (Test-Path $zipPath)) {
        throw "Final validation failed: ZIP missing at $zipPath"
    }

    if (-not (Test-Path $checksumPath)) {
        throw "Final validation failed: checksum missing at $checksumPath"
    }

    $finalHash = (Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLower()
    $finalChecksumContent = (Get-Content $checksumPath -Raw).Split('  ')[0].Trim()
    if ($finalHash -ne $finalChecksumContent) {
        throw "Final hash validation failed: file hash $finalHash vs checksum $finalChecksumContent"
    }

    $rollbackCompleted = $true

    $zipSize = (Get-Item $zipPath).Length
    Write-Host "Release $Version created successfully"
    Write-Host "ZIP: $zipPath ($zipSize bytes)"
    Write-Host "Checksum: $checksumPath"
}
catch {
    $originalError = $_
    Write-Warning "Release creation failed: $originalError"

    try {
        if ($promotionStarted) {
            Write-Host "Rolling back partially promoted artifacts..."

            if (Test-Path $zipPath) {
                Write-Host "Removing partially promoted ZIP"
                Remove-Item $zipPath -Force
            }

            if (Test-Path $checksumPath) {
                Write-Host "Removing partially promoted checksum"
                Remove-Item $checksumPath -Force
            }
        }

        Write-Host "Restoring backups..."
        $rollbackFailed = $false

        if ($backupZipPath -and (Test-Path $backupZipPath)) {
            try {
                Write-Host "Restoring backup ZIP"
                Move-Item $backupZipPath $zipPath -Force
            }
            catch {
                Write-Warning "Failed to restore backup ZIP: $_"
                $rollbackFailed = $true
            }
        }

        if ($backupChecksumPath -and (Test-Path $backupChecksumPath)) {
            try {
                Write-Host "Restoring backup checksum"
                Move-Item $backupChecksumPath $checksumPath -Force
            }
            catch {
                Write-Warning "Failed to restore backup checksum: $_"
                $rollbackFailed = $true
            }
        }

        if (-not $rollbackFailed) {
            $rollbackCompleted = $true
            Write-Host "Rollback completed successfully"
        }
        else {
            Write-Warning "Rollback failed, staging preserved at: $stagingDir"
        }
    }
    catch {
        Write-Warning "Error during rollback: $_"
        Write-Warning "Staging preserved at: $stagingDir"
    }

    throw $originalError
}
finally {
    $shouldCleanup = $true

    if (-not $rollbackCompleted) {
        $backupExists = ($backupZipPath -and (Test-Path $backupZipPath)) -or ($backupChecksumPath -and (Test-Path $backupChecksumPath))
        if ($backupExists) {
            $shouldCleanup = $false
            Write-Warning "Staging preserved due to rollback failure: $stagingDir"
        }
    }

    if ($shouldCleanup -and (Test-Path $stagingDir)) {
        Remove-Item -Recurse -Force $stagingDir -ErrorAction SilentlyContinue
    }
}
