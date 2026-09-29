# DeskPokemon 현재 구현 기능 명세

> 기준: 2026-09-29 `feat/11-macos-avalonia` 브랜치 소스 코드. GitHub 이슈의 요구 사항과 실제 구현을 대조해 현재 동작만 기록한다.

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
| 10 | 이로치 획득, 도감 및 성장 | [10-shiny.md](10-shiny.md) |
| 11 | 진화 | [11-evolution.md](11-evolution.md) |
| 12 | 설정과 자동 시작 | [12-settings.md](12-settings.md) |

## 제품 및 실행 환경

- Windows와 macOS 데스크톱 위에 포켓몬을 띄우는 Avalonia 펫 애플리케이션이다. 두 OS가 같은 XAML, 픽셀 UI 부품과 Galmuri 폰트를 사용한다.
- 프로젝트는 .NET 10, Avalonia 11.3.22, SkiaSharp 2.88.9를 사용한다. Windows x64와 macOS ARM64·x64를 빌드한다.
- 메인 창은 투명 배경, 테두리 없음, 크기 자동 조절, 항상 위 표시, 작업 표시줄 숨김으로 설정된다. 픽셀 이미지는 최근접 보간 방식으로 확대한다.
- 실행 명령은 프로젝트 루트에서 `dotnet run`이다. 종료는 설정 또는 우클릭 메뉴의 **종료**를 사용한다.

## 구현된 핵심 기능

- 첫 실행 시 1~9세대 풀·불꽃·물 스타팅 후보를 무작위로 1종씩 제시한다.
- 전역 키·마우스 입력으로 현재 포켓몬의 경험치를 올린다.
- 전국도감 기본 1,025종과 지역 모습 11개, 총 1,036개 모습을 전체/세대별로 표시하고 일반·이로치 보유 상태를 독립 관리한다. 타입·한글 설명과 종별 표시 크기는 내장 데이터에서 읽는다.
- 실행 시간 30분마다 등급이 있는 알을 지급한다. 550개 알 후보에서 부화 종과 이로치 여부를 대기 시작 전에 확정·저장한다.
- 일반·이로치의 보유 목록, 선택 상태, 육성 회차와 알 진행을 스키마 3의 로컬 JSON에 저장한다. 같은 회차의 진화 전후 모습은 레벨·경험치를 공유하고 색상 사이에는 공유하지 않는다.
- 486개 직접 진화 규칙을 사용한다. 분기는 같은 색상에서 미수집인 후보 중 무작위로 확정·저장하며, 분기 완료 후 기본형을 다시 부화하면 남은 가지를 위한 새 회차를 시작할 수 있다.
- 좌우 반전, 화면 배율, OS 로그인 시 자동 실행을 제공한다.

## 기능 흐름

1. 저장 데이터가 없으면 스타팅 후보 3종 중 1종을 선택한다.
2. 메인 창에서 전역 키·마우스 입력으로 현재 포켓몬을 성장시킨다.
3. 실행 시간 30분이 쌓이면 미리 결과가 정해진 알을 부화할 수 있다.
4. 새 일반·이로치 포켓몬은 각 도감에 등록된다. 중복은 같은 색상 회차의 레벨을 1 올리며, 이미 분기를 거쳤고 미수집 가지가 남은 기본형은 새 Lv.1 회차로 다시 키운다.
5. 도감에서 보유 포켓몬을 선택하면 메인 스프라이트와 성장 대상이 바뀐다.

각 기능의 확률, 상태 전이, 실패 처리와 저장 규칙은 연결된 세부 문서에 기록한다.

## 주요 코드 위치

| 파일 | 역할 |
| --- | --- |
| `App.axaml.cs`, `StarterWindow.axaml(.cs)` | 시작 흐름과 스타팅 선택 |
| `MainWindow.axaml`, `MainWindow.*.cs` | 메인 UI, 애니메이션, 도감, 알 상태와 사용자 조작 |
| `Settings.cs` | 저장·마이그레이션, 경험치·보유·알 규칙 |
| `InputHook.cs` | 전역 키·마우스 입력 감지 |
| `SpriteAtlas.cs`, `PokemonIcons.cs`, `PokemonForms.cs` | 일반·이로치 이미지 로드, 캐시, 프레임·아이콘 처리 |
| `EggKind.cs`, `EggHatcher.cs`, `EggArtwork.cs` | 알 등급, 확정 결과 추첨, 등급별 외형 |
| `BaseSpecies.cs`, `PokemonRarity.cs` | 부화 후보와 일반·특수 풀 분류 |
| `PokemonNames.cs`, `PokemonDetails.cs`, `Assets/Data/` | 도감 번호별 한글 이름·타입·설명과 고정 데이터 |
| `EvolutionData.cs`, `EvolutionRules.cs`, `MainWindow.Evolution.cs` | 지역 모습·486개 진화 규칙, 예약·완료와 연출 |
| `PokemonDisplaySize.cs`, `Assets/Data/pokemon-sizes.json` | 고정 종 높이와 92~120 DIP 목표 표시 크기 |
| `StartupRegistration.cs`, `Platform/*StartupRegistration.cs` | OS별 로그인 자동 실행 등록·해제 |
| `PixelSurface.cs`, `PixelStyles.axaml`, `Assets/PixelUI/` | 공통 픽셀 패널·버튼·폰트 |
| `LayoutDefaults.cs` | 말풍선·알 기본 위치 |

## 구현 범위와 검증

- 명세는 실제 구현 동작을 기록하며, 미완료 범위는 [이슈별 상태](09-developer-tools.md)에 별도로 표시한다.
- 멀티플레이 이슈 #1은 사용자 요청으로 보류한다.
- main `78dc8cb`까지의 게임 규칙과 데이터를 반영한다. 충돌 시 main 변경을 우선하고 WPF 의존 부분은 공통 Avalonia UI에 맞게 이식한다. [병합 기준](../CONTRIBUTING.md#main-업데이트-반영)을 따른다.
- 자동 테스트와 Windows/macOS의 같은 UI 렌더 비교를 실행한다. 실제 Mac 창 조작은 별도 플레이 기록으로 남기며 Windows 실기 검증과 구분한다.

최근 종별 크기·main 업데이트와 실제 Mac 플레이는 [2026-09-29 검증 기록](../playtest-2026-09-29.md)을 따른다.
이전 기능 구현 과정은 [2026-09-28 검증 기록](../playtest-2026-09-28.md)에 남긴다.
