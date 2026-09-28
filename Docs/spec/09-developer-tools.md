# 개발용 기능과 이슈별 구현 상태

- Debug 빌드의 우클릭 메뉴에는 **테스트 알 즉시 지급**, **초기화(테스트)**, **전체 해금(치트)**, **배치 편집**, **배치 저장(소스 기본값)**, **배치 되돌리기**가 추가된다.
- 테스트 알 메뉴에서 등급을 지정하고 **이번 테스트 알 확정 이로치**를 선택할 수 있다. 기존 알은 교체된다. Release 빌드에는 이러한 개발 메뉴가 없으며 전역 입력 재연결, 포켓몬 그림 재시도와 종료 같은 일반 메뉴를 제공한다.
- 초기화는 확인 후 저장 파일을 삭제하고 앱을 재시작한다. 전체 해금은 실행 중에만 모든 종을 선택 가능하게 하며 보유 목록에는 저장되지 않는다. 해제 시 미보유 종을 선택 중이었다면 스타팅으로 돌아간다.
- 배치 편집은 말풍선과 알 그룹을 드래그해 위치를 조정한다. 배치 저장은 `LayoutDefaults.cs`의 기본 오프셋 값을 수정하므로 다시 빌드해야 적용된다.

## GitHub 이슈 대조

아래는 현재 작업 트리의 구현 상태다. GitHub 이슈의 열림·닫힘 상태나 배포 완료 여부를 뜻하지 않는다. 멀티플레이는 사용자의 명시적 요청으로 보류하고 다른 기능을 먼저 마무리한다.

| 이슈 | 현재 코드 상태 |
| --- | --- |
| [#1 멀티 플레이](https://github.com/anjihong/PokeDesk/issues/1) | **사용자 요청으로 보류.** 방 생성·참가·친구 표시·네트워크 연결을 제공하지 않음 |
| [#2 진화 시스템](https://github.com/anjihong/PokeDesk/issues/2) | 구현. 레벨 조건, 이전 단계 보존, 색상별 성장 계승, 분기 선택, 이브이 예외. [상세 명세](11-evolution.md) |
| [#3 스타팅 선택](https://github.com/anjihong/PokeDesk/issues/3) | 구현 |
| [#4 알 기능](https://github.com/anjihong/PokeDesk/issues/4) | 구현. 현재 확률은 등급·풀 기반 |
| [#5 설정](https://github.com/anjihong/PokeDesk/issues/5) | 구현. 설정 탭, 사용자별 자동 시작, 본체 좌우 반전, 2·4·6·8배와 화면 맞춤, 종료. [상세 명세](12-settings.md) |
| [#6 알 종류 분화](https://github.com/anjihong/PokeDesk/issues/6) | 구현. 5등급, 등급별 풀, 확정 결과 저장 |
| [#7 이로치](https://github.com/anjihong/PokeDesk/issues/7) | 구현. 7% 판정, 이로치알 100%, 별도 수집·성장·진화, 자체 금색 별무늬 알. [상세 명세](10-shiny.md) |
| [#8 이로치 확률](https://github.com/anjihong/PokeDesk/issues/8) | 테스트용 종료 이슈. 별도 요구 사항 없음 |
| [#9 UI](https://github.com/anjihong/PokeDesk/issues/9) | 구현. 공통 픽셀 UI·Galmuri, 전체 1,025종과 세대별 도감, 보유/이로치 필터, 누락 그림 셀 유지·재시도, 한글 타입·설명·성장 상세, 설정 탭 |
| [#10 레벨업 디자인](https://github.com/anjihong/PokeDesk/issues/10) | 구현. 요구 경험치 `현재 레벨 × 30` |
| [#11 macOS 지원](https://github.com/anjihong/PokeDesk/issues/11) | 구현. Windows·macOS 공통 Avalonia UI, OS별 전역 입력·자동 시작, macOS 앱 패키징, 3개 대상 CI 및 화면 비교 |

`Assets/PixelUI`와 번들 Galmuri 폰트는 실행 화면에 적용한다. 도감과 설정은 실제 동작하는 두 탭으로 제공하며 비활성 메뉴 자리표시는 두지 않는다. 플랫폼별 실제 화면·권한·입력·화면 배율 검증은 [Windows/macOS QA](../cross-platform-qa.md)에 기록한다.
