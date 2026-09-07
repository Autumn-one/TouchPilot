Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$testRoot = Join-Path $root ("artifacts\latest-build-tests-" + [Guid]::NewGuid().ToString("N"))
$fixture = Join-Path $testRoot "Project with spaces & brackets [test]"
$source = Join-Path $fixture "artifacts\publish"
$latest = Join-Path $fixture "artifacts\latest"
$helper = Join-Path $fixture "scripts\Update-LatestBuild.ps1"

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Assert-Fails {
    param([scriptblock]$Action, [string]$ExpectedMessage)
    try { & $Action } catch {
        Assert-True ($_.Exception.Message -like "*$ExpectedMessage*") $_.Exception.Message
        return
    }
    throw "Expected failure: $ExpectedMessage"
}

function Write-FixtureFile {
    param([string]$Path, [string]$Value)
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $Path))
    [IO.File]::WriteAllText($Path, $Value)
}

try {
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $helper))
    Copy-Item -LiteralPath (Join-Path $root "scripts\Update-LatestBuild.ps1") -Destination $helper
    foreach ($name in @("publish.ps1", "build-release.ps1", "Start-TouchPilot.cmd")) {
        Copy-Item -LiteralPath (Join-Path $root $name) -Destination $fixture
    }
    foreach ($name in @(
        "TouchPilot.exe", "TouchPilot.ControlPanel.exe", "TouchPilot.Updater.exe",
        "GestureSign.exe", "GestureSign.CorePlugins.dll", "TouchPilot.dll",
        "TouchPilot.deps.json", "TouchPilot.runtimeconfig.json",
        "TouchPilot.ControlPanel.dll", "TouchPilot.ControlPanel.deps.json",
        "TouchPilot.ControlPanel.runtimeconfig.json",
        "Plugins\GestureSign.ClipboardMatch.Plugin.dll",
        "Plugins\GestureSign.ExtraPlugins.TextCopyer.dll",
        "Defaults\Actions.gsa", "Defaults\Gestures.gest",
        "Languages\ControlPanel\en.xml", "Languages\Daemon\en.xml", "THIRD-PARTY-NOTICES.txt"
    )) {
        Write-FixtureFile (Join-Path $source $name) "first build"
    }
    & $helper -SourceDirectory $source
    Assert-True ([IO.File]::ReadAllText((Join-Path $latest "TouchPilot.exe")) -eq "first build") "Initial promotion failed."

    Write-FixtureFile (Join-Path $latest "AppData\TouchPilot.config") "user settings"
    Write-FixtureFile (Join-Path $latest "obsolete.dll") "stale"
    Write-FixtureFile (Join-Path $source "TouchPilot.exe") "second build"
    & $helper -SourceDirectory $source
    Assert-True ([IO.File]::ReadAllText((Join-Path $latest "TouchPilot.exe")) -eq "second build") "The stable path was not refreshed."
    Assert-True ([IO.File]::ReadAllText((Join-Path $latest "AppData\TouchPilot.config")) -eq "user settings") "Portable data was lost."
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $latest "obsolete.dll"))) "A stale binary survived replacement."

    Remove-Item -LiteralPath (Join-Path $source "TouchPilot.exe")
    Assert-Fails { & $helper -SourceDirectory $source } "incomplete"
    Write-FixtureFile (Join-Path $source "TouchPilot.exe") "third build"
    $lockedSource = [IO.File]::Open((Join-Path $source "TouchPilot.dll"),
        [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try {
        $failed = $false
        try { & $helper -SourceDirectory $source } catch { $failed = $true }
        Assert-True $failed "A locked source file should prevent promotion."
    } finally { $lockedSource.Dispose() }
    Assert-True ([IO.File]::ReadAllText((Join-Path $latest "TouchPilot.exe")) -eq "second build") "A failed copy replaced the latest build."

    $heldLock = [IO.File]::Open((Join-Path $fixture "artifacts\.latest.lock"),
        [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try {
        $failed = $false
        try { & $helper -SourceDirectory $source } catch { $failed = $true }
        Assert-True $failed "Concurrent promotion was not rejected."
    } finally { $heldLock.Dispose() }

    foreach ($output in @("artifacts", "artifacts\latest", "artifacts\latest\nested")) {
        Assert-Fails { & (Join-Path $fixture "publish.ps1") -OutputDirectory $output } "overlap"
    }
    Assert-Fails { & (Join-Path $fixture "build-release.ps1") -Version 0.0.1 -OutputDirectory "artifacts\latest" } "overlap"
    Assert-Fails { & (Join-Path $fixture "publish.ps1") -Version 0.0.1 -Repository Autumn-one/TouchPilot -PackagePath "artifacts\latest\package.zip" } "must not be written"
    Assert-Fails { & $helper -SourceDirectory (Join-Path $fixture "artifacts") } "overlap"

    $junction = Join-Path $latest "linked-data"
    [void](New-Item -ItemType Junction -Path $junction -Target (Join-Path $fixture "scripts"))
    try {
        Assert-Fails { & $helper -SourceDirectory $source } "reparse point"
    } finally {
        [IO.Directory]::Delete($junction)
    }

    # Exercise a compiler failure before any publish output exists.
    function dotnet { Set-Variable -Name LASTEXITCODE -Value 42 -Scope 1 }
    Assert-Fails { & (Join-Path $fixture "publish.ps1") } "exit code 42"
    Remove-Item Function:dotnet
    Assert-True ([IO.File]::ReadAllText((Join-Path $latest "TouchPilot.exe")) -eq "second build") "A compiler failure replaced the latest build."
    Assert-True ([IO.File]::ReadAllText((Join-Path $latest "AppData\TouchPilot.config")) -eq "user settings") "A failed build changed user data."

    Assert-True (@(Get-ChildItem -LiteralPath (Join-Path $fixture "artifacts") -Filter ".latest-staging-*").Count -eq 0) "A failed promotion left staging files."
    Write-Host "Latest build regression checks passed."
} finally {
    $relativeTestRoot = [IO.Path]::GetRelativePath((Join-Path $root "artifacts"), $testRoot)
    if (-not $relativeTestRoot.StartsWith("latest-build-tests-") -or $relativeTestRoot.Contains('\')) {
        throw "Refusing to remove an unexpected test directory: $testRoot"
    }
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
