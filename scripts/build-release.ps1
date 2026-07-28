param(
    [Parameter(Mandatory = $true)]
    [string]$Sts2Path,
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$OutputDirectory = ""
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$gameRoot = [System.IO.Path]::GetFullPath($Sts2Path)
$gameData = Join-Path $gameRoot "data_sts2_windows_x86_64"

if (!(Test-Path -LiteralPath (Join-Path $gameData "sts2.dll")) -or
    !(Test-Path -LiteralPath (Join-Path $gameData "0Harmony.dll")))
{
    throw "The selected public-beta game folder does not contain the required reference assemblies."
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory))
{
    $OutputDirectory = Join-Path $repositoryRoot "artifacts\release-v0.1"
}

$releaseRoot = [System.IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $releaseRoot)
{
    if (Get-ChildItem -LiteralPath $releaseRoot -Force | Select-Object -First 1)
    {
        throw "Output directory is not empty: $releaseRoot"
    }
}
else
{
    New-Item -ItemType Directory -Path $releaseRoot | Out-Null
}

$workRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("tdbank-build-" + [Guid]::NewGuid().ToString("N"))
$runtimeRoot = Join-Path $workRoot "runtime"
$testModsRoot = Join-Path $workRoot "test-mods"
$setupRoot = Join-Path $releaseRoot "Setup"
$generatedIcon = Join-Path $workRoot "setup.ico"
$platformSdkRoot = Join-Path $workRoot "platform-sdk"
$platformSdkPath = $platformSdkRoot.TrimEnd("\", "/") + [System.IO.Path]::DirectorySeparatorChar
$modsPath = $runtimeRoot.TrimEnd("\", "/") + [System.IO.Path]::DirectorySeparatorChar
$testModsPath = $testModsRoot.TrimEnd("\", "/") + [System.IO.Path]::DirectorySeparatorChar

New-Item -ItemType Directory -Path $runtimeRoot, $testModsRoot, $setupRoot, $platformSdkRoot | Out-Null

function Invoke-DotNet
{
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0)
    {
        throw "dotnet exited with code $LASTEXITCODE"
    }
}

try
{
    & (Join-Path $repositoryRoot "scripts\sync-installer-icon.ps1") `
        -SourceLogo (Join-Path $repositoryRoot "TDBank\Assets\bank_logo.png") `
        -DestinationIcon $generatedIcon

    Push-Location $repositoryRoot
    try
    {
        Invoke-DotNet restore "TDBank.csproj" "--locked-mode" "/p:Sts2Path=$gameRoot"
        Invoke-DotNet restore "Tests\TDBank.LogicSmokeTests.csproj" "--locked-mode" "/p:Sts2Path=$gameRoot"
        Invoke-DotNet restore "Installer.Tests\TDBank.Setup.Tests.csproj" "--locked-mode" "/p:TargetPlatformSdkPath=$platformSdkPath" "/p:TargetPlatformDisplayName=Windows"
        Invoke-DotNet build "TDBank.csproj" "-c" $Configuration "--no-restore" "/p:Sts2Path=$gameRoot" "/p:ModsPath=$modsPath"
        Invoke-DotNet run "--project" "Tests\TDBank.LogicSmokeTests.csproj" "-c" $Configuration "--no-restore" "/p:Sts2Path=$gameRoot" "/p:ModsPath=$testModsPath"
        Invoke-DotNet build "Installer.Tests\TDBank.Setup.Tests.csproj" "-c" $Configuration "--no-restore" "/p:PayloadRoot=$runtimeRoot" "/p:TargetPlatformSdkPath=$platformSdkPath" "/p:TargetPlatformDisplayName=Windows"
        Invoke-DotNet run "--project" "Installer.Tests\TDBank.Setup.Tests.csproj" "-c" $Configuration "--no-build" "/p:TargetPlatformSdkPath=$platformSdkPath" "/p:TargetPlatformDisplayName=Windows"
        Invoke-DotNet publish "Installer\TDBank.Setup.csproj" "-c" $Configuration "-r" "win-x64" "--self-contained" "true" "--no-restore" "-o" $setupRoot "/p:PayloadRoot=$runtimeRoot" "/p:SetupIconPath=$generatedIcon" "/p:TargetPlatformSdkPath=$platformSdkPath" "/p:TargetPlatformDisplayName=Windows"
    }
    finally
    {
        Pop-Location
    }

    Copy-Item -LiteralPath (Join-Path $repositoryRoot "LICENSE") -Destination (Join-Path $releaseRoot "LICENSE.txt")
    Copy-Item -LiteralPath (Join-Path $repositoryRoot "THIRD_PARTY_NOTICES.md") -Destination $releaseRoot
    Copy-Item -LiteralPath (Join-Path $repositoryRoot "TDLib\THIRD_PARTY_LICENSES\BaseLib-LICENSE.txt") -Destination (Join-Path $releaseRoot "BaseLib-LICENSE.txt")

    $dotnetRoot = Split-Path -Parent (Get-Command dotnet).Source
    Copy-Item -LiteralPath (Join-Path $dotnetRoot "LICENSE.txt") -Destination (Join-Path $releaseRoot "DOTNET-LICENSE.txt")
    Copy-Item -LiteralPath (Join-Path $dotnetRoot "ThirdPartyNotices.txt") -Destination (Join-Path $releaseRoot "DOTNET-ThirdPartyNotices.txt")

    $hashLines = Get-ChildItem -LiteralPath $releaseRoot -Recurse -File |
        Sort-Object FullName |
        ForEach-Object {
            $relative = $_.FullName.Substring($releaseRoot.TrimEnd("\", "/").Length + 1).Replace("\", "/")
            $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
            "$hash  $relative"
        }
    [System.IO.File]::WriteAllLines(
        (Join-Path $releaseRoot "SHA256SUMS.txt"),
        $hashLines,
        [System.Text.UTF8Encoding]::new($false))
}
finally
{
    $tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd("\", "/") + [System.IO.Path]::DirectorySeparatorChar
    $resolvedWorkRoot = [System.IO.Path]::GetFullPath($workRoot)
    if ($resolvedWorkRoot.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $resolvedWorkRoot).StartsWith("tdbank-build-", [StringComparison]::Ordinal))
    {
        Remove-Item -LiteralPath $resolvedWorkRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Write-Host "Release created at $releaseRoot"
