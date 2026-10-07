# DeskPokemon

Windows / macOS 데스크톱 포켓몬 펫. 투명 오버레이 창에 애니메이션 스프라이트를 띄우고,
키 입력·마우스 클릭마다 바운스합니다. Avalonia UI / .NET 10을 사용합니다.

Windows와 macOS 모두 같은 Avalonia 화면·픽셀 UI·번들 Galmuri 한글 폰트를 사용합니다.
Windows 빌드도 Avalonia로 전환했으며, 플랫폼별로 전역 입력과 권한 처리를 연결합니다.
관련 작업은 [#11 FEAT/macOS 지원](https://github.com/anjihong/PokeDesk/issues/11)에서 추적합니다.

## 실행

소스에서 실행하려면 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)를 설치하세요.
Mac은 macOS 14 이상에서 Apple Silicon(`osx-arm64`) 또는 Intel(`osx-x64`) 빌드를 사용합니다.
OS 지원 범위는 [.NET 10 지원 OS](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md)를 따릅니다.

```bash
dotnet run --project DeskPokemon.csproj
```

첫 실행에서는 풀·불꽃·물 후보 중 스타팅을 선택합니다.
스프라이트는 첫 사용 시 다운로드하므로 네트워크 연결이 필요합니다.

- 좌클릭 드래그: 이동
- 우클릭 → 종료
- 포켓몬 아래 도감 패널: 기본종 1,025종과 지역 모습 11개, 총 1,036개 모습을 전체/세대 탭(1~9)에서 조회하고 아이콘 클릭으로 포켓몬 교체. 아직 획득하지 못한
  포켓몬은 실루엣으로 표시되고 선택 불가. 처음에는 선택한 스타팅만 보유. "보유만 보기"로 현재 세대의
  보유 포켓몬만 볼 수 있음. 상세 영역에는 한글 타입·설명·현재 경험치를 표시함. 기본 목록에서는 보유한 일반·이로치를 함께 표시하고, "이로치만"을 켜면 이로치만 표시함. 필터만 바꿔서는 현재 펫이 바뀌지 않음.
  아이콘 툴팁에 전국도감 번호·이름·해당 색상의 육성 회차 레벨이 표시되고, 미보유 색상은 선택 불가. 지역 모습도 원종의 전국도감 번호를 사용함
- 알: 포켓몬 오른쪽 알의 말풍선에 다음 알까지 남은 시간(앱 실행 시간 기준 30분). 시간이 되면 "클릭하여 부화"가
  뜨고 알을 클릭하면 부화 애니메이션 후 550개 후보의 기본형·진화하지 않는 포켓몬을 도감에 등록함(새 색상이면 NEW!). 지역 모습도 포함하며 이브이 진화형 8종은 새 알 후보에서 제외함.
  같은 모습·같은 색상을 이미 보유했다면 보통 그 회차의 레벨 +1. 분기를 거친 뒤 미수집 가지가 남은 기본형이 다시 부화하면 새 Lv.1 회차로 키우며 기존 진화형은 이전 회차에 남음. 알은 한 번에 하나만, 깔 때까지 타이머는 멈춤.
  알의 등급·포켓몬·이로치 여부는 대기 시작 시 확정되어 저장되며, 재실행이나 부화 재시도로 다시 추첨하지 않음.
  등급은 커먼 47.5%, 레어 30%, 에픽 15%, 레전더리 5%, 이로치알 2.5%.
  등급에 따라 일반/특수 포켓몬 그룹을 고른 뒤 그룹 안에서 균등 추첨함. 이로치알은 이로치 확정,
  나머지 알은 7% 확률로 이로치가 나옴. 부화 결과는 해당 색상의 애니메이션으로 표시됨.
  Debug 빌드의 "테스트 알" 메뉴에서는 등급별 알 지급과 다음 지급 1회의 이로치 강제를 확인할 수 있음
