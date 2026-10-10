param([string]$ApplicationPath = '')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$smokeRoot = Join-Path $projectRoot 'artifacts\smoke'
$settingsDirectory = Join-Path $smokeRoot 'settings'
$outputDirectory = Join-Path $smokeRoot 'output'
dotnet build (Join-Path $projectRoot 'mStatusBar.sln') -c Release
if ($LASTEXITCODE -ne 0) { throw '빌드 실패' }
New-Item -ItemType Directory -Force -Path $settingsDirectory | Out-Null
@{ CliPath = (Join-Path $projectRoot 'tests\mStatusBar.Checks\bin\Release\net10.0\mStatusBar.Checks.exe'); PollIntervalSeconds = 2 } |
    ConvertTo-Json | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $settingsDirectory 'settings.json')
$priorDirectory = $env:MSTATUSBAR_SETTINGS_DIR
$priorTitle = $env:MSTATUSBAR_MOCK_LONG_TITLE
$priorScores = $env:MSTATUSBAR_MOCK_MORE_SCORES
$env:MSTATUSBAR_MOCK_MORE_SCORES = '1'
$env:MSTATUSBAR_MOCK_LONG_TITLE = '달빛 소나타 · 별빛 아래 이어지는 아주 긴 음악 제목 · 다시 만나는 우리의 멜로디'
$env:MSTATUSBAR_SETTINGS_DIR = $settingsDirectory
try {
    $appPath = if ($ApplicationPath) { $ApplicationPath } else { Join-Path $projectRoot 'src\mStatusBar\bin\Release\net10.0-windows10.0.26100.0\mStatusBar.exe' }
    $startedAt = Get-Date
    $smokeProcess = Start-Process -FilePath $appPath -ArgumentList @('--smoke-output', ('"' + $outputDirectory + '"')) -PassThru -WindowStyle Hidden
    if (!$smokeProcess.WaitForExit(45000)) { throw "검증 앱이 종료되지 않았습니다. PID: $($smokeProcess.Id)" }
    if ($smokeProcess.ExitCode -ne 0) { throw "검증 앱 오류: $($smokeProcess.ExitCode)" }
    $diagnosticsPath = Join-Path $outputDirectory 'diagnostics.json'
    if (!(Test-Path -LiteralPath $diagnosticsPath)) { throw 'UI 진단 결과가 없습니다.' }
    if ((Get-Item -LiteralPath $diagnosticsPath).LastWriteTime -lt $startedAt) { throw '이번 실행의 진단 결과가 생성되지 않았습니다.' }
    $result = Get-Content -Raw -LiteralPath $diagnosticsPath | ConvertFrom-Json
    if ($result.blankClicksAtOpacity.PSObject.Properties.Value -contains $false) { throw "투명도별 빈 영역 클릭 실패: $($result.blankClicksAtOpacity | ConvertTo-Json -Compress)" }
    if (!$result.weatherHealthDisplayed -or !$result.weatherIconsSupported -or !$result.healthRatioApplied -or !$result.healthAlertFlashes -or !$result.healthRevived -or !$result.statusFieldsCleared -or !$result.queueUnified -or !$result.queueClickOpens) { throw '날씨/에린 시간/HP/사망 깜빡임/통합 제작 버튼 검증 실패' }
    if (!$result.centerPlacementApplied -or !$result.contextSettingsAccessible -or !$result.settingsSpaceReclaimed) { throw '가운데 배치/우클릭 설정/공간 확보 검증 실패' }
    if (!$result.timeSettingApplied -or !$result.regionClicksOpen -or !$result.outsideHookInstalled -or !$result.singleOutsideClickCloses) { throw '재생 시간/영역 클릭/한 번의 외부 클릭 닫기 검증 실패' }
    if (!$result.nearbyDisplayed -or !$result.ownMusicPrioritized -or !$result.nearbyCleared) { throw '주변 연주 표시/내 연주 우선/표시 해제 검증 실패' }
    if (!$result.appearanceApplied -or !$result.effectsAnimate -or !$result.effectsStop) { throw '재생 효과 또는 배경색/투명도 적용 검증 실패' }
    if (!$result.flyoutsPreparedHidden -or !$result.repeatedPresentationStable -or !$result.placementApplied) { throw '팝업 반복 표시 또는 위치 설정 적용 검증 실패' }
    if (!$result.attached -or $result.firstParentClass -notin @('Shell_TrayWnd', 'Shell_SecondaryTrayWnd') -or $result.parentClass -notin @('Shell_TrayWnd', 'Shell_SecondaryTrayWnd') -or !$result.connected -or $result.errors.Count -ne 0 -or $result.interval -ne 2 -or $result.placement -ne 'BeforeNotificationArea' -or $result.musicTitles.Count -lt 1 -or !$result.musicFlowing -or !$result.detailTitleFlowing -or !$result.onRight -or !$result.connectionTestSucceeded -or !$result.connectionFailureShown -or !$result.singleFlyout -or !$result.outsideDismissed -or !$result.escapeDismissed -or !$result.toggleDismissed -or !$result.modalHoldProtected -or !$result.flyoutsFit -or !$result.noFlyoutTaskbarButtons -or !$result.musicScrollbarVisible -or !$result.musicScrollWorks -or $result.flowOffsetBefore -eq $result.flowOffsetAfter) {
        throw "작업 표시줄/모의 CLI 검증 실패: $(Get-Content -Raw -LiteralPath $diagnosticsPath)"
    }
    $result
} finally { $env:MSTATUSBAR_SETTINGS_DIR = $priorDirectory; $env:MSTATUSBAR_MOCK_LONG_TITLE = $priorTitle; $env:MSTATUSBAR_MOCK_MORE_SCORES = $priorScores }


