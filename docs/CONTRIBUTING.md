# 기여 및 이슈 연결

이 문서는 저장소에서 확인한 관례와 macOS 전환 작업의 검증 방법을 정리합니다.
확인 시점에 별도 CONTRIBUTING, AGENTS, 이슈/PR 템플릿, 커밋 검사 설정은 없었고,
공개 PR 이력에서도 추가 규칙은 확인되지 않았습니다. 아래 형식을 강제하는 자동화는 없습니다.

## 커밋

기존 이력은 `feat`, `fix`와 한국어 설명을 사용합니다. 이슈를 연결한 실제 예시는
`feat(#3): 첫 실행 시 스타팅 포켓몬 선택`,
`feat(#4): 알 대기·부화 가능 상태에 루프 애니메이션 추가`입니다.
`feat: ...`, `fix: ...`, `fix : ...`처럼 이슈 번호 또는 공백 형식이 다른 커밋도 있습니다.

이번 작업을 커밋할 때는 기존 이슈 연결 형식을 따라 다음과 같이 작성할 수 있습니다.

```text
feat(#11): Windows와 macOS 화면을 Avalonia UI로 통합
fix(#11): macOS 입력 권한 거부 시 앱 종료 문제 수정
```

두 번째 메시지는 형식 예시이며, 해당 버그를 발견하거나 수정했다는 기록이 아닙니다.
관련 없는 변경은 따로 나누고 제목에는 실제 변경 내용을 적습니다.

## 이슈

macOS 지원은 기존 [#11 FEAT/macOS 지원](https://github.com/anjihong/PokeDesk/issues/11)에 연결합니다.
이번 작업은 이슈에 제안된 A안에 따라 Windows와 macOS가 같은 Avalonia 화면을 사용하도록 전환합니다.
따라서 Windows 빌드 역시 Avalonia이며, WPF UI를 별도로 유지하지 않습니다.
이슈 제목의 `FEAT/` 형식은 관찰된 사례이며 모든 이슈에 강제된 규칙으로 확인되지는 않았습니다.

검증 결과를 공유할 때는 빌드·자동 테스트와 실제 OS 화면 확인을 구분하고,
OS/CPU, 배율, 입력 권한 상태, 사용한 커밋, 재현 절차와 스크린샷을 적습니다.
작업 기록에서 `Refs #11`로 연관성을 표시할 수 있습니다.
`Closes #11`은 양쪽 OS에서 합의된 완료 조건을 확인한 뒤 사용합니다.

## 개발 확인

```bash
dotnet build DeskPokemon.csproj -c Debug
dotnet build DeskPokemon.csproj -c Release
dotnet test Tests/DeskPokemon.Tests/DeskPokemon.Tests.csproj -c Release
```

패키징 명령은 [README](../README.md), 실제 화면과 입력 확인 절차는
[Windows/macOS QA](cross-platform-qa.md)를 참고하세요.
GitHub Actions는 Windows x64, macOS ARM64, macOS x64에서 같은 소스를 빌드하고 테스트한 후
각 OS용 self-contained 실행 파일을 아티팩트로 만듭니다.
CI의 통과만으로 화면 비교, 시스템 권한, 실제 글로벌 입력 테스트가 완료되지는 않습니다.
