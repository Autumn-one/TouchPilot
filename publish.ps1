[CmdletBinding()]
param(
    [ValidateSet("win-x64", "win-x86", "win-arm64")]
    [string]$Runtime = "win-x64"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"

$root = [IO.Path]::GetFullPath($PSScriptRoot)
$output = [IO.Path]::GetFullPath((Join-Path $root "artifacts"))
$relativeOutput = [IO.Path]::GetRelativePath($root, $output)
if ([IO.Path]::IsPathRooted($relativeOutput) -or $relativeOutput.StartsWith("..", [StringComparison]::Ordinal)) {
    throw "The publish output must remain inside the repository root."
}

function Invoke-DotNet {
    param([Parameter(Mandatory)][string[]]$Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
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

Invoke-DotNet -Arguments @("build", (Join-Path $root "GestureSign.sln"), "-c", "Release", "--no-incremental")

$publishOptions = @(
    "-c", "Release",
    "-r", $Runtime,
    "--self-contained", "true",
    "--output", $output,
    "-p:DebugType=None",
    "-p:DebugSymbols=false"
)

Invoke-DotNet -Arguments (@("publish", (Join-Path $root "GestureSign.ControlPanel\GestureSign.ControlPanel.csproj")) + $publishOptions)
Invoke-DotNet -Arguments (@("publish", (Join-Path $root "GestureSign.Daemon\GestureSign.Daemon.csproj")) + $publishOptions)

$pluginOutput = Join-Path $root "GestureSign.ExtraPlugins"
$pluginSources = @(
    (Join-Path $pluginOutput "ClipboardMatch\bin\Release\net10.0-windows10.0.19041.0\GestureSign.ClipboardMatch.Plugin.dll"),
    (Join-Path $pluginOutput "TextCopyer\bin\Release\net10.0-windows10.0.19041.0\GestureSign.ExtraPlugins.TextCopyer.dll")
)
$pluginDirectory = Join-Path $output "Plugins"
[void](New-Item -ItemType Directory -Path $pluginDirectory)
foreach ($plugin in $pluginSources) {
    Copy-Item -LiteralPath $plugin -Destination $pluginDirectory -Force
}

$requiredFiles = @(
    "GestureSign.exe",
    "GestureSign.ControlPanel.exe",
    "GestureSign.CorePlugins.dll",
    "Plugins\GestureSign.ClipboardMatch.Plugin.dll",
    "Plugins\GestureSign.ExtraPlugins.TextCopyer.dll",
    "Defaults\Actions.gsa",
    "Defaults\Gestures.gest",
    "Languages\ControlPanel\en.xml",
    "Languages\Daemon\en.xml"
)
foreach ($relativePath in $requiredFiles) {
    $path = Join-Path $output $relativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required publish artifact is missing: $relativePath"
    }
}

Write-Host "Published self-contained $Runtime artifacts to $output"
Get-Item -LiteralPath (Join-Path $output "GestureSign.exe"), (Join-Path $output "GestureSign.ControlPanel.exe") |
    Select-Object Name, Length
