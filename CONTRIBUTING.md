# 기여 규칙

## 브랜치

상시 브랜치는 `main`과 `dev` 두 개뿐입니다.

| 브랜치 | 용도 | 바뀌는 방법 |
| --- | --- | --- |
| `main` | 릴리스된 코드 | 릴리스 PR로만 |
| `dev` | 다음 릴리스에 들어갈 코드 | 작업 브랜치 PR로만 |

저장소 ruleset `protect main and dev`가 두 브랜치의 직접 push, 강제 push, 삭제를 막고 PR 병합 방식을 merge commit으로 제한합니다. 저장소 설정에서도 squash와 rebase 병합은 꺼져 있습니다.

작업 브랜치는 역할에 따라 네 가지이고, 모두 `dev`에서 따서 `dev`로 PR을 보냅니다.

| 접두어 | 쓰는 경우 |
| --- | --- |
| `feat/` | 새 기능 |
| `fix/` | 결함 수정 |
| `hotfix/` | 급한 결함 수정. 흐름은 `fix/`와 같고 릴리스 PR을 바로 병합해 빨리 내보냄. 이때 `dev`에 먼저 들어가 있던 다른 변경도 함께 릴리스되므로, 병합 전에 릴리스 PR의 "포함된 변경" 목록을 확인 |
| `chore/` | CI, 설정, 문서, 테스트 정리 |

이름은 `<접두어>/<이슈 번호>-<짧은 설명>` 입니다. 예: `feat/7-polling-sync`

## 릴리스

1. 작업 브랜치 PR이 `dev`에 병합됩니다.
2. `.github/workflows/release-pr.yml`이 `dev` → `main` 릴리스 PR을 자동으로 열거나, 이미 열려 있으면 본문(포함된 PR 목록)을 갱신합니다.
3. 릴리스 PR을 병합하면 `main`이 바뀌고, 그것이 릴리스입니다.

릴리스 PR은 언제나 하나만 열려 있습니다.

## 이슈와 PR

- 이슈를 먼저 만들고, 브랜치 이름에 이슈 번호를 넣습니다.
- PR 제목은 conventional 형식(`feat: ...`, `fix(api): ...`), 본문에 `Closes #N`, 이슈와 같은 라벨을 붙입니다.
- PR은 초안(draft)이 아니라 바로 리뷰 가능한 상태로 엽니다.
- 작업 브랜치 PR과 릴리스 PR 모두 merge commit으로 병합합니다. 릴리스 PR을 squash나 rebase로 병합하면 `dev`와 `main`의 기록이 갈라져 다음 릴리스에서 충돌이 납니다. 릴리스 PR 본문도 병합 커밋에서 PR 번호를 읽습니다.
- 릴리스 PR은 `GITHUB_TOKEN`으로 만들어져서, 그 PR에서는 다른 `pull_request` 워크플로가 돌지 않습니다. CI를 필수 검사로 걸 때는 GitHub App 토큰이나 개인 토큰으로 바꿔야 합니다.
- `dev`는 기본 브랜치가 아니라서 `Closes #N`이 이슈를 자동으로 닫지 않습니다. `dev`에 병합한 뒤 이슈를 직접 닫습니다.

## 공개 저장소 주의

- 실제 회사·고객 이름, 실데이터, 비밀값을 커밋하지 않습니다. 샘플 데이터는 가상 이름을 씁니다.
- 커밋 이메일은 GitHub noreply 주소를 씁니다.
- 로컬 개발용 비밀번호는 로컬 전용입니다. 배포 설정에 재사용하지 않습니다.

## 확인 명령

```bash
docker compose -p workboard up -d --wait
cd server && WORKBOARD_TEST_DB=1 dotnet test
cd web && pnpm lint && pnpm build
```
