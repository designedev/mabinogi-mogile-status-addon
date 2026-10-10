# 개발·검증·배포

## 빌드

.NET 10 SDK와 NuGet 연결이 필요합니다. Windows 11 24H2 이상을 대상으로 하며 Deskband11Lib.Wpf/Core 1.4.2를 사용합니다.

```powershell
dotnet build mStatusBar.sln -c Release
```

## 검증

```powershell
dotnet run --project tests/mStatusBar.Checks -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/smoke.ps1
```

회귀 검증은 격리된 모의 CLI를 사용합니다. UI 검증은 실제 작업 표시줄에 모의 애드온을 부착해 위치·클릭·팝업·음악·날씨·HP·사망 경고를 확인하며 결과를 `artifacts/smoke/output`에 저장하고 종료합니다. 실제 게임 행동과 사용자 설정, Windows 자동 실행 등록은 변경하지 않습니다.

개발용 설정은 `MSTATUSBAR_SETTINGS_DIR` 환경 변수로 별도 디렉터리를 지정할 수 있습니다. 행동 명령의 검증에는 모의 CLI를 사용하고, 게임에서 승인이 필요한 수령은 확인 절차를 유지합니다.

## 배포

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/publish.ps1
```

Windows x64 자체 포함 실행 파일과 ZIP·SHA256 파일을 `dist`에 생성합니다. ZIP에는 실행 파일, README, 제삼자 고지와 라이선스만 포함합니다. 게임 CLI, 사용자 설정, 개발 데이터, 검증 결과는 포함하지 않습니다. 실행 파일은 서명하지 않습니다.

아이콘은 다음 명령으로 다시 생성할 수 있습니다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/create-icon.ps1
```

## 구성

- `src/mStatusBar.Core`: 설정, CLI 연결, 상태 조회·파싱, 제작 수령·악보 연주, 배치 계산.
- `src/mStatusBar`: WPF 작업 표시줄·설정·작업 목록·악보 선택 화면, 팝업과 트레이.
- `tests/mStatusBar.Checks`: 모의 CLI와 회귀 검증.
- `scripts`: 아이콘 생성, UI 검증, 자체 포함 배포.

CLI 응답은 UTF-8 표준 출력에서 읽습니다. 한글 행동 인수는 UTF-8 base64로 전달하며 `last-response.json`은 데이터 소스로 사용하지 않습니다. 명령 스키마를 캐시하고 미지원 명령 응답 시 한 번 갱신·재시도합니다. 조회는 순차 실행하며 이전 배치가 끝난 뒤 설정된 간격을 기다립니다. 행동 명령은 최종 응답까지 기다리고 임의로 강제 종료하지 않습니다.

제삼자 라이선스는 [고지문](../THIRD_PARTY_NOTICES.md)과 `licenses`를 참고하세요.
