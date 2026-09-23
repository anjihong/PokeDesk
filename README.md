# DeskPokemon

Windows 데스크톱 포켓몬 펫. 투명 오버레이에서 포켓몬을 키우고, 알을 부화해 일반·이로치 도감을 채운다.
WPF / .NET 9 기반이며 외부 NuGet 패키지는 사용하지 않는다.

## 실행

```bash
dotnet run
```

- 좌클릭 드래그: 이동
- 우클릭 → 종료
- 첫 실행: 1~9세대 풀·불꽃·물 스타팅 후보 3종 중 1종 선택
- 도감: 세대별 목록, 미보유 실루엣, 보유만 보기, 일반·이로치 도감 전환
- 알: 실행 시간 30분마다 1개 지급. 커먼·레어·에픽·레전더리·이로치알에 따라 부화 풀이 달라짐
- 성장: 전역 키·마우스 입력 1회당 경험치 +1. 다음 레벨 요구 경험치는 `현재 레벨 × 30`
- 저장: 일반·이로치의 보유 목록, 레벨, 경험치와 확정된 다음 알을 `%LOCALAPPDATA%\DeskPokemon\settings.json`에 저장

전체 동작과 이슈별 구현 상태는 [기능 명세](Docs/spec/01-overview.md)를 참고한다.

## 스프라이트 출처 및 라이선스

스프라이트는 앱에 포함되지 않으며, 첫 실행 시
[pagefaultgames/pokerogue-assets](https://github.com/pagefaultgames/pokerogue-assets) 의
`images/pokemon/`, `images/pokemon/exp/`, 각 경로의 `shiny/`, `images/pokemon_icons_*`, `images/egg/`에서 받아 `%LOCALAPPDATA%\DeskPokemon\sprites`에 캐시합니다.
PokéRogue 쪽 스프라이트가 정지(1프레임)인 일부 종은
[PokeAPI/sprites](https://github.com/PokeAPI/sprites) 의 `versions/generation-v/black-white/animated/` GIF를 대신 사용합니다.
포획률 수치는 [PokeAPI](https://github.com/PokeAPI/pokeapi) `pokemon_species.csv`에서 가져와 코드에 포함했지만 현재 부화 추첨에는 사용하지 않습니다.

- 재생 방식(프레임 정렬, 10fps 루프)은 PokéRogue와 동일합니다.
- 해당 저장소 자산은 라이선스 가능한 범위에서 CC-BY-NC-SA-4.0이며, 원작 스프라이트는
  저장소 측이 공정 이용을 주장하고 재라이선스하지 않습니다. 자세한 내용은 저장소 README 및 `REUSE.toml` 참고.
- 작가 크레딧: https://github.com/pagefaultgames/pokerogue/blob/beta/CREDITS.md
- PokeAPI GIF 중 도감 번호 650 초과는 비공식 스프라이트로, Smogon 커뮤니티가 제작한 BW 스타일 스프라이트입니다.
  작가 목록과 출처는 [PokeAPI/sprites README](https://github.com/PokeAPI/sprites) 참고.

비영리 팬 프로젝트입니다. Nintendo, Game Freak, Creatures Inc., The Pokémon Company와 무관합니다.
