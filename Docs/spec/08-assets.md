# 스프라이트와 데이터 자산

- 포켓몬 본체, 아이콘, 알 이미지는 앱에 포함하지 않는다. 필요할 때 `pagefaultgames/pokerogue-assets`의 이미지와 아틀라스 JSON을 다운로드해 `%LOCALAPPDATA%\DeskPokemon\sprites`에 캐시한다.
- 본체 스프라이트는 `pokemon/exp/`를 먼저 찾고, 없으면 `pokemon/`을 사용한다. 정지 프레임만 있는 경우 PokeAPI의 5세대 BW 스타일 GIF를 시도한다. GIF도 없으면 기존 정지 프레임을 사용한다.
- GIF는 프레임 합성과 잘라내기를 거쳐 앱의 10fps 재생 간격에 맞춘다.
- 폼 이름이 파일 키에 필요한 종은 `PokemonForms`의 기본 폼 매핑을 사용한다. 도감 아이콘은 기본 폼만 채택하고 다른 폼·색이 다른 개체는 표시하지 않는다.
- 한글 이름은 `PokemonNames` 배열에, 부화 후보는 `BaseSpecies`에, 포획률은 `CaptureRates`에 내장되어 있다.
- `Assets/PixelUI`에는 새 픽셀 UI 부품과 Galmuri 폰트가 준비되어 있고 프로젝트 리소스로 포함된다. **현재 메인/스타팅 화면 XAML은 이 부품을 사용하지 않는다.** 현재 화면은 WPF 도형·텍스트·스타일로 구성되어 있다. `design/`의 시안과 생성 스크립트도 런타임 기능이 아니다.

에셋 출처 및 라이선스 상세는 프로젝트 루트의 `README.md`를 따른다.
