param(
    [string]$Version = '1.0.0',
    [string]$OutputDirectory = ''
)

$ErrorActionPreference = 'Stop'
$scriptsDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptsDirectory
$artifactsRoot = Join-Path $projectRoot 'artifacts'

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $artifactsRoot 'release'
}

function Assert-ChildPath {
    param([string]$Candidate, [string]$Parent)
    $parentFull = [System.IO.Path]::GetFullPath($Parent).TrimEnd('\') + '\'
    $candidateFull = [System.IO.Path]::GetFullPath($Candidate)
    if (-not $candidateFull.StartsWith($parentFull, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean a path outside the artifacts directory: $candidateFull"
    }
}

$buildDirectory = Join-Path $artifactsRoot "build-$Version"
$portableName = "VoiceDuck-$Version-portable"
$stagingDirectory = Join-Path $OutputDirectory $portableName
$archivePath = Join-Path $OutputDirectory "$portableName.zip"

Assert-ChildPath -Candidate $buildDirectory -Parent $artifactsRoot
Assert-ChildPath -Candidate $stagingDirectory -Parent $artifactsRoot
Assert-ChildPath -Candidate $archivePath -Parent $artifactsRoot

foreach ($path in @($buildDirectory, $stagingDirectory, $archivePath)) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Recurse -Force
    }
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $stagingDirectory -Force | Out-Null

& (Join-Path $scriptsDirectory 'build.ps1') -OutputDirectory $buildDirectory
Copy-Item -LiteralPath (Join-Path $buildDirectory 'VoiceDuck.exe') -Destination $stagingDirectory
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $stagingDirectory
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination (Join-Path $stagingDirectory 'LICENSE.txt')

Compress-Archive -LiteralPath $stagingDirectory -DestinationPath $archivePath -CompressionLevel Optimal
Get-FileHash -LiteralPath $archivePath -Algorithm SHA256
Write-Output "RELEASE_ARCHIVE=$archivePath"
