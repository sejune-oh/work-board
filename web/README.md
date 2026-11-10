# web

업무 보드 화면입니다. Next.js 정적 사이트(`output: "export"`)로 빌드합니다.

```bash
pnpm install
pnpm dev     # http://localhost:3000
pnpm build   # out/ 에 정적 파일
```

## 환경 변수

모두 빌드할 때 들어가는 공개 값입니다. 비밀 값(service role key 등)을 여기에 넣지 마세요.

| 이름 | 없을 때 | 설명 |
| --- | --- | --- |
| `NEXT_PUBLIC_API_URL` | `http://localhost:5080` | C# API 주소 |
| `NEXT_PUBLIC_SUPABASE_URL` | 개발용 사용자 선택 | Supabase 프로젝트 주소 (`https://<ref>.supabase.co`) |
| `NEXT_PUBLIC_SUPABASE_ANON_KEY` | 개발용 사용자 선택 | Supabase 프로젝트의 anon(public) 키 |

로컬에서는 `web/.env.local`에 둡니다(`.env*`는 git에 올라가지 않습니다).

## 로그인

- Supabase 두 값이 **모두** 있으면 "GitHub으로 로그인"을 씁니다. 하나라도 없으면 지금처럼 오른쪽 위의
  개발용 사용자 선택(`X-Dev-User`)으로 동작합니다.
- 로그인은 PKCE 방식입니다. GitHub에서 돌아오면 `/auth/callback` 화면이 브라우저에서 코드를 세션으로 바꾸고,
  로그인을 시작한 화면으로 돌려보냅니다. Supabase 대시보드의 Authentication → URL Configuration에
  `<사이트 주소>/auth/callback`을 Redirect URL로 등록해야 합니다(로컬은 `http://localhost:3000/auth/callback`).
- API 요청에는 `Authorization: Bearer <access token>`을 붙이고, 회사를 고른 경우 `X-Tenant`도 붙입니다.
  토큰 갱신은 `@supabase/supabase-js`가 맡습니다.
- API 오류에 따라 보이는 화면: `401 unauthenticated` → 로그인 화면, `401 tenant_required` → 회사 고르기,
  `403 no_membership` → "아직 소속된 회사가 없습니다".
