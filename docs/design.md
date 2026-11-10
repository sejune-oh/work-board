# 디자인 기준

업무 보드의 화면은 모두 이 문서를 따릅니다. 새 화면, 새 컴포넌트, 기존 화면 손질 모두 해당합니다.
기준을 바꿀 때는 이슈를 먼저 만들고 이 문서와 `web/src/app/globals.css`를 같은 PR에서 고칩니다.
이 문서를 처음 추가한 PR은 문서만 담았고, 화면 코드(`globals.css`와 각 화면)는 Overview 구현 PR(#24)에서 반영합니다.

## 1. 방향

**목적.** 10명 안팎의 팀이 "누가, 어느 프로젝트에서, 무엇을, 어디까지" 하는지 한눈에 보게 합니다.
보는 사람은 개발자만이 아닙니다. 기획, 디자인, 운영 담당도 매일 아침 한 번 열어 봅니다.

**톤: 아침 현황지(現況紙).** 따뜻한 종이 위에 먹색 글자, 숫자는 장부처럼 줄 맞춰 정렬합니다.
화려한 대시보드가 아니라 잘 조판된 한 장짜리 신문 1면에 가깝습니다. 장식은 빼고, 위계는 글자 크기와
여백과 가는 줄(괘선)로 만듭니다. 카드 그림자, 그라디언트, 둥근 알약 모양을 남발하지 않습니다.

**기억에 남을 한 가지: 인주(印朱) 색은 "여기를 보세요"에만 씁니다.**
화면 전체가 먹색과 종이색으로 차분하고, 주의가 필요한 곳(긴급, 오래 멈춘 일, 담당자 없음,
리뷰 대기)에만 도장 찍은 듯한 주홍색이 나옵니다. 그래서 주홍이 보이면 그곳이 할 일입니다.
버튼, 링크, 선택 상태에는 인주 색을 쓰지 않습니다. 강조색이 곧 경보색이라 의미가 흐려지지 않습니다.

**한 문장 현황.** 개요 화면 맨 위에는 숫자 묶음 대신 사람이 쓰는 문장 하나를 크게 둡니다.
예: "리뷰를 기다리는 PR이 3건 있고, 긴급 2건 중 1건은 아직 시작 전입니다."
가장 중요한 사실을 쉬운 한국어로 먼저 말하는 것이 이 제품의 목소리입니다.

**왜 이 방향인가.**

- 한눈에 보기가 최우선이라, 색이 많을수록 눈이 갈 곳을 잃습니다. 색을 줄이고 주의 색 하나만 남기면
  10초 안에 "손봐야 할 곳"이 보입니다.
- 이슈, PR, 댓글, 회의 노트가 섞인 정보라 표(장부)와 목록이 주인공입니다. 장부 조판이 이 정보에 맞습니다.
- 지라 같은 무거운 도구와 거리를 두고 싶은 팀입니다. 종이와 문장의 톤이 "누구나 쓸 수 있는" 인상을 줍니다.

## 2. 글꼴

모두 Google Fonts에서 받습니다. 한글을 지원하는 글꼴만 씁니다.

| 역할 | 글꼴 | 굵기 | 쓰는 곳 |
| --- | --- | --- | --- |
| 표제 (display) | **Hahmlet** | 500, 600, 700 | 화면 제목, 한 문장 현황, 큰 숫자 |
| 본문 (body) | **IBM Plex Sans KR** | 400, 500, 600 | 모든 본문, 버튼, 표 |
| 고정폭 (mono) | **IBM Plex Mono** | 400, 500 | 저장소 이름과 번호(`booking-api #102`), 날짜, 표 안 숫자 |

```html
<link rel="preconnect" href="https://fonts.googleapis.com" />
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin />
<link href="https://fonts.googleapis.com/css2?family=Hahmlet:wght@500;600;700&family=IBM+Plex+Sans+KR:wght@400;500;600&family=IBM+Plex+Mono:wght@400;500&display=swap" rel="stylesheet" />
```

Next.js에서는 `next/font/google`로 같은 세 글꼴을 불러와 아래 변수에 연결합니다.
IBM Plex Mono에는 한글이 없으므로 mono 스택 두 번째에 IBM Plex Sans KR을 둡니다.

| 단계 | 크기 / 줄 간격 | 글꼴 | 예 |
| --- | --- | --- | --- |
| `display` | 36px / 1.25 (휴대폰 26px) | Hahmlet 600 | 한 문장 현황 |
| `title` | 24px / 1.3 | Hahmlet 600 | 화면 제목 |
| `heading` | 17px / 1.4 | Hahmlet 600 | 구역 제목 |
| `body` | 15px / 1.6 | Plex Sans KR 400 | 본문 |
| `ui` | 14px / 1.45 | Plex Sans KR 500 | 카드 제목, 버튼, 표 |
| `meta` | 12.5px / 1.4 | Plex Sans KR 400 / Mono | 저장소·번호, 날짜, 보조 설명 |
| `eyebrow` | 11.5px / 1.2, 자간 0.08em | Plex Sans KR 600 | 구역 위 작은 머리말 |

숫자는 항상 `font-variant-numeric: tabular-nums`로 자리를 맞춥니다. 한글 본문은 `word-break: keep-all`.

## 3. 색

색은 세 무리입니다. **종이와 먹**(바탕), **상태**(5색), **인주**(주의). 프로젝트 색(DB의 `projects.color`)은
8px 사각 표식에만 쓰고 넓은 면에 칠하지 않습니다.

| 토큰 | 라이트 | 다크 | 쓰는 곳 |
| --- | --- | --- | --- |
| `paper` | `#F5F1E8` | `#16140F` | 화면 바탕 |
| `surface` | `#FBF9F4` | `#1E1C16` | 카드, 패널, 표 |
| `sunken` | `#ECE6D9` | `#100F0B` | 열 바탕, 입력창, 막대 바닥 |
| `ink` | `#1D1B16` | `#EEE8DA` | 본문 글자, 기본 버튼 바탕 |
| `ink-soft` | `#3F3B32` | `#CFC8B7` | 부제, 표 본문 |
| `muted` | `#6B6557` | `#968F80` | 보조 글자, 날짜 |
| `rule` | `#DCD4C3` | `#2F2C24` | 괘선, 테두리 |
| `rule-strong` | `#1D1B16` | `#EEE8DA` | 구역 머리 굵은 줄 |
| `focus` | `#2B4FA8` | `#8FA8F0` | 키보드 초점 테두리 |
| `accent` (인주) | `#B8361A` | `#FF6B42` | 주의 표시 전용 |
| `accent-wash` | `#F9E1D6` | `#3A1C12` | 주의 표시 바탕 |
| `status-todo` | `#9A9384` | `#8C8576` | 할 일 |
| `status-progress` | `#2F67B2` | `#6FA1EA` | 진행 중 |
| `status-review` | `#B7821A` | `#E2B04A` | 리뷰 중 |
| `status-done` | `#3F7F5A` | `#6DBB8B` | 완료 |
| `status-cancel` | `#B9B2A3` | `#5A554A` | 취소(예정) |

상태 색 규칙:

- 상태 색은 점(8px 원), 막대 조각, 얇은 왼쪽 줄에만 씁니다. 글자 색이나 넓은 배경으로 쓰지 않습니다.
- 상태는 언제나 **점 + 한국어 이름**을 같이 씁니다. 색만으로 구분하지 않습니다(색각 이상 대비).
- 완료는 녹색이지만 "축하" 색이 아닙니다. 완료 항목은 기본적으로 `muted` 글자로 한발 물러섭니다.
- 취소는 회색 점 + 제목 가운데 줄. 별도 열을 만들지 않고 완료 열 아래 접힌 묶음으로 둡니다.
  지금 스키마에는 취소를 나타낼 값(`state_reason`)이 없어서, 상태 규칙 보완(#9) 이후에 실제로 나타납니다.
- 라이트 모드의 `muted`와 `accent`는 `paper`, `surface`, `sunken`, `accent-wash` 위에서 글자 대비 4.5:1 이상이 되도록 정한 값입니다. 바꿀 때는 대비를 다시 계산합니다.

인주 규칙:

- 인주 색이 화면에 나오는 곳은 주의 표시뿐입니다(4장의 "주의 표시").
- 한 화면에서 인주가 칠해진 면은 2~3곳을 넘기지 않습니다. 넘치면 목록으로 모아 한 곳에 둡니다.
- 버튼은 먹색(`ink` 바탕, `paper` 글자), 링크는 `ink` 글자 + 밑줄입니다.

## 4. 간격, 모서리, 그림자, 움직임

- 간격은 4px 단위: 4, 8, 12, 16, 24, 32, 48, 64. 구역과 구역 사이는 48(휴대폰 32).
- 모서리: 표식·점 2px, 카드·입력창 6px, 패널 10px. 알약(완전히 둥근) 모양은 사람 머리글자 원에만.
- 그림자는 두 단계뿐입니다. `shadow-lift`(카드 hover, 1px 아래로 얕게)와 `shadow-panel`(이슈 패널,
  화면 위로 떠 있는 것). 평소 카드는 그림자 없이 테두리(`rule`)만.
- 구역 머리는 굵은 줄(`rule-strong`, 2px) 위에 `eyebrow` + `heading`. 신문 단 머리처럼 보이게.
- 바탕에 아주 옅은 종이 결(SVG 노이즈, 불투명도 3% 이하)을 깔 수 있습니다. 다크에서는 끕니다.

움직임:

- 시간: 빠름 120ms(hover, 눌림), 보통 200ms(패널 열림, 펼침), 첫 화면 등장 360ms.
- 곡선: `cubic-bezier(0.2, 0.7, 0.2, 1)` 하나만.
- 첫 화면은 구역별로 40ms씩 늦춰 아래로 2~6px 떠오르며 나타납니다. 한 번뿐이고, 데이터가 다시 불러와질 때는 움직이지 않습니다.
- 막대(진행률, 부하)는 처음 그릴 때만 왼쪽에서 채워집니다.
- `prefers-reduced-motion: reduce`이면 모든 움직임을 끕니다.
- 깜빡임, 무한 반복 움직임, 튀는 효과는 쓰지 않습니다. 긴급도 깜빡이지 않습니다.

## 5. 컴포넌트 규칙

### 이슈 카드

```
┃ booking-api #102 · 작업            긴급
┃ 예약 조회 API 페이지네이션
┃ 이민준          PR 1 · 댓글 2 · 오늘
```

- 왼쪽 3px 줄이 상태 색입니다. 상태 점을 따로 두지 않습니다(열 제목이 이미 상태를 말하는 보드에서는 줄도 생략 가능).
- 첫 줄 `meta` mono: 저장소 #번호, 종류(큰 목표/기능/작업/버그). 제목은 `ui` 굵기 500, 두 줄까지.
- 마지막 줄: 담당자(없으면 주의 표시), 오른쪽에 PR 수, 댓글 수, 마지막 활동.
- 우선순위는 설정에서 켠 경우에만, 긴급만 인주 표시로 보여 줍니다. 높음은 `ink-soft` 글자 "높음"만.
- hover: 테두리 `ink-soft`, `shadow-lift`. 클릭하면 이슈 패널(`?issue=`)이 열립니다. 카드 안에 다른 링크를 두지 않습니다.

### 상태 표시

- 기본형: 8px 점 + 상태 이름(`meta`, `ink-soft`). 예: `● 리뷰 중`.
- 요약형: 상태 막대(아래 "진행률 막대") 또는 `할 일 6 · 진행 중 3 · 리뷰 중 3` 같은 점 붙은 숫자 줄.
- 상태 이름은 할 일 / 진행 중 / 리뷰 중 / 완료 / 취소. 다른 말로 바꾸지 않습니다.

### 주의 표시

인주 색을 쓰는 유일한 요소입니다. 모양은 **도장**: 인주 글자 + 1px 인주 테두리 + 2px 모서리, 굵기 600.

| 종류 | 조건 | 표시 |
| --- | --- | --- |
| 긴급 | 우선순위 `urgent`이고 완료가 아님 | 도장 `긴급` (인주 바탕 `accent-wash`) |
| 멈춤 | 열린 이슈의 마지막 활동이 7일 이상 전 | 카드 전체 불투명도 0.55 + 날짜 자리에 도장 `12일째 멈춤` |
| 담당자 없음 | 열린 이슈에 담당자가 0명 | 담당자 자리에 점선 원 + 인주 글자 `담당자 없음` |
| 리뷰 대기 | 리뷰 요청된 열린 PR이 24시간 이상 | 도장 `리뷰 대기 1일` |

멈춘 카드는 흐려지지만 도장은 흐려지지 않습니다(도장은 불투명도 1로 되돌립니다). 흐려짐은 "잊혀 가는 중",
도장은 "그래서 봐야 함"을 뜻합니다.

### 사람별 부하 막대

- 한 줄에 한 사람: 머리글자 원, 이름, 막대, 숫자.
- 막대는 그 사람의 열린 일을 상태별로 쌓은 것입니다: 진행 중 + 리뷰 중(진한 색), 할 일(옅은 색).
- 막대 길이의 기준은 팀에서 열린 일이 가장 많은 사람입니다(그 사람이 100%). 절대 기준을 두지 않습니다.
- "몰림" 도장: 진행 중 + 리뷰 중이 4건 이상이고 팀 중앙값의 2배 이상일 때만. 매번 1등을 표시하지 않습니다.
- 맡은 일이 0건이면 막대 대신 `muted` 글자 "맡은 일 없음".

### 진행률 막대

- 높이 6px, 바닥 `sunken`, 모서리 2px. 완료 / 리뷰 중 / 진행 중 / 할 일 순서로 왼쪽부터 쌓습니다.
- 옆에 숫자를 항상 함께: `완료 1 / 6 · 17%`. 막대만 두지 않습니다.
- 퍼센트는 정수. 전체가 0건이면 막대 대신 "아직 일이 없습니다".

### 빈 화면, 시작 화면, 오류

- **빈 화면**: 한 문장으로 사실을 말하고, 그게 좋은 소식이면 그렇다고 말합니다.
  예: "담당자 없는 일이 없습니다." "7일 넘게 멈춘 일이 없습니다." 일러스트는 넣지 않고 `muted` 글자 + 가는 점선 상자.
- **시작 화면**: 한 문장 현황과 같은 표제 크기로 "누가, 어느 프로젝트에서, 무엇을, 어디까지."를 보여 주고,
  그 아래 사용자 선택 하나만. 실행 방법은 접어 둡니다.
- **오류**: 인주 색을 쓰지 않습니다(인주는 "일"에 대한 주의). `ink` 글자 + `rule-strong` 왼쪽 줄 + 해결 방법 한 줄.
  예: "API 서버에 연결할 수 없습니다. server 폴더에서 API를 실행했는지 확인하세요."
- **불러오는 중**: 회색 빈 줄(skeleton) 대신 구역 머리는 바로 그리고 내용 자리에 `muted` "불러오는 중…".

### 말

- 지라 용어 금지: Epic → 큰 목표, Task → 작업, Backlog → 대기 목록, Sprint → 주기, Story point → 크기.
- 버튼은 동사로 끝냅니다: "이슈로 만들기", "보드에서 보기".
- 숫자 뒤에 단위: "3건", "12일째", "2일 전".

## 6. 토큰 (`web/src/app/globals.css`에 그대로 넣기)

Tailwind 4의 `@theme inline`으로 연결하면 `bg-paper`, `text-ink`, `border-rule`, `bg-status-review`,
`text-accent`, `font-display`, `shadow-lift` 같은 클래스가 생깁니다.

```css
@import "tailwindcss";

:root {
  --wb-paper: #f5f1e8;
  --wb-surface: #fbf9f4;
  --wb-sunken: #ece6d9;
  --wb-ink: #1d1b16;
  --wb-ink-soft: #3f3b32;
  --wb-muted: #6b6557;
  --wb-rule: #dcd4c3;
  --wb-rule-strong: #1d1b16;
  --wb-focus: #2b4fa8;
  --wb-accent: #b8361a;
  --wb-accent-wash: #f9e1d6;
  --wb-status-todo: #9a9384;
  --wb-status-progress: #2f67b2;
  --wb-status-review: #b7821a;
  --wb-status-done: #3f7f5a;
  --wb-status-cancel: #b9b2a3;
  --wb-shadow-lift: 0 1px 0 rgb(29 27 22 / 0.06), 0 4px 12px -6px rgb(29 27 22 / 0.18);
  --wb-shadow-panel: 0 0 0 1px rgb(29 27 22 / 0.08), 0 24px 48px -16px rgb(29 27 22 / 0.28);
  color-scheme: light;
}

/* 다크 값은 아래 블록과 :root[data-theme="dark"] 블록 두 곳에 같게 둡니다.
   화면 오른쪽 위 "밝게 · 어둡게" 버튼이 <html data-theme>를 고정하면 OS 설정보다 우선합니다.
   이슈 패널 뒤 어둑한 막은 --wb-scrim(라이트 rgb(22 20 15 / 0.28), 다크 rgb(0 0 0 / 0.5))입니다. */
@media (prefers-color-scheme: dark) {
  :root:not([data-theme="light"]) {
    --wb-paper: #16140f;
    --wb-surface: #1e1c16;
    --wb-sunken: #100f0b;
    --wb-ink: #eee8da;
    --wb-ink-soft: #cfc8b7;
    --wb-muted: #968f80;
    --wb-rule: #2f2c24;
    --wb-rule-strong: #eee8da;
    --wb-focus: #8fa8f0;
    --wb-accent: #ff6b42;
    --wb-accent-wash: #3a1c12;
    --wb-status-todo: #8c8576;
    --wb-status-progress: #6fa1ea;
    --wb-status-review: #e2b04a;
    --wb-status-done: #6dbb8b;
    --wb-status-cancel: #5a554a;
    --wb-shadow-lift: 0 1px 0 rgb(0 0 0 / 0.3), 0 6px 16px -8px rgb(0 0 0 / 0.6);
    --wb-shadow-panel: 0 0 0 1px rgb(238 232 218 / 0.08), 0 24px 48px -16px rgb(0 0 0 / 0.7);
    color-scheme: dark;
  }
}

:root[data-theme="dark"] {
  --wb-paper: #16140f;
  --wb-surface: #1e1c16;
  --wb-sunken: #100f0b;
  --wb-ink: #eee8da;
  --wb-ink-soft: #cfc8b7;
  --wb-muted: #968f80;
  --wb-rule: #2f2c24;
  --wb-rule-strong: #eee8da;
  --wb-focus: #8fa8f0;
  --wb-accent: #ff6b42;
  --wb-accent-wash: #3a1c12;
  --wb-status-todo: #8c8576;
  --wb-status-progress: #6fa1ea;
  --wb-status-review: #e2b04a;
  --wb-status-done: #6dbb8b;
  --wb-status-cancel: #5a554a;
  --wb-shadow-lift: 0 1px 0 rgb(0 0 0 / 0.3), 0 6px 16px -8px rgb(0 0 0 / 0.6);
  --wb-shadow-panel: 0 0 0 1px rgb(238 232 218 / 0.08), 0 24px 48px -16px rgb(0 0 0 / 0.7);
  color-scheme: dark;
}

@theme inline {
  --color-paper: var(--wb-paper);
  --color-surface: var(--wb-surface);
  --color-sunken: var(--wb-sunken);
  --color-ink: var(--wb-ink);
  --color-ink-soft: var(--wb-ink-soft);
  --color-muted: var(--wb-muted);
  --color-rule: var(--wb-rule);
  --color-rule-strong: var(--wb-rule-strong);
  --color-focus: var(--wb-focus);
  --color-accent: var(--wb-accent);
  --color-accent-wash: var(--wb-accent-wash);
  --color-status-todo: var(--wb-status-todo);
  --color-status-progress: var(--wb-status-progress);
  --color-status-review: var(--wb-status-review);
  --color-status-done: var(--wb-status-done);
  --color-status-cancel: var(--wb-status-cancel);

  /* next/font/google sets --font-hahmlet, --font-plex-kr, --font-plex-mono */
  --font-display: var(--font-hahmlet), "Hahmlet", serif;
  --font-sans: var(--font-plex-kr), "IBM Plex Sans KR", sans-serif;
  --font-mono: var(--font-plex-mono), "IBM Plex Mono", var(--font-plex-kr), monospace;

  --text-display: 2.25rem;
  --text-display--line-height: 1.25;
  --text-title: 1.5rem;
  --text-title--line-height: 1.3;
  --text-heading: 1.0625rem;
  --text-heading--line-height: 1.4;
  --text-body: 0.9375rem;
  --text-body--line-height: 1.6;
  --text-ui: 0.875rem;
  --text-ui--line-height: 1.45;
  --text-meta: 0.78125rem;
  --text-meta--line-height: 1.4;
  --text-eyebrow: 0.71875rem;
  --text-eyebrow--line-height: 1.2;

  --radius-mark: 2px;
  --radius-card: 6px;
  --radius-panel: 10px;

  --shadow-lift: var(--wb-shadow-lift);
  --shadow-panel: var(--wb-shadow-panel);

  --ease-paper: cubic-bezier(0.2, 0.7, 0.2, 1);
}

body {
  background: var(--wb-paper);
  color: var(--wb-ink);
  font-family: var(--font-sans);
  font-size: var(--text-body);
  line-height: 1.6;
  word-break: keep-all;
  font-variant-numeric: tabular-nums;
}

:focus-visible {
  outline: 2px solid var(--wb-focus);
  outline-offset: 2px;
}

@media (prefers-reduced-motion: reduce) {
  *, *::before, *::after {
    animation: none !important;
    transition: none !important;
  }
}
```

상태 색은 코드에서 한 곳(`web/src/lib/labels.ts`의 `STATUS_DOT`)에서만 클래스 이름으로 연결합니다:
`bg-status-todo`, `bg-status-progress`, `bg-status-review`, `bg-status-done`.

## 7. 화면을 만들 때 확인할 것 (PR 체크리스트)

PR 설명에 아래 항목을 복사해 확인 표시합니다.

- [ ] 새 화면을 만들기 전에, 기존 화면이나 이슈 패널에 붙일 수 없는지 먼저 따졌다(흩어지면 아무도 안 봄).
- [ ] 화면 맨 위가 "이 화면에서 가장 중요한 사실 하나"를 말한다.
- [ ] 색은 토큰만 썼다. `slate-*`, `blue-*` 같은 Tailwind 기본 색이나 16진수 값을 직접 쓰지 않았다.
- [ ] 인주(`accent`)는 주의 표시에만 썼고, 한 화면에 칠해진 면이 3곳 이하다.
- [ ] 상태는 점 + 한국어 이름으로 보이고, 이름이 할 일 / 진행 중 / 리뷰 중 / 완료 / 취소 중 하나다.
- [ ] 지라 용어가 없다. 고급 필드(주기, 크기, 우선순위)는 설정에서 켠 경우에만 보인다.
- [ ] 이슈가 보이는 모든 곳은 클릭하면 이슈 패널(`?issue=`)이 열린다.
- [ ] 빈 상태, 불러오는 중, 오류 문구를 모두 넣었다.
- [ ] 라이트와 다크 모두에서 확인했다.
- [ ] 휴대폰 너비(375px)에서 가로 스크롤 없이 읽힌다(넓은 표는 그 표만 가로 스크롤).
- [ ] 키보드로 모든 클릭 대상에 갈 수 있고 초점 테두리가 보인다.
- [ ] 움직임은 4장 규칙 안이고 `prefers-reduced-motion`에서 꺼진다.

## 8. 참고

- 개요 화면 기획: [overview.md](overview.md), 시안: [mockups/overview.html](mockups/overview.html)
- 이 기준은 `frontend-design` 스킬의 원칙(분명한 방향 하나, 흔한 기본값 피하기)을 이 제품에 맞게 정한 것입니다.
