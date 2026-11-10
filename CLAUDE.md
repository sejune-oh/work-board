# work-board 작업 규칙 (AI 세션용)

브랜치, 릴리스, 이슈·PR 규칙은 [CONTRIBUTING.md](CONTRIBUTING.md)를 따릅니다. 요약:

- 상시 브랜치는 `main`, `dev` 두 개. 둘 다 직접 push하지 않습니다.
- 작업은 `feat/`, `fix/`, `hotfix/`, `chore/` 브랜치에서 하고 `dev`로 PR을 보냅니다.
- `dev` → `main` 릴리스 PR은 `release-pr` 워크플로가 만듭니다. 손으로 만들지 않습니다.
- 모든 PR은 merge commit으로 병합합니다(squash·rebase는 저장소 설정과 ruleset으로 막혀 있음).

추가로 지킬 것:

- 원격 작업(push, 이슈·PR 생성, 댓글, 병합, 저장소 설정 변경)은 매번 사용자에게 먼저 묻습니다.
- 이 저장소는 GitHub 계정 `sejune-oh` 소유입니다. 공유 gh 활성 계정이 다를 수 있으니 `GH_TOKEN="$(gh auth token -u sejune-oh)"`로 실행합니다.
- 공개 저장소입니다. 회사 이름, 실데이터, 회사 이메일을 넣지 않습니다.
- 코드 주석에는 읽는 사람이 알아야 할 것만 씁니다. 변경 이유는 커밋 메시지에 씁니다.
- API를 바꾸면 `docs/api-contract.md`와 `web/src/lib/types.ts`를 함께 고칩니다.
- API는 `server/src/WorkBoard.Api` 폴더에서 `dotnet run`으로 실행해야 개발 설정을 읽습니다. 로컬 DB 포트는 5434입니다.
