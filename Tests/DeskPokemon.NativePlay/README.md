# macOS 실제 창 플레이 확인

저장소 루트에서 실행한다. 실행 중 실제 마우스 커서와 키 이벤트를 사용하므로 완료될 때까지 다른 창을 조작하지 않는다.

```sh
dotnet run --project Tests/DeskPokemon.NativePlay/DeskPokemon.NativePlay.csproj -c Release
```

- headless 대신 Avalonia macOS 창을 연다. CGEvent로 버튼·도감·알을 클릭하고 키 자동 반복을 보낸다.
- 메모리 전용 `Settings.NewPreview`를 사용한다. 실제 플레이어 저장 파일은 읽어 해시를 비교할 뿐, 테스트 진행을 저장하지 않는다.
- 자동 실행 체크박스는 가짜 서비스에 연결한다. 실제 LaunchAgent는 변경하지 않는다.
- 실제 포켓몬 이미지와 아이콘은 기존 다운로드/캐시 경로로 불러온다. 최초 실행에는 네트워크가 필요하다.
- 경험치 툴팁, 키 반복 제외, 반전·배율, 전체/보유/이로치 도감, 일반·이로치 2단계 진화와 빛·실루엣·색상 공개 연출, 부화와 서랍 원위치 복귀를 확인한다.
- 실제 파이리·리자드·리자몽 이미지가 같은 110 DIP 몸체 높이를 사용하고, 현재 프레임의 발이 그림자의 타원 중심에 놓이는지도 검사한다.
- 진화 조건과 준비된 알은 미리보기 데이터에서 설정한다. 30분이나 진화 레벨까지 실제로 기다리는 테스트가 아니다.
- 성공하면 `NATIVE_PLAY_PASS`를 출력하고 종료한다. 실패는 종료 코드 1과 오류를 남긴다.
- `artifacts/native-play/`에 실제 창의 Avalonia 렌더 결과 PNG를 저장한다. 이는 OS 합성 화면 캡처나 Windows 실기 검증을 대신하지 않는다.

macOS의 입력 권한과 현재 사용자 마우스 조작은 결과에 영향을 줄 수 있다. Windows 및 Mac Intel의 공통 UI 비교는 `desktop.yml`의 별도 headless CI에서 수행한다.
