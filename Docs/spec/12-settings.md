# 설정

메인 화면의 **설정** 탭에서 본체 좌우 반전, 로그인 시 자동 시작, UI 배율과 종료를 제공한다. 도감과 같은 픽셀 패널을 사용하며 Windows와 macOS가 같은 컨트롤·레이아웃을 공유한다.

## 본체 좌우 반전

- 좌우 반전 옵션은 포켓몬 본체 이미지의 표시 방향을 바꾼다. 이름·경험치·말풍선·알·도감과 설정 텍스트는 뒤집지 않는다. 이미지 로드 실패 시 표시하는 이름과 **이미지 없음** 안내도 정상 방향을 유지한다.
- 포켓몬 크기 보정과 클릭·레벨업 애니메이션을 유지한 채 본체를 감싼 요소에 반전을 적용한다.
- `FlipHorizontal`을 저장해 다음 실행에도 유지한다. 저장에 실패하면 이전 값과 체크 상태로 되돌린다.

## UI 배율과 화면 맞춤

- 축소·확대 버튼으로 **2·4·6·8배** 중 하나를 고른다. 기본값은 2배이며 양 끝에서는 해당 방향 버튼을 비활성화한다.
- 메인 본체와 경험치 표시, 도감·설정 패널을 함께 확대한다. 포켓몬 종별 본체 크기 보정과는 별도다.
- 기본 UI는 폭 352 DIP이며, 2px 자산 격자를 1 DIP로 배치한다. 표시값 2·4·6·8배는 기본 레이아웃에 각각 1·2·3·4배 변환을 요청한다.
- 현재 모니터의 작업 영역과 OS 화면 배율을 고려해 창과 사용 가능한 패널 영역이 들어가도록 실제 배율을 낮출 수 있다. 이때 요청한 배율 옆에 **화면 맞춤**을 표시하고, 도감·설정의 높이는 스크롤 가능한 영역으로 제한한다.
- 화면 맞춤은 실제 표시 배율만 조정한다. 저장한 사용자의 배율은 유지하며, UI 배율과 DPI/Retina 배율을 중복 저장하지 않는다.
- `UiScale`을 저장해 다음 실행에 복원한다. 저장 실패 시 이전 값으로 되돌린다. 이전 저장 파일에 값이 없으면 2배를 사용한다.

## 로그인 시 자동 시작

- Windows와 macOS에서 **현재 사용자**의 로그인 시 앱을 시작하도록 등록할 수 있다. 컴퓨터 부팅 시 시스템 서비스로 실행하는 기능은 아니다.
- 앱 생성, 앱 실행, 설정 화면 열기 및 저장 데이터 읽기는 등록을 생성하거나 복구하지 않는다. 사용자가 자동 시작 옵션을 직접 바꿀 때만 `IStartupRegistration.SetEnabled`를 호출한다.
- `Query`는 OS의 등록을 읽어 `IsSupported`, `IsEnabled`, `Error`를 반환한다. OS에 등록된 상태를 표시하며, 별도의 시스템 설정에서 시작 항목을 차단했는지까지 감시하지 않는다.
- Windows는 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`의 `PokeDesk` 문자열 값 하나를 관리한다. 실행 파일의 전체 경로를 따옴표로 감싸며 셸이나 환경 변수 확장을 사용하지 않는다. Run 명령의 260자 제한을 넘으면 등록하지 않고 오류를 반환한다.
- macOS는 `~/Library/LaunchAgents/io.github.anjihong.PokeDesk.plist`에 `ProgramArguments`, `RunAtLoad=true`, `LimitLoadToSessionType=Aqua`를 저장한다. `.app/Contents/MacOS/` 내부를 포함한 실제 실행 파일 전체 경로가 인자 하나로 들어가며 공백·한글·XML 특수 문자를 그대로 보존한다. 파일은 현재 사용자만 읽고 쓸 수 있다.
- macOS 등록은 **다음 로그인부터** 적용한다. 등록할 때 `launchctl bootstrap`이나 앱 프로세스를 실행하지 않으므로 현재 앱이 중복 실행되지 않는다. `KeepAlive`를 사용하지 않아 사용자가 닫은 앱을 다시 띄우지 않는다.
- 옵션을 끄면 앱이 소유한 등록만 삭제한다. 현재 실행 중인 앱과 다른 앱의 시작 항목에는 영향을 주지 않는다. 이미 꺼져 있다면 추가 변경 없이 유지한다.
- `dotnet` 호스트로 DLL을 직접 실행 중이거나 실제 실행 파일 경로를 찾을 수 없으면 켜기를 거절하고 오류를 반환한다. 배포된 실행 파일 또는 앱 번들에서 설정할 수 있다. 기존 등록을 끄거나 조회하는 작업은 개발 실행 중에도 가능하다.
- 파일·레지스트리 접근 실패는 반환값의 `Error`로 표시하고 실제 조회된 등록 상태를 유지한다. 관리자 권한이나 OS 권한 요청 대화상자를 띄우지 않는다.
- 등록 뒤 앱을 옮긴 경우 자동 시작을 다시 켜서 새 실행 경로로 등록해야 한다.

구현은 `StartupRegistration.cs`, `Platform/WindowsStartupRegistration.cs`, `Platform/MacStartupRegistration.cs`다. 테스트는 가짜 레지스트리와 임시 폴더만 사용하며 실제 로그인 설정을 변경하지 않는다.

OS 동작 근거: [Microsoft Run/RunOnce 문서](https://learn.microsoft.com/en-us/windows/win32/setupapi/run-and-runonce-registry-keys), [Apple LaunchAgent 문서](https://developer.apple.com/library/archive/documentation/MacOSX/Conceptual/BPSystemStartup/Chapters/CreatingLaunchdJobs.html).

## 종료

- 설정 패널의 **종료**와 우클릭 메뉴의 **종료**는 같은 창 닫기 동작을 수행한다.
- 종료 전에 현재 상태를 저장한다. 저장에 실패하면 창을 닫지 않고 오류를 표시해 다시 시도할 수 있게 한다.
- 정상 종료하면 전역 입력 연결·타이머·진행 중인 창 작업을 정리한다. 자동 시작 등록은 종료해도 유지되며, 등록을 해제하려면 옵션을 직접 꺼야 한다.

저장 필드와 복구 규칙은 [07-save-data.md](07-save-data.md), 화면 배율과 OS별 확인 항목은 [Windows/macOS QA](../cross-platform-qa.md)를 참고한다.
