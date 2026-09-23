# 저장 데이터

저장 위치: `%LOCALAPPDATA%\DeskPokemon\settings.json`

| 필드 | 의미 |
| --- | --- |
| `SchemaVersion` | 저장 형식 버전. 현재 값 `2` |
| `StarterDex` | 최초 선택한 스타팅의 도감 번호 |
| `SelectedDex` | 현재 화면에 표시할 포켓몬의 도감 번호 |
| `SelectedShiny` | 현재 선택이 이로치인지 여부 |
| `Progress`, `Owned` | 일반 포켓몬의 도감 번호별 성장 상태와 보유 목록 |
| `ShinyProgress`, `ShinyOwned` | 이로치 포켓몬의 도감 번호별 성장 상태와 보유 목록 |
| `Eggs` | 보유 알 수. 정상 흐름에서는 `0` 또는 `1` |
| `EggSeconds` | 다음 알까지 누적한 실행 시간(초) |
| `PendingEgg` | 확정된 다음 알의 `Kind`, `Dex`, `IsShiny` |

`UnlockAll`과 `RemainingEggSeconds`는 계산·개발용 속성이며 JSON에 저장하지 않는다.

- 구버전은 마이그레이션 전 원본을 `settings.json.schema{버전}.bak`으로 한 번 백업한다.
- 스키마 1 저장은 일반 보유·성장 데이터를 유지하고 이로치 데이터를 빈 상태로 추가한다. 기존 준비 알은 커먼 알로 변환하고 결과를 한 번 확정한다.
- 스타팅 번호가 없으면 파이리로 보정한다. 선택 종이 해당 색상에서 미보유면 일반 스타팅으로 되돌린다.
- 저장은 임시 파일 작성 후 원본 교체 방식이다. 부화 저장 실패 시 메모리 상태도 복구한다.
- 손상된 JSON은 새 게임 흐름으로 시작한다. 현재 앱보다 새 스키마는 덮어쓰지 않고 오류와 함께 종료한다.
