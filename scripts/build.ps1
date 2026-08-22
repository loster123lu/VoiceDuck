param(
    [string]$OutputDirectory = ""
)

$ErrorActionPreference = 'Stop'
$scriptsDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptsDirectory
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    throw "C# compiler was not found at $compiler"
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $projectRoot 'artifacts'
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

$sourceDirectory = Join-Path $projectRoot 'src\VoiceDuck'
$sources = Get-ChildItem -LiteralPath $sourceDirectory -Filter '*.cs' | Sort-Object Name
$applicationPath = Join-Path $OutputDirectory 'VoiceDuck.exe'
$manifestPath = Join-Path $sourceDirectory 'app.manifest'

$compilerArguments = @(
    '/nologo',
    '/target:winexe',
    '/platform:anycpu',
    '/optimize+',
    '/debug:pdbonly',
    '/codepage:65001',
    "/win32manifest:$manifestPath",
    "/out:$applicationPath",
    '/reference:System.dll',
    '/reference:System.Core.dll',
    '/reference:System.Drawing.dll',
    '/reference:System.Windows.Forms.dll',
    '/reference:System.Runtime.Serialization.dll'
)
$compilerArguments += $sources.FullName
& $compiler $compilerArguments
if ($LASTEXITCODE -ne 0) { throw "VoiceDuck build failed with exit code $LASTEXITCODE" }

$testPath = Join-Path $OutputDirectory 'VoiceDuck.CoreTests.exe'
$testSources = @(
    (Join-Path $sourceDirectory 'Models.cs'),
    (Join-Path $sourceDirectory 'VoiceGate.cs'),
    (Join-Path $sourceDirectory 'DuckingCoordinator.cs'),
    (Join-Path $projectRoot 'tests\CoreTests\CoreTests.cs')
)
$testArguments = @(
    '/nologo',
    '/target:exe',
    '/platform:anycpu',
    '/optimize+',
    '/codepage:65001',
    "/out:$testPath",
    '/reference:System.dll',
    '/reference:System.Core.dll',
    '/reference:System.Runtime.Serialization.dll'
)
$testArguments += $testSources
& $compiler $testArguments
if ($LASTEXITCODE -ne 0) { throw "VoiceDuck test build failed with exit code $LASTEXITCODE" }

& $testPath
if ($LASTEXITCODE -ne 0) { throw "VoiceDuck core tests failed with exit code $LASTEXITCODE" }

$smokeTestPath = Join-Path $OutputDirectory 'VoiceDuck.AudioSmokeTest.exe'
$smokeSources = @(
    (Join-Path $sourceDirectory 'Models.cs'),
    (Join-Path $sourceDirectory 'VoiceGate.cs'),
    (Join-Path $sourceDirectory 'DuckingCoordinator.cs'),
    (Join-Path $sourceDirectory 'CoreAudio.cs'),
    (Join-Path $projectRoot 'tests\AudioSmokeTest\AudioSmokeTest.cs')
)
$smokeArguments = @(
    '/nologo',
    '/target:exe',
    '/platform:anycpu',
    '/optimize+',
    '/codepage:65001',
    "/out:$smokeTestPath",
    '/reference:System.dll',
    '/reference:System.Core.dll',
    '/reference:System.Runtime.Serialization.dll'
)
$smokeArguments += $smokeSources
& $compiler $smokeArguments
if ($LASTEXITCODE -ne 0) { throw "VoiceDuck audio smoke test build failed with exit code $LASTEXITCODE" }

& $smokeTestPath
if ($LASTEXITCODE -ne 0) { throw "VoiceDuck audio smoke test failed with exit code $LASTEXITCODE" }

$uiTestPath = Join-Path $OutputDirectory 'VoiceDuck.UiRenderTest.exe'
$uiSources = Get-ChildItem -LiteralPath $sourceDirectory -Filter '*.cs' |
    Where-Object { $_.Name -ne 'Program.cs' } |
    Sort-Object Name
$uiArguments = @(
    '/nologo',
    '/target:exe',
    '/platform:anycpu',
    '/optimize+',
    '/codepage:65001',
    "/out:$uiTestPath",
    '/reference:System.dll',
    '/reference:System.Core.dll',
    '/reference:System.Drawing.dll',
    '/reference:System.Windows.Forms.dll',
    '/reference:System.Runtime.Serialization.dll'
)
$uiArguments += $uiSources.FullName
$uiArguments += (Join-Path $projectRoot 'tests\UiRenderTest\UiRenderTest.cs')
& $compiler $uiArguments
if ($LASTEXITCODE -ne 0) { throw "VoiceDuck UI render test build failed with exit code $LASTEXITCODE" }

Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination (Join-Path $OutputDirectory 'README.md') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination (Join-Path $OutputDirectory 'LICENSE.txt') -Force
Write-Output "BUILT=$applicationPath"
