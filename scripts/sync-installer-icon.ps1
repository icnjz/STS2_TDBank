param(
    [string]$SourceLogo = "",
    [string]$DestinationIcon = "",
    [string]$PreviewPath = ""
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
if ([string]::IsNullOrWhiteSpace($SourceLogo))
{
    $SourceLogo = Join-Path $repositoryRoot "TDBank\Assets\bank_logo.png"
}
if ([string]::IsNullOrWhiteSpace($DestinationIcon))
{
    $DestinationIcon = Join-Path $repositoryRoot "Installer\setup.ico"
}

$SourceLogo = [System.IO.Path]::GetFullPath($SourceLogo)
$DestinationIcon = [System.IO.Path]::GetFullPath($DestinationIcon)
$pngMagic = [byte[]](0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A)
$sizes = [int[]](16, 24, 32, 48, 64, 128, 256)

function Test-BytePrefix
{
    param(
        [byte[]]$Bytes,
        [byte[]]$Prefix
    )

    if ($Bytes.Length -lt $Prefix.Length)
    {
        return $false
    }
    for ($index = 0; $index -lt $Prefix.Length; $index++)
    {
        if ($Bytes[$index] -ne $Prefix[$index])
        {
            return $false
        }
    }
    return $true
}

$sourceBytes = [System.IO.File]::ReadAllBytes($SourceLogo)
if (!(Test-BytePrefix -Bytes $sourceBytes -Prefix $pngMagic))
{
    throw "The installer logo must be a real PNG file: $SourceLogo"
}

$frames = New-Object System.Collections.Generic.List[byte[]]
$source = New-Object System.Drawing.Bitmap($SourceLogo)
try
{
    if ($source.Width -ne $source.Height)
    {
        throw "The installer logo must be square: $($source.Width)x$($source.Height)"
    }
    if ($source.GetPixel(0, 0).A -ne 0 -or
        $source.GetPixel($source.Width - 1, 0).A -ne 0 -or
        $source.GetPixel(0, $source.Height - 1).A -ne 0 -or
        $source.GetPixel($source.Width - 1, $source.Height - 1).A -ne 0)
    {
        throw "The installer logo corners must be transparent."
    }

    foreach ($size in $sizes)
    {
        $bitmap = New-Object System.Drawing.Bitmap(
            $size,
            $size,
            [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try
        {
            $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
            try
            {
                $graphics.Clear([System.Drawing.Color]::Transparent)
                $graphics.CompositingMode =
                    [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
                $graphics.CompositingQuality =
                    [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
                $graphics.InterpolationMode =
                    [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.PixelOffsetMode =
                    [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $graphics.SmoothingMode =
                    [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
                $destination = New-Object System.Drawing.Rectangle(0, 0, $size, $size)
                $sourceRectangle = New-Object System.Drawing.Rectangle(
                    0,
                    0,
                    $source.Width,
                    $source.Height)
                $attributes = New-Object System.Drawing.Imaging.ImageAttributes
                try
                {
                    $attributes.SetWrapMode(
                        [System.Drawing.Drawing2D.WrapMode]::TileFlipXY)
                    $graphics.DrawImage(
                        $source,
                        $destination,
                        0,
                        0,
                        $source.Width,
                        $source.Height,
                        [System.Drawing.GraphicsUnit]::Pixel,
                        $attributes)
                }
                finally
                {
                    $attributes.Dispose()
                }
            }
            finally
            {
                $graphics.Dispose()
            }

            if ($bitmap.GetPixel(0, 0).A -ne 0 -or
                $bitmap.GetPixel($size - 1, $size - 1).A -ne 0)
            {
                throw "Generated ${size}x${size} icon frame lost transparency."
            }

            $stream = New-Object System.IO.MemoryStream
            try
            {
                $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
                $frame = $stream.ToArray()
                if (!(Test-BytePrefix -Bytes $frame -Prefix $pngMagic))
                {
                    throw "Generated ${size}x${size} icon frame is not PNG."
                }
                $frames.Add($frame)
            }
            finally
            {
                $stream.Dispose()
            }
        }
        finally
        {
            $bitmap.Dispose()
        }
    }
}
finally
{
    $source.Dispose()
}

$destinationDirectory = Split-Path -Parent $DestinationIcon
[System.IO.Directory]::CreateDirectory($destinationDirectory) | Out-Null
$temporaryIcon = Join-Path $destinationDirectory (
    "." + [System.IO.Path]::GetFileName($DestinationIcon) + "." +
    [Guid]::NewGuid().ToString("N") + ".tmp")

try
{
    $fileStream = New-Object System.IO.FileStream(
        $temporaryIcon,
        [System.IO.FileMode]::CreateNew,
        [System.IO.FileAccess]::Write,
        [System.IO.FileShare]::None)
    $writer = New-Object System.IO.BinaryWriter($fileStream)
    try
    {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$frames.Count)
        $offset = 6 + (16 * $frames.Count)
        for ($index = 0; $index -lt $frames.Count; $index++)
        {
            $size = $sizes[$index]
            if ($size -eq 256)
            {
                $writer.Write([byte]0)
                $writer.Write([byte]0)
            }
            else
            {
                $writer.Write([byte]$size)
                $writer.Write([byte]$size)
            }
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$frames[$index].Length)
            $writer.Write([uint32]$offset)
            $offset += $frames[$index].Length
        }
        foreach ($frame in $frames)
        {
            $writer.Write($frame)
        }
    }
    finally
    {
        $writer.Dispose()
    }

    [System.IO.File]::Copy($temporaryIcon, $DestinationIcon, $true)
}
finally
{
    if (Test-Path -LiteralPath $temporaryIcon)
    {
        [System.IO.File]::Delete($temporaryIcon)
    }
}

if (![string]::IsNullOrWhiteSpace($PreviewPath))
{
    $PreviewPath = [System.IO.Path]::GetFullPath($PreviewPath)
    [System.IO.Directory]::CreateDirectory((Split-Path -Parent $PreviewPath)) |
        Out-Null
    [System.IO.File]::WriteAllBytes(
        $PreviewPath,
        $frames[$frames.Count - 1])
}

$iconBytes = [System.IO.File]::ReadAllBytes($DestinationIcon)
$count = [System.BitConverter]::ToUInt16($iconBytes, 4)
if ($count -ne $sizes.Count)
{
    throw "Generated icon has the wrong frame count: $count"
}
for ($index = 0; $index -lt $count; $index++)
{
    $entryOffset = 6 + (16 * $index)
    $frameSize = [System.BitConverter]::ToUInt32($iconBytes, $entryOffset + 8)
    $frameOffset = [System.BitConverter]::ToUInt32($iconBytes, $entryOffset + 12)
    $frameBytes = New-Object byte[] $frameSize
    [System.Array]::Copy(
        $iconBytes,
        [int]$frameOffset,
        $frameBytes,
        0,
        [int]$frameSize)
    if (!(Test-BytePrefix -Bytes $frameBytes -Prefix $pngMagic))
    {
        throw "Generated icon frame $index is not PNG."
    }
}

$hash = (Get-FileHash -LiteralPath $DestinationIcon -Algorithm SHA256).Hash
Write-Host "Installer icon synchronized from $SourceLogo"
Write-Host "ICO: $DestinationIcon"
Write-Host "SHA256: $hash"
