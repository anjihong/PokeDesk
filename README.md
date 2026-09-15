# DeskPokemon

Windows 데스크톱 포켓몬 펫. 투명 오버레이 창에 애니메이션 스프라이트를 띄우고,
키 입력·마우스 클릭마다 바운스합니다. WPF / .NET 9, 외부 패키지 없음.

## 실행

```bash
dotnet run
```

- 좌클릭 드래그: 이동
- 우클릭 → 종료
- 포켓몬 아래 패널: 세대 탭(1~9) 선택 후 아이콘 클릭으로 포켓몬 교체 (1~1025번 전부)
- 레벨: 키 입력·마우스 클릭마다 경험치 +1, 레벨 × 10회마다 레벨업. 포켓몬별로 따로 기록되며
  `%LOCALAPPDATA%\DeskPokemon\settings.json` 에 저장

## 스프라이트 출처 및 라이선스

스프라이트는 앱에 포함되지 않으며, 첫 실행 시
[pagefaultgames/pokerogue-assets](https://github.com/pagefaultgames/pokerogue-assets) 의
`images/pokemon/` 및 `images/pokemon_icons_*` 에서 받아 `%LOCALAPPDATA%\DeskPokemon\sprites` 에 캐시합니다.

- 재생 방식(프레임 정렬, 10fps 루프)은 PokéRogue와 동일합니다.
- 해당 저장소 자산은 라이선스 가능한 범위에서 CC-BY-NC-SA-4.0이며, 원작 스프라이트는
  저장소 측이 공정 이용을 주장하고 재라이선스하지 않습니다. 자세한 내용은 저장소 README 및 `REUSE.toml` 참고.
- 작가 크레딧: https://github.com/pagefaultgames/pokerogue/blob/beta/CREDITS.md

비영리 팬 프로젝트입니다. Nintendo, Game Freak, Creatures Inc., The Pokémon Company와 무관합니다.
