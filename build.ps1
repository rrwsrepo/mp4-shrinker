param([Parameter(Mandatory=$true)][string]$FfmpegDirectory)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
foreach ($tool in @('ffmpeg.exe','ffprobe.exe')) {
    if (!(Test-Path -LiteralPath (Join-Path $FfmpegDirectory $tool) -PathType Leaf)) {
        throw "Missing $tool in the supplied FFmpeg folder."
    }
}
dotnet build MP4Shrinker.sln -c Release
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
dotnet run --project tests/MP4Shrinker.Tests -c Release
if ($LASTEXITCODE -ne 0) { throw 'Tests failed. Symbolic-link tests require Windows Developer Mode or link-creation permission.' }
$destination = Join-Path $PSScriptRoot 'artifacts/MP4-Shrinker-v1.0-win-x64'
dotnet publish src/MP4Shrinker.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $destination
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
foreach ($tool in @('ffmpeg.exe','ffprobe.exe')) { Copy-Item -LiteralPath (Join-Path $FfmpegDirectory $tool) -Destination $destination -Force }
foreach ($document in @('README.txt','THIRD-PARTY-NOTICES.txt','TEST-RESULTS.txt')) { Copy-Item -LiteralPath $document -Destination $destination -Force }
Copy-Item -LiteralPath 'third-party' -Destination $destination -Recurse -Force
Get-ChildItem -LiteralPath $destination -Filter '*.pdb' | Remove-Item
Compress-Archive -LiteralPath $destination -DestinationPath (Join-Path $PSScriptRoot 'artifacts/MP4-Shrinker-v1.0-win-x64.zip') -Force
Write-Host "Portable ZIP created in artifacts. Test it on Windows before redistribution."
