# DeskPokemon 구현 명세

> 기준: 2026-09-21 현재 작업 트리의 소스 코드. 현재 구현된 동작을 기록한다.

## 문서 목록

| 번호 | 분류 | 문서 |
| --- | --- | --- |
| 01 | 개요와 코드 구조 | 이 문서 |
| 02 | 시작 및 스타팅 선택 | [02-startup.md](02-startup.md) |
| 03 | 메인 창과 포켓몬 표시 | [03-pet-display.md](03-pet-display.md) |
| 04 | 입력 경험치와 레벨 | [04-input-and-level.md](04-input-and-level.md) |
| 05 | 도감과 포켓몬 선택 | [05-pokedex.md](05-pokedex.md) |
| 06 | 알 지급 및 부화 | [06-egg.md](06-egg.md) |
| 07 | 저장 데이터 | [07-save-data.md](07-save-data.md) |
| 08 | 스프라이트와 데이터 자산 | [08-assets.md](08-assets.md) |
| 09 | 개발용 기능과 현재 범위 | [09-developer-tools.md](09-developer-tools.md) |

## 제품 및 실행 환경

- Windows 데스크톱 위에 포켓몬을 띄우는 WPF 펫 애플리케이션이다.
- 프로젝트는 .NET 9의 `net9.0-windows`를 대상으로 하며 외부 NuGet 패키지는 사용하지 않는다.
- 메인 창은 투명 배경, 테두리 없음, 크기 자동 조절, 항상 위 표시, 작업 표시줄 숨김으로 설정된다. 픽셀 이미지는 `NearestNeighbor` 방식으로 확대한다.
- 실행 명령은 프로젝트 루트에서 `dotnet run`이다. 종료는 우클릭 메뉴의 **종료**를 사용한다.

## 주요 코드 위치

| 파일 | 역할 |
| --- | --- |
| `App.xaml.cs`, `StarterWindow.xaml(.cs)` | 시작 흐름과 스타팅 선택 |
| `MainWindow.xaml(.cs)` | 메인 UI, 애니메이션, 도감, 알 상태와 사용자 조작 |
| `Settings.cs` | 저장·마이그레이션, 경험치·보유·알 규칙 |
| `InputHook.cs` | 전역 키·마우스 입력 감지 |
| `SpriteAtlas.cs`, `PokemonIcons.cs`, `PokemonForms.cs` | 원격 이미지 로드, 캐시, 프레임·아이콘 처리 |
| `EggHatcher.cs`, `BaseSpecies.cs`, `CaptureRates.cs` | 부화 후보와 가중 추첨 |
| `PokemonNames.cs` | 도감 번호별 한글 이름 |
| `LayoutDefaults.cs` | 말풍선·알 기본 위치 |
