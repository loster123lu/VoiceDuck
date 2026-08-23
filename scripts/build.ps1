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
$dependencyDirectory = Join-Path $projectRoot 'third_party\NAudio\2.3.0'
$naudioCore = Join-Path $dependencyDirectory 'NAudio.Core.dll'
$naudioWasapi = Join-Path $dependencyDirectory 'NAudio.Wasapi.dll'
$netstandard = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\netstandard.dll'
$driverArchive = Join-Path $projectRoot 'third_party\VBCABLE\VBCABLE_Driver_Pack45.zip'
foreach ($required in @($naudioCore, $naudioWasapi, $netstandard, $driverArchive)) {
    if (-not (Test-Path -LiteralPath $required)) { throw "Required build input was not found: $required" }
}
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
    '/reference:System.Runtime.Serialization.dll',
    '/reference:System.ServiceProcess.dll',
    '/reference:System.IO.Compression.dll',
    '/reference:System.IO.Compression.FileSystem.dll',
    "/reference:$netstandard",
    "/reference:$naudioCore",
    "/reference:$naudioWasapi",
    "/resource:$driverArchive,VoiceDuck.Resources.VBCABLE_Driver_Pack45.zip"
)
$compilerArguments += $sources.FullName
& $compiler $compilerArguments
if ($LASTEXITCODE -ne 0) { throw "VoiceDuck build failed with exit code $LASTEXITCODE" }
Copy-Item -LiteralPath $naudioCore -Destination $OutputDirectory -Force
Copy-Item -LiteralPath $naudioWasapi -Destination $OutputDirectory -Force

$testPath = Join-Path $OutputDirectory 'VoiceDuck.CoreTests.exe'
$testSources = @(
    (Join-Path $sourceDirectory 'Models.cs'),
    (Join-Path $sourceDirectory 'MusicShareCore.cs'),
    (Join-Path $sourceDirectory 'AudioEndpoints.cs'),
    (Join-Path $sourceDirectory 'MusicShareAudioEngine.cs'),
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
    '/reference:System.Runtime.Serialization.dll',
    "/reference:$netstandard",
    "/reference:$naudioCore",
    "/reference:$naudioWasapi"
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
    (Join-Path $sourceDirectory 'MusicShareCore.cs'),
    (Join-Path $sourceDirectory 'CaptureDevices.cs'),
    (Join-Path $sourceDirectory 'AudioEndpoints.cs'),
    (Join-Path $sourceDirectory 'AudioRecovery.cs'),
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
    '/reference:System.Runtime.Serialization.dll',
    '/reference:System.ServiceProcess.dll',
    "/reference:$netstandard",
    "/reference:$naudioCore",
    "/reference:$naudioWasapi"
)
$smokeArguments += $smokeSources
& $compiler $smokeArguments
if ($LASTEXITCODE -ne 0) { throw "VoiceDuck audio smoke test build failed with exit code $LASTEXITCODE" }

& $smokeTestPath
if ($LASTEXITCODE -ne 0) { throw "VoiceDuck audio smoke test failed with exit code $LASTEXITCODE" }

$driverTestPath = Join-Path $OutputDirectory 'VoiceDuck.DriverPackageTest.exe'
$driverTestSources = @(
    (Join-Path $sourceDirectory 'Models.cs'),
    (Join-Path $sourceDirectory 'MusicShareCore.cs'),
    (Join-Path $sourceDirectory 'VoiceGate.cs'),
    (Join-Path $sourceDirectory 'DuckingCoordinator.cs'),
    (Join-Path $sourceDirectory 'CaptureDevices.cs'),
    (Join-Path $sourceDirectory 'CoreAudio.cs'),
    (Join-Path $sourceDirectory 'AudioEndpoints.cs'),
    (Join-Path $sourceDirectory 'AudioRecovery.cs'),
    (Join-Path $sourceDirectory 'VirtualAudioInstaller.cs'),
    (Join-Path $projectRoot 'tests\DriverPackageTest\DriverPackageTest.cs')
)
$driverTestArguments = @(
    '/nologo',
    '/target:exe',
    '/platform:anycpu',
    '/optimize+',
    '/codepage:65001',
    "/out:$driverTestPath",
    '/reference:System.dll',
    '/reference:System.Core.dll',
    '/reference:System.Runtime.Serialization.dll',
    '/reference:System.ServiceProcess.dll',
    '/reference:System.IO.Compression.dll',
    '/reference:System.IO.Compression.FileSystem.dll',
    "/reference:$netstandard",
    "/reference:$naudioCore",
    "/reference:$naudioWasapi",
    "/resource:$driverArchive,VoiceDuck.Resources.VBCABLE_Driver_Pack45.zip"
)
$driverTestArguments += $driverTestSources
& $compiler $driverTestArguments
if ($LASTEXITCODE -ne 0) { throw "VoiceDuck driver package test build failed with exit code $LASTEXITCODE" }

& $driverTestPath
if ($LASTEXITCODE -ne 0) { throw "VoiceDuck driver package verification failed with exit code $LASTEXITCODE" }

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
    '/reference:System.Runtime.Serialization.dll',
    '/reference:System.ServiceProcess.dll',
    '/reference:System.IO.Compression.dll',
    '/reference:System.IO.Compression.FileSystem.dll',
    "/reference:$netstandard",
    "/reference:$naudioCore",
    "/reference:$naudioWasapi",
    "/resource:$driverArchive,VoiceDuck.Resources.VBCABLE_Driver_Pack45.zip"
)
$uiArguments += $uiSources.FullName
$uiArguments += (Join-Path $projectRoot 'tests\UiRenderTest\UiRenderTest.cs')
& $compiler $uiArguments
if ($LASTEXITCODE -ne 0) { throw "VoiceDuck UI render test build failed with exit code $LASTEXITCODE" }

Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination (Join-Path $OutputDirectory 'README.md') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination (Join-Path $OutputDirectory 'LICENSE.txt') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'THIRD_PARTY_NOTICES.md') -Destination $OutputDirectory -Force
$licensesDirectory = Join-Path $OutputDirectory 'ThirdPartyLicenses'
New-Item -ItemType Directory -Path $licensesDirectory -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $dependencyDirectory 'LICENSE.txt') -Destination (Join-Path $licensesDirectory 'NAudio-LICENSE.txt') -Force
Copy-Item -LiteralPath (Join-Path $dependencyDirectory 'NOTICE.md') -Destination (Join-Path $licensesDirectory 'NAudio-NOTICE.md') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'third_party\VBCABLE\NOTICE.md') -Destination (Join-Path $licensesDirectory 'VBCABLE-NOTICE.md') -Force
Write-Output "BUILT=$applicationPath"
