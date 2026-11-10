# work-board

팀 업무 보드. GitHub 이슈·PR·댓글과 회의 노트를 한 화면에 모아 "누가, 어느 프로젝트에서, 무엇을, 어디까지 했는지"를 보여 줍니다.

## 구성 (로컬 개발)

| 폴더 | 내용 | 주소 |
| --- | --- | --- |
| `db/` | Postgres 17 스키마(`001_schema.sql`)와 샘플 데이터(`002_seed.sql`) | `localhost:5434` |
| `server/` | C# (.NET 10) API | `http://localhost:5080` |
| `web/` | Next.js 정적 사이트 | `http://localhost:3000` |
| `docs/api-contract.md` | API 형식. 프론트와 백엔드는 이 문서를 기준으로 맞춥니다 | |

브라우저 → C# API → Postgres 순서로 연결합니다. 회사별 데이터 분리는 DB의 RLS가 맡습니다. API는 요청마다 `app.tenant_id`를 설정합니다.
로그인은 Supabase Auth의 GitHub 로그인입니다. 화면은 `NEXT_PUBLIC_SUPABASE_URL`과 `NEXT_PUBLIC_SUPABASE_ANON_KEY`가 있으면 "GitHub으로 로그인"을 쓰고,
없으면 화면 오른쪽 위의 **개발용 사용자 선택**이 로그인을 대신합니다(`X-Dev-User` 헤더). 화면 설정은 [web/README.md](web/README.md)에 있습니다.

## 실행

```bash
# 1. DB (Docker Desktop의 WSL 연동이 켜져 있어야 합니다)
docker compose up -d

# 2. API (프로젝트 폴더에서 실행해야 appsettings.Development.json을 읽습니다)
cd server/src/WorkBoard.Api && dotnet run

# 3. 화면
cd web && pnpm install && pnpm dev

# 테스트 (DB가 떠 있으면 RLS 통합 테스트까지 실행)
cd server && WORKBOARD_TEST_DB=1 dotnet test
```

DB 포트는 다른 프로젝트와 겹치지 않도록 5434를 씁니다.
`docker-compose.yml`, `db/init/001_schema.sql`, `appsettings.Development.json`의 비밀번호는 로컬 개발 전용 값입니다. 배포 환경에 그대로 쓰지 마세요. `X-Dev-User` 헤더 인증도 Development 환경에서만 켜집니다.

## 로그인 설정 (API)

로컬에서는 `server/src/WorkBoard.Api/appsettings.Local.json`에 넣습니다. 이 파일은 `.gitignore`에 있어 커밋되지 않고, Development 환경에서만 읽습니다. 양식은 `appsettings.Local.example.json`을 복사해 씁니다. 배포 환경에서는 같은 이름의 환경 변수(`Auth__Supabase__Url` 등)로 넣습니다.

| 이름 | 내용 |
| --- | --- |
| `Auth__Supabase__Url` | Supabase 프로젝트 주소. 발급자는 `{Url}/auth/v1`, 대상은 `authenticated` |
| `Auth__Supabase__ServiceRoleKey` | 서비스 롤 키. 사용자의 GitHub identity 조회(Admin API)에만 쓰고 브라우저에 내보내지 않음 |
| `Auth__Supabase__JwtSecret` | 선택. 공유 키(HS256) 프로젝트일 때만. 비우면 `{Url}/auth/v1/.well-known/jwks.json`의 공개 키로 검증 |

Development가 아닌 환경에서 `Url`이나 `ServiceRoleKey`가 없으면 API가 시작되지 않습니다. Development에서는 없어도 시작하고, 이때는 개발용 헤더 경로만 동작합니다.

```bash
cd server/src/WorkBoard.Api
cp appsettings.Local.example.json appsettings.Local.json   # 값을 채운 뒤 API 재시작
```

웹은 빌드 시점 공개 값 `NEXT_PUBLIC_SUPABASE_URL`, `NEXT_PUBLIC_SUPABASE_ANON_KEY`를 씁니다.

샘플 사용자: `haneul-dev`(acme-health 관리자), `other-org-dev`(globex-labs). 두 번째 사용자로 바꾸면 첫 번째 회사의 데이터가 하나도 보이지 않아야 합니다.

DB를 처음 상태로 되돌리기: `docker compose down -v && docker compose up -d`
