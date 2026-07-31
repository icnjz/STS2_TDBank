param(
    [Parameter(Mandatory = $true)]
    [string]$Sts2Path,
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$OutputDirectory = "",
    [string[]]$RegressionSts2Paths = @()
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$gameRoot = [System.IO.Path]::GetFullPath($Sts2Path)
$gameData = Join-Path $gameRoot "data_sts2_windows_x86_64"

if (!(Test-Path -LiteralPath (Join-Path $gameData "sts2.dll")) -or
    !(Test-Path -LiteralPath (Join-Path $gameData "0Harmony.dll")))
{
    throw "The selected Slay the Spire 2 folder does not contain the required reference assemblies."
}

$releaseInfoPath = Join-Path $gameRoot "release_info.json"
if (!(Test-Path -LiteralPath $releaseInfoPath))
{
    throw "The selected Slay the Spire 2 folder does not contain release_info.json."
}

$releaseInfo = Get-Content -LiteralPath $releaseInfoPath -Raw | ConvertFrom-Json
if ($releaseInfo.version -ne "v0.107.1")
{
    throw "Universal releases must be compiled against the lowest supported baseline, Steam Latest v0.107.1. Detected $($releaseInfo.version)."
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory))
{
    $OutputDirectory = Join-Path $repositoryRoot "artifacts\release-v0.1.3-lts"
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
$verifiedVersions = [System.Collections.Generic.List[string]]::new()
$verifiedVersions.Add("$($releaseInfo.version) ($($releaseInfo.commit)) baseline")

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
        foreach ($regressionPath in $RegressionSts2Paths)
        {
            $regressionRoot = [System.IO.Path]::GetFullPath($regressionPath)
            $regressionData = Join-Path $regressionRoot "data_sts2_windows_x86_64"
            $regressionInfoPath = Join-Path $regressionRoot "release_info.json"
            if (!(Test-Path -LiteralPath (Join-Path $regressionData "sts2.dll")) -or
                !(Test-Path -LiteralPath $regressionInfoPath))
            {
                throw "Regression game path is incomplete: $regressionRoot"
            }

            $regressionInfo = Get-Content -LiteralPath $regressionInfoPath -Raw | ConvertFrom-Json
            Invoke-DotNet restore "CompatibilityTests\TDBank.BinaryCompatibilitySmokeTests.csproj" "/p:Sts2Path=$regressionRoot"
            Invoke-DotNet run "--project" "CompatibilityTests\TDBank.BinaryCompatibilitySmokeTests.csproj" "-c" $Configuration "--no-restore" "/p:Sts2Path=$regressionRoot" "--" $runtimeRoot $regressionData
            $verifiedVersions.Add("$($regressionInfo.version) ($($regressionInfo.commit)) binary compatibility")
        }
        Invoke-DotNet build "Installer.Tests\TDBank.Setup.Tests.csproj" "-c" $Configuration "--no-restore" "/p:PayloadRoot=$runtimeRoot" "/p:TargetPlatformSdkPath=$platformSdkPath" "/p:TargetPlatformDisplayName=Windows"
        Invoke-DotNet run "--project" "Installer.Tests\TDBank.Setup.Tests.csproj" "-c" $Configuration "--no-build" "/p:TargetPlatformSdkPath=$platformSdkPath" "/p:TargetPlatformDisplayName=Windows" "--" "--test-ignore-live-game"
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

    [System.IO.File]::WriteAllLines(
        (Join-Path $releaseRoot "LTS-COMPATIBILITY.txt"),
        @(
            "TD Bank v0.1.3 LTS"
            "Minimum accepted game version: v0.107.1"
            "Verified builds:"
        ) + $verifiedVersions + @(
            ""
            "Newer semantic game versions are accepted by Setup in forward-compatible mode."
            "Unknown future save schemas are preserved fail-closed and are never rewritten."
            "If Harmony patch targets drift, TD Bank and TDLib self-disable instead of crashing the game."
        ),
        [System.Text.UTF8Encoding]::new($false))

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
