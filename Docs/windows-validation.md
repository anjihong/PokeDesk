# Windows 배포 및 공통 UI 검증

Windows와 Mac은 공통 Avalonia 소스를 사용한다. `feat/11-macos-avalonia`에서 이어진
Windows 호환성 개선, main 업데이트와 macOS 입력 권한 복구를 `window_dev` 브랜치에 반영한다.
기존 UI 커밋의 CI 결과와 후속 변경의 로컬 검증을 구분하며, 후속 변경의 원격 CI는 아직 실행하지 않았다.

## 결과

| 항목 | 상태 및 증거 |
| --- | --- |
| CI 검증 대상 커밋 | `64a35b9` — 참고 UI와 픽셀 경계 변경 |
| GitHub Actions 실행 | [37596301271 — 성공](https://github.com/anjihong/PokeDesk/actions/runs/37596301271) |
| Windows Release 빌드·배포 | 통과 — `windows-2022` / `win-x64`, 자체 포함 배포 아티팩트 생성 |
| Windows 전체 테스트 | 278/278 통과, 실패·건너뜀 0. Mac ARM64·Intel도 각각 278/278 통과 |
| Windows/Mac 공통 UI 비교 | 30개 화면·OS 간 90쌍 모두 통과. 레이아웃 90쌍 모두 정확히 일치 |
| 네이티브 EXE 시작·종료 검사 | 새 검사 스크립트를 로컬에 추가함. Windows runner 실행은 아직 하지 않음 |
| Windows 물리 데스크톱 전체 플레이 | 미확인 — CI 결과와 구분 |

CI 보고서는 `artifacts/windows-validation/base64/`에 내려받았다. 가장 큰 비-AA 차이는
Windows ↔ Mac ARM64의 `settings-1x`에서 0.7787%, 잔여 채널 MAE는 0.4904로 허용 기준 안이다.
이는 모든 픽셀이 같다는 뜻이 아니다. 이 CI 결과에는 이후의 모니터 변경 대응과 main `37ef2f9`의
새 진화 연출 이식과 macOS 권한 복구 수정이 포함되지 않는다.

## 후속 로컬 검증 (2026-10-07)

- main `37ef2f9`의 진화 연출·선택 취소를 공통 Avalonia UI에 이식했다. 기존 전체 세대 도감은 유지한다.
- 모니터·작업 영역·DPI 변경 후 화면 재배치, 입력 권한 복구와 관련 명세를 함께 수정했다.
- 전체 회귀 테스트 **Release 315/315, Debug 321/321** 통과. 로그와 TRX는 `artifacts/windows-validation/`에 기록했다.
- macOS 실제 CoreGraphics/IOKit API에서 입력 연결 **Active → Disposed**를 확인했다. 입력을 합성하거나 플레이어 저장을 열지 않는 별도 테스트 호스트에서 수행했다. 이 결과를 설치된 `.app`의 TCC 허용, 실제 설정 토글·거부·재등록 확인으로 대체하지 않는다.
- macOS 권한 회귀는 가짜 backend를 사용해 자동 재확인, CG/HID 조회 불일치, 키보드/마우스 부분 실패, timeout·권한 회수·종료를 검증한다. OS 권한 설정은 변경하지 않았다.
- 최종 Windows 자체 포함 배포 빌드와 PowerShell 7.6.6 검사를 통과했다. 필수 파일 19개·AMD64 PE32+ 6개·내장 .NET 10.0.12를 확인했다. 결과는 `artifacts/windows-validation/final-powershell-report.json`, 배포 폴더는 `artifacts/windows-validation/permission-fix/win-x64`다. Windows 자체 실행 검사는 이 Mac 환경에서 수행하지 않았다.
- 최종 Mac ARM64 자체 포함 배포 빌드·plist 검사·ad-hoc 서명 검증·ZIP 생성도 통과했다. 앱은 `artifacts/windows-validation/permission-fix/mac-package/DeskPokemon.app`에 있다. 실제 인증서 서명 옵션과 TCC 설정 변경은 실행하지 않았다.
- 네이티브 플레이 도우미 Release 빌드도 경고·오류 없이 통과했다. 기존 플레이어 앱이 실행 중이므로 이번 검증에서는 전역 키·마우스 이벤트를 합성하는 전체 플레이를 다시 실행하지 않았다.

## 자동 검증 범위

[`desktop.yml`](../.github/workflows/desktop.yml)은 같은 소스를 Windows x64, Mac ARM64, Mac x64에서
Release로 빌드하고 테스트한 뒤 각 실행 파일을 게시한다. 테스트는 공통 게임 규칙·저장·이미지 처리·UI 조작을
검증하며, UI 렌더는 headless Skia로 생성한다. 30개 화면의 위치·크기·텍스트·폰트·기록된 색상은 OS 간 정확히
일치해야 한다. 글자 래스터화 차이의 한도와 원본 오차 기록은 [공통 QA](cross-platform-qa.md)를 따른다.

[`validate-windows-publish.ps1`](../scripts/validate-windows-publish.ps1)은 다음을 확인한다.

- EXE, .NET 런타임, Skia·HarfBuzz 네이티브 DLL이 비어 있지 않은 AMD64 PE32+ 파일인지 검사한다.
- 앱·Avalonia·텍스트 렌더링의 필수 관리 DLL, `runtimeconfig.json`, `deps.json`의 존재를 검사한다.
- `net10.0` / `win-x64`, 내장 `Microsoft.NETCore.App` 10.x 런타임과 외부 공유 런타임 요구 부재를 확인한다.
- 검사한 파일의 크기·SHA-256과 결과를 `windows-validation.json`에 기록한다.

`-LaunchSmokeTest`는 Windows GitHub Actions의 새 프로필에서만 허용한다. 기존 `DeskPokemon` 데이터 폴더가
있으면 앱을 실행하지 않고 실패한다. 게시된 `DeskPokemon.exe`를 직접 실행해 GUI 메시지 루프,
**DeskPokemon - 스타팅 선택** 창의 제목·가시성·클라이언트 크기와 프로세스 유지를 확인한다.
작업 표시줄에 표시되지 않는 창도 찾을 수 있도록 `EnumWindows`에서 직접 시작한 PID·가시성·정확한 제목으로
창을 식별한다. 포켓몬을 선택하지 않은 채 그 창에 `WM_CLOSE`를 게시하고, 제한 시간 안에 종료 코드 0으로 끝나며 `settings.json`이
생기지 않아야 통과한다. 이미지 캐시는 일회용 runner 프로필에 생성될 수 있다.
실패 시 정리할 수 있는 프로세스는 검사가 직접 시작한 PID 하나뿐이다.

로컬에서 배포 폴더의 구조만 검사하려면 PowerShell 7 이상에서 실행한다.

```powershell
./scripts/validate-windows-publish.ps1 -PublishDirectory ./artifacts/publish/win-x64
```

## Windows 실행과 남은 실기 확인

GitHub Actions의 `DeskPokemon-win-x64` 아티팩트를 내려받아 ZIP 전체를 한 폴더에 풀고,
그 폴더의 `DeskPokemon.exe`를 실행한다. .NET 런타임은 포함되어 있으며 DLL을 EXE와 함께 유지해야 한다.

CI의 네이티브 창 검사로 Win32 시작·종료를 확인할 수 있지만, 이것이 사람이 Windows에서 전체 게임을
조작했다는 뜻은 아니다. 다음은 [공통 QA](cross-platform-qa.md)에 따라 별도로 확인한다.

- 다른 앱의 키·마우스 입력, 자동 반복 제외, 앱 내부 입력의 중복 경험치 방지.
- 도감·박스·세대 팝업·이로치 필터, 진화·부화·저장·재실행의 실제 창 조작.
- Windows 100%/200% DPI, 모니터 간 이동, 투명 창 합성·항상 위·작업 표시줄 동작.
- 실제 사용자 자동 시작 등록·해제와 재로그인 실행. 자동 등록 단위 테스트는 가짜 레지스트리 저장소를 사용한다.

스타팅 창 실행 검사에서는 전역 입력 훅이나 로그인 등록을 활성화하지 않는다.
그 경로의 자동 테스트·Mac 네이티브 플레이 결과를 Windows 실기 검증으로 대체해 기록하지 않는다.
