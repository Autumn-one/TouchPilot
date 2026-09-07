[CmdletBinding()]
param([Parameter(Mandatory)][string]$SourceDirectory)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$artifacts = Join-Path $root "artifacts"
$latest = Join-Path $artifacts "latest"
$source = [IO.Path]::GetFullPath($SourceDirectory)
$relativeSource = [IO.Path]::GetRelativePath($root, $source)
if ([IO.Path]::IsPathRooted($relativeSource) -or $relativeSource.StartsWith("..") -or
    $relativeSource -eq ".") {
    throw "The latest build source must be a directory inside the repository."
}
$sourceFromLatest = [IO.Path]::GetRelativePath($latest, $source)
$latestFromSource = [IO.Path]::GetRelativePath($source, $latest)
if (-not $sourceFromLatest.StartsWith("..") -or -not $latestFromSource.StartsWith("..")) {
    throw "The latest build source must not overlap artifacts\latest."
}

function Assert-PlainDirectory {
    param([string]$Path)

    $directory = Get-Item -LiteralPath $Path -Force
    if (-not $directory.PSIsContainer) {
        throw "Expected a directory: $Path"
    }
    for ($ancestor = $directory; $null -ne $ancestor; $ancestor = $ancestor.Parent) {
        if (($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Refusing a build directory with a reparse point: $($ancestor.FullName)"
        }
        if ($ancestor.FullName -eq $root) { break }
    }
    foreach ($item in Get-ChildItem -LiteralPath $Path -Recurse -Force) {
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Refusing a build containing a reparse point: $($item.FullName)"
        }
    }
}

Assert-PlainDirectory -Path $source
$requiredFiles = @(
    "TouchPilot.exe", "TouchPilot.ControlPanel.exe", "TouchPilot.Updater.exe",
    "GestureSign.exe", "GestureSign.CorePlugins.dll",
    "TouchPilot.dll", "TouchPilot.deps.json", "TouchPilot.runtimeconfig.json",
    "TouchPilot.ControlPanel.dll", "TouchPilot.ControlPanel.deps.json",
    "TouchPilot.ControlPanel.runtimeconfig.json",
    "Plugins\GestureSign.ClipboardMatch.Plugin.dll",
    "Plugins\GestureSign.ExtraPlugins.TextCopyer.dll",
    "Defaults\Actions.gsa", "Defaults\Gestures.gest",
    "Languages\ControlPanel\en.xml", "Languages\Daemon\en.xml",
    "THIRD-PARTY-NOTICES.txt"
)
foreach ($relativePath in $requiredFiles) {
    $file = Get-Item -LiteralPath (Join-Path $source $relativePath) -ErrorAction SilentlyContinue
    if ($null -eq $file -or $file.PSIsContainer -or $file.Length -eq 0) {
        throw "The latest build is incomplete: $relativePath"
    }
}

if (Test-Path -LiteralPath $artifacts) {
    $item = Get-Item -LiteralPath $artifacts -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Refusing an artifacts directory that is a reparse point."
    }
}
[void](New-Item -ItemType Directory -Path $artifacts -Force)
$buildLock = [IO.File]::Open((Join-Path $artifacts ".latest.lock"),
    [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
$staging = Join-Path $artifacts (".latest-staging-" + [Guid]::NewGuid().ToString("N"))
$backup = Join-Path $artifacts (".latest-previous-" + [Guid]::NewGuid().ToString("N"))
try {
    if (Test-Path -LiteralPath $latest) {
        Assert-PlainDirectory -Path $latest
    }
    $latestPrefix = $latest + [IO.Path]::DirectorySeparatorChar
    foreach ($process in Get-Process -Name TouchPilot, TouchPilot.ControlPanel, TouchPilot.Updater,
        GestureSign, GestureSign.ControlPanel, GestureSign.Updater -ErrorAction SilentlyContinue) {
        if ([string]::IsNullOrWhiteSpace($process.Path) -or
            $process.Path.StartsWith($latestPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Exit TouchPilot and its control panel before replacing artifacts\latest (PID $($process.Id))."
        }
    }

    [void](New-Item -ItemType Directory -Path $staging)
    Get-ChildItem -LiteralPath $source -Force |
        Copy-Item -Destination $staging -Recurse -Force
    $userData = Join-Path $latest "AppData"
    if (Test-Path -LiteralPath $userData -PathType Container) {
        Copy-Item -LiteralPath $userData -Destination $staging -Recurse -Force
    }

    # Keep the previous complete tree until the new tree has taken its stable name.
    if (Test-Path -LiteralPath $latest) {
        [IO.Directory]::Move($latest, $backup)
    }
    try {
        [IO.Directory]::Move($staging, $latest)
    } catch {
        if (Test-Path -LiteralPath $backup) {
            [IO.Directory]::Move($backup, $latest)
        }
        throw
    }
    if (Test-Path -LiteralPath $backup) {
        try {
            Remove-Item -LiteralPath $backup -Recurse -Force
        } catch {
            Write-Warning "The latest build is ready, but the previous build could not be removed: $backup"
        }
    }
} finally {
    $buildLock.Dispose()
    if (Test-Path -LiteralPath $staging) {
        Remove-Item -LiteralPath $staging -Recurse -Force
    }
}

Write-Host "Latest runnable build: $latest"
Write-Host "Launch: $(Join-Path $root 'Start-TouchPilot.cmd')"
