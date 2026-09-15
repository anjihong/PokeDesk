# DeskPokemon

Windows 데스크톱 포켓몬 펫. 투명 오버레이 창에 애니메이션 스프라이트를 띄우고,
키 입력·마우스 클릭마다 바운스합니다. WPF / .NET 9, 외부 패키지 없음.

## 실행

```bash
dotnet run
```

- 좌클릭 드래그: 이동
- 우클릭 → 종료
- 포켓몬 아래 도감 패널: 세대 탭(1~9) 선택 후 아이콘 클릭으로 포켓몬 교체. 아직 획득하지 못한
  포켓몬은 실루엣으로 표시되고 선택 불가. 처음에는 파이리만 보유. "보유만 보기"로 현재 세대의
  보유 포켓몬만 볼 수 있고, 아이콘에 마우스를 올리면 번호와 이름이 뜸
- 알: 앱 실행 시간 30분마다 알 1개. "부화" 버튼으로 까면 1~1025번 중 랜덤 포켓몬이 도감에 등록되고,
  이미 있는 포켓몬이면 그 포켓몬의 레벨 +1. 확률은 공식 포획률(capture rate)에 비례해 전설일수록 드묾.
  우클릭 메뉴 "알 즉시 획득(테스트)"로 바로 까볼 수 있음
- 레벨: 키 입력·마우스 클릭마다 경험치 +1, 레벨 × 10회마다 레벨업. 포켓몬별로 따로 기록되며
  `%LOCALAPPDATA%\DeskPokemon\settings.json` 에 저장

## 스프라이트 출처 및 라이선스

스프라이트는 앱에 포함되지 않으며, 첫 실행 시
[pagefaultgames/pokerogue-assets](https://github.com/pagefaultgames/pokerogue-assets) 의
`images/pokemon/`, `images/pokemon_icons_*`, `images/egg/` 에서 받아 `%LOCALAPPDATA%\DeskPokemon\sprites` 에 캐시합니다.
포획률 수치는 [PokeAPI](https://github.com/PokeAPI/pokeapi) `pokemon_species.csv` 에서 가져와 코드에 포함했습니다.

- 재생 방식(프레임 정렬, 10fps 루프)은 PokéRogue와 동일합니다.
- 해당 저장소 자산은 라이선스 가능한 범위에서 CC-BY-NC-SA-4.0이며, 원작 스프라이트는
  저장소 측이 공정 이용을 주장하고 재라이선스하지 않습니다. 자세한 내용은 저장소 README 및 `REUSE.toml` 참고.
- 작가 크레딧: https://github.com/pagefaultgames/pokerogue/blob/beta/CREDITS.md

비영리 팬 프로젝트입니다. Nintendo, Game Freak, Creatures Inc., The Pokémon Company와 무관합니다.
