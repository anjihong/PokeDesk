# 새로 그린 픽셀 UI 부품

`design/reference-ui-target.png`의 테두리 좌표와 빈 영역의 색을 기준으로 `design/draw_reference_pixel_ui.py`가 **빈 투명 캔버스에서 직접 그린** PNG 32개입니다. 원본 이미지의 픽셀을 자르거나 글자 자리를 덮어 쓰지 않습니다.

두 번째 재작업에서는 탭의 아래 모서리를 평평하게 고쳤고, 칸 테두리를 4px 줄였으며, 별도의 테두리 장식을 제거했습니다. 칸 내부와 크림색 패널의 색을 참고 화면에서 측정한 값에 맞춰 보정했습니다.

- 배경이 필요한 영역만 불투명합니다. 패널 모서리와 아이콘 바깥은 RGBA 투명입니다.
- 패널, 탭, 칸, 화살표, 체크박스, 배지는 독립 파일입니다. 패널 PNG에는 글자, 포켓몬, 아이콘이 들어 있지 않습니다.
- 픽셀 격자는 2 px입니다. 앱에서 정수배 크기로 표시하고 `NearestNeighbor` 보간을 사용하세요.
- 텍스트는 `Assets/Fonts/Galmuri11-Bold.ttf` 또는 `Galmuri11.ttf`로 앱에서 그립니다.
- 기본 배치 좌표와 크기는 `manifest.json`에 기록했습니다. 좌표는 1246×1263 참고 캔버스 기준입니다.

## 주요 레이어 순서

1. 포켓몬/알 스프라이트와 `pet_shadow.png`, `egg_shadow.png` (상단은 투명)
2. `exp_track.png`, `exp_fill_segment.png`, `exp_label_plate.png`, `egg_timer_plate.png`
3. `drawer_frame.png`, `tab_rail.png`
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
