# DeskPokemon 현재 구현 기능 명세

> 기준: 2026-09-23 `main` 브랜치 소스 코드. GitHub 이슈의 요구 사항과 실제 구현을 대조해 현재 동작만 기록한다.

## 문서 목록

| 번호 | 분류 | 문서 |
| --- | --- | --- |
| 01 | 개요와 코드 구조 | 이 문서 |
| 02 | 시작 및 스타팅 선택 | [02-startup.md](02-startup.md) |
| 03 | 메인 창과 포켓몬 표시 | [03-pet-display.md](03-pet-display.md) |
| 04 | 입력 경험치와 레벨 | [04-input-and-level.md](04-input-and-level.md) |
| 05 | 도감과 포켓몬 선택 | [05-pokedex.md](05-pokedex.md) |
| 06 | 알 등급, 지급 및 부화 | [06-egg.md](06-egg.md) |
| 07 | 저장 데이터 | [07-save-data.md](07-save-data.md) |
| 08 | 스프라이트와 데이터 자산 | [08-assets.md](08-assets.md) |
| 09 | 개발용 기능과 이슈별 구현 상태 | [09-developer-tools.md](09-developer-tools.md) |

## 제품 및 실행 환경

- Windows 데스크톱 위에 포켓몬을 띄우는 WPF 펫 애플리케이션이다.
- 프로젝트는 .NET 9의 `net9.0-windows`를 대상으로 하며 외부 NuGet 패키지는 사용하지 않는다.
- 메인 창은 투명 배경, 테두리 없음, 크기 자동 조절, 항상 위 표시, 작업 표시줄 숨김으로 설정된다. 픽셀 이미지는 `NearestNeighbor` 방식으로 확대한다.
- 실행 명령은 프로젝트 루트에서 `dotnet run`이다. 종료는 우클릭 메뉴의 **종료**를 사용한다.

## 구현된 핵심 기능

- 첫 실행 시 1~9세대 풀·불꽃·물 스타팅 후보를 무작위로 1종씩 제시한다.
- 전역 키·마우스 입력으로 현재 포켓몬의 경험치를 올린다.
- 전국도감 1,025종을 세대별로 표시하고 일반·이로치 보유 상태를 독립 관리한다.
- 실행 시간 30분마다 등급이 있는 알을 지급한다. 부화 종과 이로치 여부는 대기 시작 전에 확정·저장한다.
- 일반·이로치의 보유 목록, 선택 상태, 레벨, 경험치와 알 진행을 로컬 JSON에 저장한다.

## 주요 코드 위치

| 파일 | 역할 |
| --- | --- |
| `App.xaml.cs`, `StarterWindow.xaml(.cs)` | 시작 흐름과 스타팅 선택 |
| `MainWindow.xaml(.cs)` | 메인 UI, 애니메이션, 도감, 알 상태와 사용자 조작 |
| `Settings.cs` | 저장·마이그레이션, 경험치·보유·알 규칙 |
| `InputHook.cs` | 전역 키·마우스 입력 감지 |
| `SpriteAtlas.cs`, `PokemonIcons.cs`, `PokemonForms.cs` | 일반·이로치 이미지 로드, 캐시, 프레임·아이콘 처리 |
| `EggKind.cs`, `EggHatcher.cs`, `EggArtwork.cs` | 알 등급, 확정 결과 추첨, 등급별 외형 |
| `BaseSpecies.cs`, `PokemonRarity.cs` | 부화 후보와 일반·특수 풀 분류 |
| `PokemonNames.cs` | 도감 번호별 한글 이름 |
| `LayoutDefaults.cs` | 말풍선·알 기본 위치 |
