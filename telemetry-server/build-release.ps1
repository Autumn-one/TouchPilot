[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = "1.0.0"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$outputRoot = Join-Path $root "distribution\telemetry-server"
$previousGoOs = $env:GOOS
$previousGoArch = $env:GOARCH
$previousCgoEnabled = $env:CGO_ENABLED

Push-Location $PSScriptRoot
try {
    $env:GOOS = "linux"
    $env:CGO_ENABLED = "0"
    foreach ($architecture in @("amd64", "arm64")) {
        $env:GOARCH = $architecture
        $outputDirectory = Join-Path $outputRoot "linux-$architecture"
        [void](New-Item -ItemType Directory -Path $outputDirectory -Force)
        $outputPath = Join-Path $outputDirectory "touchpilot-telemetry"
        & go build -trimpath -ldflags "-s -w -X main.version=$Version" -o $outputPath .
        if ($LASTEXITCODE -ne 0) {
            throw "Go build failed for linux-$architecture with exit code $LASTEXITCODE."
        }
    }
} finally {
    Pop-Location
    $env:GOOS = $previousGoOs
    $env:GOARCH = $previousGoArch
    $env:CGO_ENABLED = $previousCgoEnabled
}

$checksumLines = foreach ($architecture in @("amd64", "arm64")) {
    $path = Join-Path $outputRoot "linux-$architecture\touchpilot-telemetry"
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  linux-$architecture/touchpilot-telemetry"
}
[IO.File]::WriteAllLines((Join-Path $outputRoot "checksums.sha256"), $checksumLines,
    [Text.UTF8Encoding]::new($false))
