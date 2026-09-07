[CmdletBinding()]
param(
    [ValidateSet("win-x64", "win-x86", "win-arm64")]
    [string]$Runtime = "win-x64",

    [ValidateSet("Release", "Portable", "uiAccessRelease")]
    [string]$Configuration = "Release",

    [string]$OutputDirectory,

    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$')]
    [string]$Version,

    [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')]
    [string]$Repository,

    [string]$PackagePath,

    [DateTimeOffset]$BuiltAtUtc = [DateTimeOffset]::UtcNow,

    [switch]$SkipLatest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"

$root = [IO.Path]::GetFullPath($PSScriptRoot)
$output = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    [IO.Path]::GetFullPath((Join-Path $root "artifacts\publish"))
} elseif ([IO.Path]::IsPathRooted($OutputDirectory)) {
    [IO.Path]::GetFullPath($OutputDirectory)
} else {
    [IO.Path]::GetFullPath((Join-Path $root $OutputDirectory))
}
$relativeOutput = [IO.Path]::GetRelativePath($root, $output)
if ($relativeOutput -eq "." -or [IO.Path]::IsPathRooted($relativeOutput) -or
    $relativeOutput.StartsWith("..", [StringComparison]::Ordinal)) {
    throw "The publish output must remain inside the repository root."
}
$latest = Join-Path $root "artifacts\latest"
$outputFromLatest = [IO.Path]::GetRelativePath($latest, $output)
$latestFromOutput = [IO.Path]::GetRelativePath($output, $latest)
if (-not $outputFromLatest.StartsWith("..") -or -not $latestFromOutput.StartsWith("..")) {
    throw "The publish work directory must not overlap artifacts\latest. Omit OutputDirectory to refresh the latest build."
}
$builtAt = $BuiltAtUtc.ToUniversalTime()
$expiresAt = $builtAt.AddMonths(3)
$lifecycleProperties = @(
    "-p:TouchPilotBuildBuiltAtUtcTicks=$($builtAt.UtcDateTime.Ticks)",
    "-p:TouchPilotBuildExpiresAtUtcTicks=$($expiresAt.UtcDateTime.Ticks)"
)
$releaseVersionProperties = @()
if (-not [string]::IsNullOrWhiteSpace($Version)) {
    $null = $Version -match '^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)'
    $releaseFileVersion = "$($Matches.major).$($Matches.minor).$($Matches.patch).0"
    $releaseVersionProperties = @(
        "-p:TouchPilotReleaseVersion=$Version",
        "-p:TouchPilotReleaseFileVersion=$releaseFileVersion"
    )
}
$buildProperties = $lifecycleProperties + $releaseVersionProperties

$resolvedPackagePath = $null
if (-not [string]::IsNullOrWhiteSpace($PackagePath)) {
    if ([string]::IsNullOrWhiteSpace($Version) -or [string]::IsNullOrWhiteSpace($Repository)) {
        throw "Version and Repository are required when PackagePath is specified."
    }

    $resolvedPackagePath = if ([IO.Path]::IsPathRooted($PackagePath)) {
        [IO.Path]::GetFullPath($PackagePath)
    } else {
        [IO.Path]::GetFullPath((Join-Path $root $PackagePath))
    }
    $relativePackage = [IO.Path]::GetRelativePath($root, $resolvedPackagePath)
    if ([IO.Path]::IsPathRooted($relativePackage) -or
        $relativePackage.StartsWith("..", [StringComparison]::Ordinal)) {
        throw "The release package must remain inside the repository root."
    }
    if (-not [IO.Path]::GetRelativePath($latest, $resolvedPackagePath).StartsWith("..")) {
        throw "The release package must not be written inside artifacts\latest."
    }
    $packageFromOutput = [IO.Path]::GetRelativePath($output, $resolvedPackagePath)
    if (-not [IO.Path]::IsPathRooted($packageFromOutput) -and
        -not $packageFromOutput.StartsWith("..", [StringComparison]::Ordinal)) {
        throw "The release package cannot be created inside the publish output directory."
    }
}

function Invoke-DotNet {
    param([Parameter(Mandatory)][string[]]$Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

for ($ancestor = $output; $ancestor -ne $root; $ancestor = Split-Path -Parent $ancestor) {
    if (Test-Path -LiteralPath $ancestor) {
        $item = Get-Item -LiteralPath $ancestor -Force
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Refusing a publish output with a reparse point: $ancestor"
        }
    }
}
if (Test-Path -LiteralPath $output) {
    $outputItem = Get-Item -LiteralPath $output -Force
    if (($outputItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Refusing to clean a publish output that is a reparse point: $output"
    }
    Remove-Item -LiteralPath $output -Recurse -Force
}
[void](New-Item -ItemType Directory -Path $output)

Invoke-DotNet -Arguments (@("build", (Join-Path $root "GestureSign.sln"), "-c", $Configuration,
    "--no-incremental") + $buildProperties)

$publishOptions = @(
    "-c", $Configuration,
    "-r", $Runtime,
    "--self-contained", "true",
    "--output", $output,
    "-p:DebugType=None",
    "-p:DebugSymbols=false"
) + $buildProperties

Invoke-DotNet -Arguments (@("publish", (Join-Path $root "GestureSign.ControlPanel\GestureSign.ControlPanel.csproj")) + $publishOptions)
Invoke-DotNet -Arguments (@("publish", (Join-Path $root "GestureSign.Daemon\GestureSign.Daemon.csproj")) + $publishOptions)

$daemonPath = Join-Path $output "TouchPilot.exe"
if (-not (Test-Path -LiteralPath $daemonPath -PathType Leaf)) {
    throw "The TouchPilot executable was not published."
}
# One transition alias lets existing GestureSign startup entries and updaters launch the renamed binary.
Copy-Item -LiteralPath $daemonPath -Destination (Join-Path $output "GestureSign.exe") -Force

$updaterPublishOptions = @(
    "-c", "Release",
    "-r", $Runtime,
    "--self-contained", "true",
    "--output", $output,
    "-p:DebugType=None",
    "-p:DebugSymbols=false"
) + $buildProperties
Invoke-DotNet -Arguments (@("publish", (Join-Path $root "GestureSign.Updater\GestureSign.Updater.csproj")) + $updaterPublishOptions)

$pluginOutput = Join-Path $root "GestureSign.ExtraPlugins"
$pluginSources = @(
    (Join-Path $pluginOutput "ClipboardMatch\bin\$Configuration\net10.0-windows10.0.19041.0\GestureSign.ClipboardMatch.Plugin.dll"),
    (Join-Path $pluginOutput "TextCopyer\bin\$Configuration\net10.0-windows10.0.19041.0\GestureSign.ExtraPlugins.TextCopyer.dll")
)
if ($pluginSources.Where({ -not (Test-Path -LiteralPath $_ -PathType Leaf) }).Count -ne 0) {
    Invoke-DotNet -Arguments @("build", (Join-Path $pluginOutput "ClipboardMatch\ClipboardMatch.csproj"), "-c", $Configuration, "--no-incremental")
    Invoke-DotNet -Arguments @("build", (Join-Path $pluginOutput "TextCopyer\TextCopyer.csproj"), "-c", $Configuration, "--no-incremental")
}
$pluginDirectory = Join-Path $output "Plugins"
[void](New-Item -ItemType Directory -Path $pluginDirectory)
foreach ($plugin in $pluginSources) {
    Copy-Item -LiteralPath $plugin -Destination $pluginDirectory -Force
}

$thirdPartyNoticesSource = Join-Path $root "THIRD-PARTY-NOTICES.txt"
if (-not (Test-Path -LiteralPath $thirdPartyNoticesSource -PathType Leaf)) {
    throw "The third-party notices file is missing: $thirdPartyNoticesSource"
}
Copy-Item -LiteralPath $thirdPartyNoticesSource -Destination $output -Force

$requiredFiles = @(
    "TouchPilot.exe",
    "TouchPilot.ControlPanel.exe",
    "TouchPilot.Updater.exe",
    "GestureSign.exe",
    "GestureSign.CorePlugins.dll",
    "Plugins\GestureSign.ClipboardMatch.Plugin.dll",
    "Plugins\GestureSign.ExtraPlugins.TextCopyer.dll",
    "Defaults\Actions.gsa",
    "Defaults\Gestures.gest",
    "Languages\ControlPanel\en.xml",
    "Languages\Daemon\en.xml",
    "THIRD-PARTY-NOTICES.txt"
)
foreach ($relativePath in $requiredFiles) {
    $path = Join-Path $output $relativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required publish artifact is missing: $relativePath"
    }
}

if ($resolvedPackagePath) {
    $manifestPath = Join-Path $output "release-manifest.json"
    $releaseFiles = Get-ChildItem -LiteralPath $output -File -Recurse |
        Where-Object { $_.FullName -ne $manifestPath } |
        Sort-Object FullName |
        ForEach-Object {
            $relativePath = [IO.Path]::GetRelativePath($output, $_.FullName).Replace('\', '/')
            $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            [ordered]@{
                path = $relativePath
                sha256 = $hash
                size = $_.Length
            }
        }
    $manifest = [ordered]@{
        version = $Version
        repository = $Repository
        runtime = $Runtime
        files = @($releaseFiles)
    }
    $utf8WithoutBom = [Text.UTF8Encoding]::new($false)
    [IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 5), $utf8WithoutBom)

    $packageDirectory = Split-Path -Parent $resolvedPackagePath
    [void](New-Item -ItemType Directory -Path $packageDirectory -Force)
    if (Test-Path -LiteralPath $resolvedPackagePath) {
        Remove-Item -LiteralPath $resolvedPackagePath -Force
    }
    [IO.Compression.ZipFile]::CreateFromDirectory($output, $resolvedPackagePath,
        [IO.Compression.CompressionLevel]::Optimal, $false)

    $packageHash = (Get-FileHash -LiteralPath $resolvedPackagePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $checksumPath = $resolvedPackagePath + ".sha256"
    [IO.File]::WriteAllText($checksumPath,
        "$packageHash  $([IO.Path]::GetFileName($resolvedPackagePath))`n", $utf8WithoutBom)
    Write-Host "Created release package $resolvedPackagePath"
}

Write-Host "Published self-contained $Runtime artifacts to $output"
if (-not $SkipLatest) {
    & (Join-Path $root "scripts\Update-LatestBuild.ps1") -SourceDirectory $output
}
Get-Item -LiteralPath (Join-Path $output "TouchPilot.exe"), (Join-Path $output "TouchPilot.ControlPanel.exe") |
    Select-Object Name, Length
