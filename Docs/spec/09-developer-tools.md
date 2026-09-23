# 개발용 기능과 이슈별 구현 상태

- Debug 빌드의 우클릭 메뉴에는 **테스트 알 즉시 지급**, **초기화(테스트)**, **전체 해금(치트)**, **배치 편집**, **배치 저장(소스 기본값)**, **배치 되돌리기**가 추가된다.
- 테스트 알 메뉴에서 등급을 지정하고 **이번 테스트 알 확정 이로치**를 선택할 수 있다. 기존 알은 교체된다. Release 빌드 우클릭 메뉴에는 **종료**만 있다.
- 초기화는 확인 후 저장 파일을 삭제하고 앱을 재시작한다. 전체 해금은 실행 중에만 모든 종을 선택 가능하게 하며 보유 목록에는 저장되지 않는다. 해제 시 미보유 종을 선택 중이었다면 스타팅으로 돌아간다.
- 배치 편집은 말풍선과 알 그룹을 드래그해 위치를 조정한다. 배치 저장은 `LayoutDefaults.cs`의 기본 오프셋 값을 수정하므로 다시 빌드해야 적용된다.

## GitHub 이슈 대조

| 이슈 | 현재 코드 상태 |
| --- | --- |
| [#1 멀티 플레이](https://github.com/anjihong/PokeDesk/issues/1) | 미구현 |
| [#2 진화 시스템](https://github.com/anjihong/PokeDesk/issues/2) | 미구현. 알 후보만 진화 전·비진화 종으로 제한 |
| [#3 스타팅 선택](https://github.com/anjihong/PokeDesk/issues/3) | 구현 |
| [#4 알 기능](https://github.com/anjihong/PokeDesk/issues/4) | 구현. 현재 확률은 등급·풀 기반 |
| [#5 설정](https://github.com/anjihong/PokeDesk/issues/5) | 미구현. 설정 탭, 시작 프로그램, 좌우 반전, 배율 조정 없음 |
| [#6 알 종류 분화](https://github.com/anjihong/PokeDesk/issues/6) | 구현. 5등급, 등급별 풀, 확정 결과 저장 |
| [#7 이로치](https://github.com/anjihong/PokeDesk/issues/7) | 구현. 7% 판정, 이로치알 100%, 별도 수집·성장 |
| [#8 이로치 확률](https://github.com/anjihong/PokeDesk/issues/8) | 테스트용 종료 이슈. 별도 요구 사항 없음 |
| [#9 UI](https://github.com/anjihong/PokeDesk/issues/9) | 일부 구현. 세대 탭, 보유/이로치 필터, 실루엣 제공. 전체 세대 탭·포켓몬 설명·설정 탭·픽셀 UI 적용은 미구현 |
| [#10 레벨업 디자인](https://github.com/anjihong/PokeDesk/issues/10) | 구현. 요구 경험치 `현재 레벨 × 30` |
| [#11 macOS 지원](https://github.com/anjihong/PokeDesk/issues/11) | 미구현. 현재 WPF·Win32 훅 기반 Windows 전용 |

`Assets/PixelUI`와 `design/`에는 UI 시안·부품이 있지만 현재 런타임 XAML에는 적용되지 않는다. 메뉴 2·3, 진화, 멀티 플레이, 설정, macOS 지원도 현재 구현 범위 밖이다.