- 메뉴 탭(도감 / 설정): 누르면 패널 높이만큼 현재 위치에서 위로 이동하며 펼쳐지고, 다시 누르면 접히며 원래 위치로 돌아옴
- 설정: 포켓몬 좌우 반전, 전체 UI 배율 2·4·6·8배, 로그인 시 자동 실행, 종료. 배율은 화면 안에 들어가도록 제한되며 선택값은 유지됨. 자동 실행은 Windows 사용자 시작 항목 또는 Mac LaunchAgent에 이 실행 파일을 등록함
- 진화: 486개 직접 진화 규칙에 따라 종별 레벨에 도달하면 자동으로 진화하며 이전 모습도 계속 선택할 수 있음. 레벨 외 조건은 첫 진화 25/다음 진화 40으로 대체. 분기는 같은 색상의 미수집 후보 중 무작위로 정해 이미지 로딩 전에 저장함. 이브이는 Lv.25에 8개 진화형 중 하나로 진화함. 이전 모습을 선택해 키워도 같은 회차의 경험치가 쌓이며, 다음 조건을 달성하면 안내된 현재 출발 모습을 도감에서 선택해 이어서 진화할 수 있음
- 레벨: 키 입력·마우스 클릭마다 경험치 +1, 레벨 × 30회마다 레벨업. 같은 육성 회차의 진화 전후 모습은 레벨·경험치를 공유함. 일반/이로치의 보유·육성 회차는 따로 기록되며, 이로치 진화 결과도 이로치로 유지됨.
  이로치 펫은 레벨 앞에 ★ 표시가 붙음.
  경험치 바에 커서를 올리면 현재 경험치와 다음 레벨에 필요한 경험치를 확인할 수 있음.
  키를 길게 눌러 생기는 자동 반복은 추가 입력으로 세지 않음
- 표시 크기: 내장한 종별 높이로 목표 몸체 높이를 72~100 DIP 안에서 조절함. 파이리·리자드·리자몽은 76·86·94 DIP이며, 폭이 넓으면 212 DIP 안에 맞추고 실제 발 하단을 그림자에 정렬함. 일반/이로치와 진화 연출이 같은 [표시 규칙](Docs/spec/03-pet-display.md)을 사용함

## macOS 입력 권한

다른 앱에서 발생한 키 입력·클릭에도 반응하려면 **시스템 설정 → 개인정보 보호 및 보안 → 입력 모니터링**에서
DeskPokemon을 허용하세요. 입력 내용을 저장하거나 전송하지 않으며, 입력 횟수로 바운스와 경험치만 처리합니다.
권한이 없으면 앱 안에 설명과 **권한 요청 / 다시 연결** 버튼이 표시됩니다.
우클릭 메뉴의 **전역 입력 다시 연결**에서도 다시 시도할 수 있습니다.
허용을 변경한 후 연결되지 않으면 앱을 완전히 종료하고 다시 실행하세요.
권한을 허용하지 않아도 스타팅 선택·도감·알·저장 기능은 사용할 수 있습니다.

