# DeskPokemon

Windows / macOS 데스크톱 포켓몬 펫. 투명 오버레이 창에 애니메이션 스프라이트를 띄우고,
키 입력·마우스 클릭마다 바운스합니다. Avalonia UI / .NET 10을 사용합니다.

Windows와 macOS 모두 같은 Avalonia 화면·스타일·번들 한글 폰트를 사용합니다.
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
- 포켓몬 아래 도감 패널: 세대 탭(1~9) 선택 후 아이콘 클릭으로 포켓몬 교체. 아직 획득하지 못한
  포켓몬은 실루엣으로 표시되고 선택 불가. 처음에는 선택한 스타팅만 보유. "보유만 보기"로 현재 세대의
  보유 포켓몬만 볼 수 있고, 아이콘에 마우스를 올리면 번호와 이름이 뜸
- 알: 포켓몬 오른쪽 알의 말풍선에 다음 알까지 남은 시간(앱 실행 시간 기준 30분). 시간이 되면 "클릭하여 부화"가
  뜨고 알을 클릭하면 부화 애니메이션 후 진화 전 포켓몬(진화하지 않는 포켓몬 포함) 중 랜덤으로 도감에 등록됨(새 포켓몬이면 NEW!).
  이미 있는 포켓몬이면 그 포켓몬의 레벨 +1. 알은 한 번에 하나만, 깔 때까지 타이머는 멈춤.
  확률은 공식 포획률(capture rate)에 비례해 전설일수록 드묾. 우클릭 메뉴 "알 즉시 획득(테스트)"로 바로 받아볼 수 있음
- 메뉴 탭(도감 / 메뉴 2 / 메뉴 3): 누르면 아래로 패널이 펼쳐지고 다시 누르면 접힘. 2·3은 추후 추가 예정
- 레벨: 키 입력·마우스 클릭마다 경험치 +1, 레벨 × 10회마다 레벨업. 포켓몬별로 따로 기록됨.
  키를 길게 눌러 생기는 자동 반복은 추가 입력으로 세지 않음

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

`settings.json`에는 스타팅·선택 포켓몬, 보유 목록, 포켓몬별 레벨·경험치, 알 진행 상태가 저장됩니다.
기존 Windows 저장 위치를 유지하며, 이전 세이브도 로드합니다.
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
빌드·테스트 후 실행 파일을 아티팩트로 보관합니다. 같은 테스트 자산으로 만든 9개 화면 PNG도
OS 간 비교하며, 크기 차이와 허용 범위를 넘는 픽셀 차이를 실패 처리합니다.
`desktop-visual-comparison` 아티팩트에서 비교 결과와 차이 이미지를 확인할 수 있습니다.
실제 양쪽 화면, DPI/Retina, 권한과 전역 입력 확인은
[Windows/macOS QA](docs/cross-platform-qa.md)에 따라 별도로 기록합니다.
커밋·이슈 관례는 [기여 안내](docs/CONTRIBUTING.md)에 정리했습니다.

## 스프라이트 출처 및 라이선스

공통 한글 폰트는 [나눔고딕](https://github.com/google/fonts/tree/main/ofl/nanumgothic)을 사용하며,
재배포 라이선스는 [SIL Open Font License](Assets/Fonts/OFL.txt)에 포함되어 있습니다.

스프라이트는 앱에 포함되지 않으며, 첫 실행 시
[pagefaultgames/pokerogue-assets](https://github.com/pagefaultgames/pokerogue-assets) 의
`images/pokemon/`, `images/pokemon/exp/`, `images/pokemon_icons_*`, `images/egg/` 에서 받아 위 데이터 폴더의 `sprites/`에 캐시합니다.
PokéRogue 쪽 스프라이트가 정지(1프레임)인 일부 종은
[PokeAPI/sprites](https://github.com/PokeAPI/sprites) 의 `versions/generation-v/black-white/animated/` GIF를 대신 사용합니다.
포획률 수치는 [PokeAPI](https://github.com/PokeAPI/pokeapi) `pokemon_species.csv` 에서 가져와 코드에 포함했습니다.

- 재생 방식(프레임 정렬, 10fps 루프)은 PokéRogue와 동일합니다.
- 해당 저장소 자산은 라이선스 가능한 범위에서 CC-BY-NC-SA-4.0이며, 원작 스프라이트는
  저장소 측이 공정 이용을 주장하고 재라이선스하지 않습니다. 자세한 내용은 저장소 README 및 `REUSE.toml` 참고.
- 작가 크레딧: https://github.com/pagefaultgames/pokerogue/blob/beta/CREDITS.md
- PokeAPI GIF 중 도감 번호 650 초과는 비공식 스프라이트로, Smogon 커뮤니티가 제작한 BW 스타일 스프라이트입니다.
  작가 목록과 출처는 [PokeAPI/sprites README](https://github.com/PokeAPI/sprites) 참고.

비영리 팬 프로젝트입니다. Nintendo, Game Freak, Creatures Inc., The Pokémon Company와 무관합니다.
