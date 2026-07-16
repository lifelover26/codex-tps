$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$sourcePng = Join-Path $repoRoot 'Resources\AppIcon.png'
$targetIco = Join-Path $repoRoot 'windows\src\CodexTPSTray\Resources\AppIcon.ico'

$guid = [guid]::NewGuid().ToString('N')
$tempIco = Join-Path $repoRoot "windows\src\CodexTPSTray\Resources\AppIcon.$guid.tmp.ico"
$backupIco = Join-Path $repoRoot "windows\src\CodexTPSTray\Resources\AppIcon.$guid.bak.ico"

$backupCreated = $false
$newIconPromoted = $false
$rollbackFailed = $false

try {
    Add-Type -AssemblyName System.Drawing

    $sourceImage = [System.Drawing.Image]::FromFile($sourcePng)
    $originalSourceWidth = $sourceImage.Width
    $originalSourceHeight = $sourceImage.Height

    try {
        $sizes = @(16, 32, 48, 64, 128, 256)
        $pngDataList = New-Object System.Collections.ArrayList

        foreach ($size in $sizes) {
            $bitmap = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)

            try {
                $graphics = [System.Drawing.Graphics]::FromImage($bitmap)

                try {
                    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
                    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                    $graphics.Clear([System.Drawing.Color]::Transparent)

                    $scale = [Math]::Min($size / $sourceImage.Width, $size / $sourceImage.Height)
                    $width = [int]($sourceImage.Width * $scale)
                    $height = [int]($sourceImage.Height * $scale)
                    $x = ($size - $width) / 2
                    $y = ($size - $height) / 2

                    $graphics.DrawImage($sourceImage, [int]$x, [int]$y, $width, $height)

                    $memoryStream = New-Object System.IO.MemoryStream

                    try {
                        $bitmap.Save($memoryStream, [System.Drawing.Imaging.ImageFormat]::Png)
                        $pngData = $memoryStream.ToArray()
                        [void]$pngDataList.Add($pngData)
                    } finally {
                        $memoryStream.Dispose()
                    }
                } finally {
                    $graphics.Dispose()
                }
            } finally {
                $bitmap.Dispose()
            }
        }

        $headerSize = 6
        $entrySize = 16
        $totalDirectorySize = $headerSize + ($sizes.Count * $entrySize)

        $totalPngSize = 0
        foreach ($pngData in $pngDataList) {
            $totalPngSize += $pngData.Length
        }

        $totalSize = $totalDirectorySize + $totalPngSize

        $outputStream = New-Object System.IO.FileStream($tempIco, [System.IO.FileMode]::Create)

        try {
            $binaryWriter = New-Object System.IO.BinaryWriter($outputStream)

            try {
                $binaryWriter.Write([UInt16]0)
                $binaryWriter.Write([UInt16]1)
                $binaryWriter.Write([UInt16]$sizes.Count)

                $dataOffset = $totalDirectorySize

                for ($i = 0; $i -lt $sizes.Count; $i++) {
                    $size = $sizes[$i]
                    $pngData = $pngDataList[$i]

                    if ($size -eq 256) {
                        $binaryWriter.Write([byte]0)
                        $binaryWriter.Write([byte]0)
                    } else {
                        $binaryWriter.Write([byte]$size)
                        $binaryWriter.Write([byte]$size)
                    }

                    $binaryWriter.Write([byte]0)
                    $binaryWriter.Write([byte]0)
                    $binaryWriter.Write([UInt16]1)
                    $binaryWriter.Write([UInt16]32)
                    $binaryWriter.Write([UInt32]$pngData.Length)
                    $binaryWriter.Write([UInt32]$dataOffset)

                    $dataOffset += $pngData.Length
                }

                foreach ($pngData in $pngDataList) {
                    $binaryWriter.Write($pngData)
                }
            } finally {
                $binaryWriter.Dispose()
            }
        } finally {
            $outputStream.Dispose()
        }
    } finally {
        $sourceImage.Dispose()
    }

    $fileInfo = Get-Item $tempIco
    $fileLength = $fileInfo.Length

    $validateStream = New-Object System.IO.FileStream($tempIco, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read)

    try {
        $reader = New-Object System.IO.BinaryReader($validateStream)

        try {
            $reserved = $reader.ReadUInt16()
            $type = $reader.ReadUInt16()
            $count = $reader.ReadUInt16()

            if ($reserved -ne 0) {
                throw "Invalid ICO reserved field: $reserved"
            }

            if ($type -ne 1) {
                throw "Invalid ICO type: $type"
            }

            if ($count -ne 6) {
                throw "Expected 6 directory entries, got $count"
            }

            $pngSignature = [byte[]]@(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A)

            for ($i = 0; $i -lt 6; $i++) {
                $width = $reader.ReadByte()
                $height = $reader.ReadByte()
                $colorCount = $reader.ReadByte()
                $reservedByte = $reader.ReadByte()
                $colorPlanes = $reader.ReadUInt16()
                $bitsPerPixel = $reader.ReadUInt16()
                $length = $reader.ReadUInt32()
                $entryOffset = $reader.ReadUInt32()

                $expectedSize = $sizes[$i]
                if ($expectedSize -eq 256) {
                    if ($width -ne 0 -or $height -ne 0) {
                        throw "Entry $i (256x256) has invalid width/height: $width/$height"
                    }
                } else {
                    if ($width -ne $expectedSize -or $height -ne $expectedSize) {
                        throw "Entry $i has invalid width/height: $width/$height (expected $expectedSize)"
                    }
                }

                if ($reservedByte -ne 0) {
                    throw "Entry $i has non-zero reserved byte: $reservedByte"
                }

                if ($colorPlanes -ne 1) {
                    throw "Entry $i has invalid color planes: $colorPlanes"
                }

                if ($bitsPerPixel -ne 32) {
                    throw "Entry $i has invalid bits per pixel: $bitsPerPixel"
                }

                if ($length -lt 8) {
                    throw "Entry $i has invalid length: $length"
                }

                if ($entryOffset + $length -gt $fileLength) {
                    throw "Entry $i offset+length ($($entryOffset + $length)) exceeds file length ($fileLength)"
                }

                $originalPos = $validateStream.Position
                $validateStream.Position = $entryOffset
                $signature = $reader.ReadBytes(8)

                $match = $true
                for ($j = 0; $j -lt 8; $j++) {
                    if ($signature[$j] -ne $pngSignature[$j]) {
                        $match = $false
                        break
                    }
                }

                if (-not $match) {
                    throw "Entry $i does not start with PNG signature"
                }

                $validateStream.Position = $originalPos
            }
        } finally {
            $reader.Dispose()
        }
    } finally {
        $validateStream.Dispose()
    }

    Write-Host "Promoting ICO..."

    if (Test-Path $targetIco) {
        Move-Item $targetIco $backupIco -Force
        $backupCreated = $true
        Write-Host "Backed up existing ICO to $backupIco"
    }

    Move-Item $tempIco $targetIco -Force
    $newIconPromoted = $true

    Write-Host "AppIcon.ico created successfully with 6 entries"
}
catch {
    $originalError = $_
    Write-Warning "AppIcon creation failed: $originalError"

    try {
        Write-Host "Rolling back..."

        if ($newIconPromoted -and (Test-Path $targetIco)) {
            Write-Host "Removing newly promoted ICO"
            Remove-Item $targetIco -Force
        }

        if ($backupCreated -and (Test-Path $backupIco)) {
            try {
                Write-Host "Restoring backup ICO"
                Move-Item $backupIco $targetIco -Force
            }
            catch {
                Write-Warning "Failed to restore backup ICO: $_"
                Write-Warning "Backup ICO preserved at: $backupIco"
                $rollbackFailed = $true
            }
        }
    }
    catch {
        Write-Warning "Error during rollback: $_"
        $rollbackFailed = $true
    }

    throw $originalError
}
finally {
    if (Test-Path $tempIco) {
        Remove-Item $tempIco -Force -ErrorAction SilentlyContinue
    }

    $shouldRemoveBackup = $false
    if ($newIconPromoted -and -not $rollbackFailed) {
        $shouldRemoveBackup = $true
    }
    elseif (-not $newIconPromoted -and $backupCreated -and -not $rollbackFailed) {
        $shouldRemoveBackup = $true
    }

    if ($shouldRemoveBackup -and (Test-Path $backupIco)) {
        Remove-Item $backupIco -Force -ErrorAction SilentlyContinue
    }

    if ($rollbackFailed -and (Test-Path $backupIco)) {
        Write-Warning "Backup ICO preserved due to rollback failure: $backupIco"
    }
}
