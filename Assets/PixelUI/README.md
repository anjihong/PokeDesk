# 새로 그린 픽셀 UI 부품

`design/reference-ui-target.png`의 테두리 좌표와 빈 영역의 색을 기준으로 `design/draw_reference_pixel_ui.py`가 **빈 투명 캔버스에서 직접 그린** PNG 32개입니다. 원본 이미지의 픽셀을 자르거나 글자 자리를 덮어 쓰지 않습니다.

두 번째 재작업에서는 탭의 아래 모서리를 평평하게 고쳤고, 칸 테두리를 4px 줄였으며, 별도의 테두리 장식을 제거했습니다. 칸 내부와 크림색 패널의 색을 참고 화면에서 측정한 값에 맞춰 보정했습니다.

2026-10-07에는 참고 이미지와의 접합부를 맞춰 탭 아래의 안쪽 장식선과 목록 위쪽의 밝은 이중 경계를 제거했습니다. 목록은 하나의 짙은 3 DIP 선으로 도구막대와 연결합니다. 앱의 상세 패널은 8 DIP 모서리를 고정해 높이가 늘어나도 안쪽 하이라이트까지 늘어나지 않도록 합니다. `PixelSurface` 자체에서 최근접 보간과 레이아웃 반올림을 적용하므로 팝업도 같은 픽셀 경계를 사용합니다.

- 배경이 필요한 영역만 불투명합니다. 패널 모서리와 아이콘 바깥은 RGBA 투명입니다.
- 패널, 탭, 칸, 화살표, 체크박스, 배지는 독립 파일입니다. 패널 PNG에는 글자, 포켓몬, 아이콘이 들어 있지 않습니다.
- 픽셀 격자는 2 px입니다. 앱에서 정수배 크기로 표시하고 `NearestNeighbor` 보간을 사용하세요.
- 텍스트는 `Assets/Fonts/Galmuri11-Bold.ttf` 또는 `Galmuri11.ttf`로 앱에서 그립니다.
- 기본 배치 좌표와 크기는 `manifest.json`에 기록했습니다. 좌표는 1246×1263 참고 캔버스 기준이며, 실행 화면의 배치는 공통 XAML을 따릅니다. 메인 화면은 `tab_rail.png`를 그리지 않고 폭 344 DIP의 세 탭 행을 352 DIP 패널 양옆에서 4 DIP씩 안으로 배치합니다. 이는 탭의 색상 면을 패널의 크림색 면과 맞추기 위한 여백입니다. 이 PNG는 참고 조립용으로 보관합니다.

## 주요 레이어 순서

1. 포켓몬/알 스프라이트와 `pet_shadow.png`, `egg_shadow.png` (상단은 투명)
2. `exp_track.png`, `exp_fill_segment.png`, `exp_label_plate.png`, `egg_timer_plate.png`
3. `drawer_frame.png` (실행 화면에서 탭 뒤의 별도 받침은 표시하지 않음)
4. `tab_selected.png`, `tab_box.png`, `tab_settings.png`와 별도 `icon_*.png`, 앱의 텍스트
5. `toolbar_bg.png`, `generation_selector.png`, `arrow_*.png`, `checkbox_*.png`, 앱의 텍스트
6. `box_grid_bg.png` 위에 `slot_*.png` 6열×4행, 각 스프라이트/실루엣/물음표는 별도
7. `detail_base.png`, `detail_panel.png`, `portrait_frame.png`, `type_badge.png`, `number_badge.png` 위에 앱의 내용

## 미리보기

- `design/pixel-ui-redrawn-transparent.png`: 실제 투명 배경의 조립 이미지
- `design/pixel-ui-redrawn-checker.png`: 투명 영역을 확인하기 위한 체크무늬 미리보기
- `design/pixel-ui-assets-contact-sheet.png`: 분리된 PNG 32개를 한눈에 보는 목록
- `design/pixel-ui-reference-demo.png`: 별도 텍스트와 기존 앱 캐시의 포켓몬을 올린 조립 예시. 이 텍스트와 포켓몬은 UI 부품에 포함되지 않으며, 참고 그림의 포켓몬 포즈와는 다릅니다.

재생성: 프로젝트 루트에서 `python design/draw_reference_pixel_ui.py`
