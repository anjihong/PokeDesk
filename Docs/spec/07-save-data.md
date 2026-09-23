# 저장 데이터

저장 위치: `%LOCALAPPDATA%\DeskPokemon\settings.json`

| 필드 | 의미 |
| --- | --- |
| `SchemaVersion` | 저장 형식 버전. 현재 값 `1` |
| `StarterDex` | 최초 선택한 스타팅의 도감 번호 |
| `SelectedDex` | 현재 화면에 표시할 포켓몬의 도감 번호 |
| `Progress` | 도감 번호별 `Level`, `Exp` |
| `Owned` | 보유한 포켓몬의 도감 번호 집합 |
| `Eggs` | 보유 알 수. 정상 흐름에서는 `0` 또는 `1` |
| `EggSeconds` | 다음 알까지 누적한 실행 시간(초) |

`UnlockAll`과 `RemainingEggSeconds`는 계산·개발용 속성이며 JSON에 저장하지 않는다. 구버전 저장 파일은 스타팅을 파이리로 보정하고 보유 목록을 스타팅 1종으로 초기화하되 기존 레벨 기록은 유지한다. 마이그레이션 후에는 스타팅을 보유 목록에 넣고, 선택 종이 미보유라면 스타팅으로 되돌린다. 저장 파일이 손상되면 새 게임 흐름으로 시작한다.
