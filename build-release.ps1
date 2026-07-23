[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$')]
    [string]$Version,

    [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')]
    [string]$Repository = "Autumn-one/TouchPilot",

    [string]$OutputDirectory,

    [DateTimeOffset]$BuiltAtUtc = [DateTimeOffset]::UtcNow,

    [string]$InnoCompilerPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"

$root = [IO.Path]::GetFullPath($PSScriptRoot)
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $root "artifacts"))
$output = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    [IO.Path]::GetFullPath((Join-Path $artifactsRoot "release"))
} elseif ([IO.Path]::IsPathRooted($OutputDirectory)) {
    [IO.Path]::GetFullPath($OutputDirectory)
} else {
    [IO.Path]::GetFullPath((Join-Path $root $OutputDirectory))
}

$relativeOutput = [IO.Path]::GetRelativePath($artifactsRoot, $output)
if ($relativeOutput -eq "." -or [IO.Path]::IsPathRooted($relativeOutput) -or
    $relativeOutput.StartsWith("..", [StringComparison]::Ordinal)) {
    throw "The release output must remain inside the repository artifacts directory."
}

$builtAt = $BuiltAtUtc.ToUniversalTime()
$expiresAt = $builtAt.AddMonths(3)
$workDirectory = Join-Path $output "work"
$installerDirectory = Join-Path $workDirectory "installer"
$portableDirectory = Join-Path $workDirectory "portable"
$null = $Version -match '^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)'
$numericVersion = "$($Matches.major).$($Matches.minor).$($Matches.patch).0"
$installerAssetName = "TouchPilot-$Version-win-x64-setup.exe"
$portableAssetName = "TouchPilot-$Version-win-x64-portable.zip"
$installerAssetPath = Join-Path $output $installerAssetName
$portableAssetPath = Join-Path $output $portableAssetName

function Remove-SafeDirectory {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }
    $item = Get-Item -LiteralPath $Path -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Refusing to clean a release output that is a reparse point: $Path"
    }
    Remove-Item -LiteralPath $Path -Recurse -Force
}

