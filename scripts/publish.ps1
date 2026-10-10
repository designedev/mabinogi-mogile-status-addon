param([ValidateSet('win-x64', 'win-arm64')][string]$Runtime = 'win-x64')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$publishDirectory = Join-Path $projectRoot "dist\mStatusBar-$Runtime"
dotnet publish (Join-Path $projectRoot 'src\mStatusBar\mStatusBar.csproj') -c Release -r $Runtime --self-contained true -o $publishDirectory -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
if ($LASTEXITCODE -ne 0) { throw 'Windows 앱 배포 빌드에 실패했습니다.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $publishDirectory
Copy-Item -LiteralPath (Join-Path $projectRoot 'THIRD_PARTY_NOTICES.md') -Destination $publishDirectory
Copy-Item -LiteralPath (Join-Path $projectRoot 'licenses') -Destination $publishDirectory -Recurse -Force
$zipPath = Join-Path $projectRoot "dist\mStatusBar-0.6.1-$Runtime.zip"
# Explicit allowlist: user settings, CLI, fixtures and build intermediates are never distributed.
Compress-Archive -LiteralPath @((Join-Path $publishDirectory 'mStatusBar.exe'), (Join-Path $publishDirectory 'README.md'), (Join-Path $publishDirectory 'THIRD_PARTY_NOTICES.md'), (Join-Path $publishDirectory 'licenses')) -DestinationPath $zipPath -Force
$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath "$zipPath.sha256" -Value "$hash  $(Split-Path -Leaf $zipPath)" -Encoding ascii
Write-Output $zipPath