`dotnet run`으로 실행하면 권한 대상이 터미널 또는 dotnet 호스트로 표시될 수 있습니다.
배포 환경 확인은 아래 `.app`을 고정된 위치(예: Applications)에 놓고 실행해서 진행하세요.
macOS의 읽기 전용 전역 입력은 Input Monitoring을 사용합니다.
[Apple DTS 설명](https://developer.apple.com/forums/thread/811443)

## 저장 데이터와 캐시

| OS | 데이터 폴더 |
| --- | --- |
| Windows | `%LOCALAPPDATA%\DeskPokemon` |
| macOS | `~/Library/Application Support/DeskPokemon` |

`settings.json` 스키마 3에는 스타팅·선택 모습과 색상(`SelectedShiny`), 일반/이로치 보유 목록
(`Owned`, `ShinyOwned`), 모습과 육성 회차의 연결(`GrowthLinks`, `ShinyGrowthLinks`),
회차별 성장 기록(`Progress`, `ShinyProgress`)을 저장합니다. 성장 기록에는 레벨·경험치와 현재 모습,
진화 이력, 미리 확정한 진화 대상(`PendingEvolution`)이 포함됩니다. 지역 모습 ID는 전국도감 번호와 구분합니다.
알 진행 시간과 확정된 다음 알(`PendingEgg`: 등급·모습 ID·색상), 좌우 반전(`FlipHorizontal`)과 배율(`UiScale`)도 저장합니다.
기존 Windows 저장 위치를 유지합니다. 이전 스키마를 처음 읽을 때 원본을
`settings.json.schema<이전 버전>.bak`으로 보관하고 변환합니다. 스키마 2의 종별 성장 기록은 각각 독립 회차로
보존하고 새로 진행하는 진화부터 공유합니다. 스키마 1에서 이미 지급된 알은 커먼으로 변환하며, 이미 확정된 스키마 2의 알과
기존 이브이 진화형 보유 기록은 유지합니다.
현재 앱보다 새로운 스키마나 손상된 JSON은 덮어쓰지 않고 로드를 거부합니다.
스프라이트는 같은 폴더의 `sprites/`에 캐시합니다. 이미 받은 자산은 오프라인에서도 재사용합니다.
OS 간 진행 상태를 옮기려면 양쪽 앱을 종료한 뒤 `settings.json`을 복사하세요.
`sprites/`도 복사하면 다시 다운로드하지 않아도 됩니다.

## 빌드·테스트·패키징

```bash
dotnet build DeskPokemon.csproj -c Release
dotnet test Tests/DeskPokemon.Tests/DeskPokemon.Tests.csproj -c Release

# Windows x64: 출력 폴더의 DeskPokemon.exe 실행
dotnet publish DeskPokemon.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish/win-x64

# macOS Apple Silicon: Mac에서 .app 생성 (Intel은 osx-x64로 변경)
dotnet publish DeskPokemon.csproj -c Release -r osx-arm64 --self-contained true -o artifacts/publish/osx-arm64
bash scripts/package-macos.sh artifacts/publish/osx-arm64 artifacts/package/osx-arm64
open artifacts/package/osx-arm64/DeskPokemon.app
```

배포 출력은 .NET 런타임을 포함합니다. `package-macos.sh`는 `.app`과 실행 권한을 보존하는
`DeskPokemon.zip`을 생성하고 로컬 테스트용 ad-hoc 서명을 적용합니다.
세 번째 인수로 `1.2.3` 형식의 버전을 지정할 수 있습니다. 기존 출력과 섞이지 않도록 새 출력 폴더를 사용하세요.
현재 스크립트는 Apple Developer ID 서명이나 공증을 수행하지 않습니다.
공개 배포 전에 [Avalonia macOS 배포 안내](https://docs.avaloniaui.net/docs/deployment/macos)에 따라
서명·공증을 진행해야 합니다. 테스트 빌드가 macOS에서 차단되면 출처를 확인한 뒤
시스템 설정의 개인정보 보호 및 보안에서 앱 실행 허용 절차를 따르세요.

[GitHub Actions](.github/workflows/desktop.yml)는 Windows x64, Mac ARM64, Mac x64에서
빌드·테스트 후 실행 파일을 아티팩트로 보관합니다. 일반/이로치 도감, 다섯 알 등급, 부화 결과와
경험치 툴팁·전체 도감·설정·분기 진화·진화 연출 3시점을 포함해 같은 테스트 자산으로 만든 30개 화면 PNG도
OS 간 비교하며, 레이아웃·텍스트·폰트·기록된 색상은 정확하게 일치해야 합니다.
이미지는 OS별 글자 래스터화 차이를 제한적으로 허용하고, 원본 오차와 판별된 경계 차이를 함께 보고합니다.
`desktop-visual-comparison` 아티팩트에서 비교 결과와 차이 이미지를 확인할 수 있습니다.
실제 양쪽 화면, DPI/Retina, 권한과 전역 입력 확인은
[Windows/macOS QA](Docs/cross-platform-qa.md)에 따라 별도로 기록합니다.
커밋·이슈 관례는 [기여 안내](Docs/CONTRIBUTING.md)에 정리했습니다.

macOS의 실제 창·마우스·키 입력 검증은 다음 도우미로 실행할 수 있습니다. 입력 모니터링 권한이 있는
환경에서 실행하며, 실제 이미지 캐시와 메모리 미리보기 세이브를 사용해 플레이어의 저장 파일은 바꾸지 않습니다.

```bash
dotnet run --project Tests/DeskPokemon.NativePlay/DeskPokemon.NativePlay.csproj -c Release
dotnet run --project Tests/DeskPokemon.NativePlay/DeskPokemon.NativePlay.csproj -c Release -- --reference
```

`--reference`는 파이리만 보유하고 커먼 알이 대기하는 상태에서 도감 1세대·박스·설정을 실제로 클릭합니다.
애니메이션을 유지한 채 `artifacts/native-play/reference-ui.png`, `reference-box.png`, `reference-settings.png`를
저장한 후 종료합니다. 인자를 생략하면 입력·선택·진화·부화·지역 모습의 전체 플레이 검증을 실행합니다.

## 스프라이트 출처 및 라이선스

공통 UI 한글 폰트는 [Galmuri v2.40.4](https://github.com/quiple/galmuri)를 사용하며,
재배포 라이선스는 [Galmuri OFL](Assets/Fonts/LICENSE-Galmuri.txt)에 포함되어 있습니다. 기존 나눔고딕과 [라이선스](Assets/Fonts/OFL.txt)도 보관합니다.
지역 모습 카탈로그·진화 조건·한글 타입·설명·종별 높이는 고정한 PokeAPI 데이터에서 생성해 앱에 포함합니다. 출처·해시와 갱신 방법은
[자산 명세](Docs/spec/08-assets.md), 전체 기능 명세는 [개요](Docs/spec/01-overview.md)를 참고하세요.
멀티플레이(#1)는 보류되어 있습니다.

스프라이트는 앱에 포함되지 않으며, 첫 실행 시
[pagefaultgames/pokerogue-assets](https://github.com/pagefaultgames/pokerogue-assets) 의
`images/pokemon/`, `images/pokemon/exp/`와 각 경로의 `shiny/`, `images/pokemon_icons_*`, `images/egg/`에서 받아 위 데이터 폴더의 `sprites/`에 캐시합니다.
PokéRogue 쪽 스프라이트가 정지(1프레임)인 일부 종은
[PokeAPI/sprites](https://github.com/PokeAPI/sprites) 의 `versions/generation-v/black-white/animated/` GIF를 대신 사용합니다.
지역 모습은 내부 ID가 PokeAPI 번호와 다르므로 GIF·대체 PNG를 요청하지 않고 해당 모습의 아틀라스만 사용합니다.
일반/이로치 자산은 별도 캐시하며, 이로치 자산이 없을 때 일반 색상으로 대체하지 않습니다.

- 재생 방식(프레임 정렬, 10fps 루프)은 PokéRogue와 동일합니다.
- 해당 저장소 자산은 라이선스 가능한 범위에서 CC-BY-NC-SA-4.0이며, 원작 스프라이트는
  저장소 측이 공정 이용을 주장하고 재라이선스하지 않습니다. 자세한 내용은 저장소 README 및 `REUSE.toml` 참고.
- 작가 크레딧: https://github.com/pagefaultgames/pokerogue/blob/beta/CREDITS.md
- PokeAPI GIF 중 도감 번호 650 초과는 비공식 스프라이트로, Smogon 커뮤니티가 제작한 BW 스타일 스프라이트입니다.
  작가 목록과 출처는 [PokeAPI/sprites README](https://github.com/PokeAPI/sprites) 참고.

비영리 팬 프로젝트입니다. Nintendo, Game Freak, Creatures Inc., The Pokémon Company와 무관합니다.