function Invoke-Publish {
    param(
        [Parameter(Mandatory)][string]$Configuration,
        [Parameter(Mandatory)][string]$Destination
    )

    & (Join-Path $root "publish.ps1") -Runtime win-x64 -Configuration $Configuration `
        -OutputDirectory $Destination -BuiltAtUtc $builtAt
    if ($LASTEXITCODE -ne 0) {
        throw "Publishing the $Configuration distribution failed with exit code $LASTEXITCODE."
    }
}

function Assert-ReleaseContents {
    param([Parameter(Mandatory)][string]$Directory)

    $requiredFiles = @(
        "GestureSign.exe",
        "GestureSign.ControlPanel.exe",
        "GestureSign.Updater.exe",
        "GestureSign.CorePlugins.dll",
        "Defaults\Actions.gsa",
        "Defaults\Gestures.gest",
        "Languages\ControlPanel\en.xml",
        "Languages\Daemon\en.xml"
    )
    foreach ($relativePath in $requiredFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $Directory $relativePath) -PathType Leaf)) {
            throw "Required release file is missing: $relativePath"
        }
    }

    $forbiddenExtensions = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($extension in @(".pdb", ".cs", ".csproj", ".sln", ".ps1", ".iss", ".user")) {
        [void]$forbiddenExtensions.Add($extension)
    }
    $forbiddenFiles = @(Get-ChildItem -LiteralPath $Directory -File -Recurse | Where-Object {
        $forbiddenExtensions.Contains($_.Extension) -or
        $_.Name -like "*.config.user" -or
        $_.Name -like "*GestureSign.Tests*" -or
        $_.Name -like "*ReleaseManager*"
    })
    if ($forbiddenFiles.Count -ne 0) {
        throw "Forbidden files were found in the release: $($forbiddenFiles.FullName -join ', ')"
    }
}

function Write-ReleaseManifest {
    param(
        [Parameter(Mandatory)][string]$Directory,
        [Parameter(Mandatory)][ValidateSet("installer", "portable")][string]$Distribution
    )

    $manifestPath = Join-Path $Directory "release-manifest.json"
    $files = Get-ChildItem -LiteralPath $Directory -File -Recurse |
        Where-Object { $_.FullName -ne $manifestPath } |
        Sort-Object FullName |
        ForEach-Object {
            [ordered]@{
                path = [IO.Path]::GetRelativePath($Directory, $_.FullName).Replace('\', '/')
                sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
                size = $_.Length
            }
        }
    $manifest = [ordered]@{
        version = $Version
        repository = $Repository
        runtime = "win-x64"
        distribution = $Distribution
        builtAtUtc = $builtAt.ToString("O", [Globalization.CultureInfo]::InvariantCulture)
        expiresAtUtc = $expiresAt.ToString("O", [Globalization.CultureInfo]::InvariantCulture)
        files = @($files)
    }
    $utf8WithoutBom = [Text.UTF8Encoding]::new($false)
    [IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 5), $utf8WithoutBom)
}

function Find-InnoCompiler {
    if (-not [string]::IsNullOrWhiteSpace($InnoCompilerPath)) {
        $candidate = [IO.Path]::GetFullPath($InnoCompilerPath)
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            throw "Inno Setup compiler was not found: $candidate"
        }
        return $candidate
    }

    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }
    foreach ($candidate in @(
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
        (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe"),
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe")
    )) {
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and
            (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            return $candidate
        }
    }
    throw "Inno Setup 6 compiler was not found. Install JRSoftware.InnoSetup with winget."
}

Remove-SafeDirectory -Path $output
[void](New-Item -ItemType Directory -Path $installerDirectory -Force)
[void](New-Item -ItemType Directory -Path $portableDirectory -Force)

Write-Host "Publishing installer distribution..."
Invoke-Publish -Configuration Release -Destination $installerDirectory
Assert-ReleaseContents -Directory $installerDirectory
Write-ReleaseManifest -Directory $installerDirectory -Distribution installer

Write-Host "Publishing portable distribution..."
Invoke-Publish -Configuration Portable -Destination $portableDirectory
Assert-ReleaseContents -Directory $portableDirectory
Write-ReleaseManifest -Directory $portableDirectory -Distribution portable

Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($portableDirectory, $portableAssetPath,
    [IO.Compression.CompressionLevel]::Optimal, $false)

$iscc = Find-InnoCompiler
$setupIconPath = Join-Path $root "GestureSign.Daemon\Resources\normal.ico"
$outputBaseFilename = [IO.Path]::GetFileNameWithoutExtension($installerAssetName)
& $iscc "/Qp" "/DAppVersion=$Version" "/DNumericVersion=$numericVersion" `
    "/DSourceDir=$installerDirectory" `
    "/DOutputDir=$output" "/DOutputBaseFilename=$outputBaseFilename" `
    "/DSetupIconPath=$setupIconPath" (Join-Path $root "installer\TouchPilot.iss")
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed with exit code $LASTEXITCODE."
}

foreach ($assetPath in @($installerAssetPath, $portableAssetPath)) {
    if (-not (Test-Path -LiteralPath $assetPath -PathType Leaf)) {
        throw "Release asset was not created: $assetPath"
    }
    $asset = Get-Item -LiteralPath $assetPath
    if ($asset.Length -le 0) {
        throw "Release asset is empty: $assetPath"
    }
}

Remove-SafeDirectory -Path $workDirectory
Write-Host "Created TouchPilot $Version release assets:"
Get-Item -LiteralPath $installerAssetPath, $portableAssetPath | ForEach-Object {
    [pscustomobject]@{
        Name = $_.Name
        Size = $_.Length
        Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
} | Format-Table -AutoSize
