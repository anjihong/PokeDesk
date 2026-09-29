# 기여 및 이슈 연결

이 문서는 저장소에서 확인한 관례와 macOS 전환 작업의 검증 방법을 정리합니다.
루트 [AGENTS.md](../AGENTS.md)에 프로젝트 지침을 보관합니다. 기능 추가·변경·버그 수정 후에는
관련 `Docs/spec/` 문서를 구현에 맞게 갱신해야 합니다. 해당 문서가 없으면 새로 작성하고
[명세 개요](spec/01-overview.md)에 연결하세요. 아래 커밋 형식을 강제하는 자동화는 없습니다.

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

### main 업데이트 반영

- `origin/main`을 가져와 현재 Avalonia 브랜치에 병합한다. 기능을 수동 이식했더라도 병합 이력을 남겨 다음 업데이트의 비교 기준이 되도록 한다.
- 동작이 충돌하면 main의 게임 규칙·진화 데이터·기능 수정 사항을 우선한다. WPF 의존 구현은 Windows와 macOS가 함께 사용하는 Avalonia 코드로 옮긴다.
- 사용자가 요청한 Avalonia UI 개선은 새 main 변경과 충돌하지 않는 한 유지한다. Windows 전용 프로젝트나 화면을 다시 추가하지 않는다.
- 현재 반영 기준은 `78dc8cb`(시작 준비 후 메인 창 표시)다. 진화·이브이 8분기·미수집 분기 재육성·수동 테스트 모드가 포함된다. [진화 초안](issue-2-draft.md)은 main에서 가져온 설계 기록이며, 현재 동작은 [기능 명세](spec/01-overview.md)를 따른다.

### 빌드와 테스트

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
