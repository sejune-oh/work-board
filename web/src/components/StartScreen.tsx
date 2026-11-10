import { UserPicker } from "./UserPicker";

const RUN_STEPS = [
  "docker compose up -d",
  "cd server/src/WorkBoard.Api && dotnet run",
  "cd web && pnpm install && pnpm dev",
];

/** Shown instead of the boards until someone picks who they are. */
export function StartScreen({ tenantRequired = false }: { tenantRequired?: boolean }) {
  return (
    <div className="reveal mx-auto flex max-w-[640px] flex-col gap-8 py-6 min-[900px]:py-12">
      <div className="border-b-2 border-rule-strong pb-3">
        <p className="eyebrow text-ink">업무 보드</p>
      </div>
      <h1 className="font-display text-[26px] font-semibold leading-[1.28] tracking-[-0.02em] min-[640px]:text-display">
        누가, 어느 프로젝트에서,
        <br />
        무엇을, 어디까지.
      </h1>
      <p className="-mt-4 text-body text-ink-soft">이슈, PR, 회의 노트를 한 장에 모아 아침마다 펼쳐 봅니다.</p>

      <div className="border-t border-rule pt-5">
        <p className="mb-3 text-ui text-ink-soft">
          {tenantRequired
            ? "이 사용자는 여러 조직에 속해 있습니다. 괄호 안의 조직까지 맞는 항목을 고르세요."
            : "시작하려면 누구로 볼지 고르세요."}
        </p>
        <UserPicker large />
      </div>

      <details className="text-meta text-muted">
        <summary className="cursor-pointer hover:text-ink-soft">내 컴퓨터에서 실행하는 방법</summary>
        <ol className="mt-3 flex list-decimal flex-col gap-1.5 pl-5">
          {RUN_STEPS.map((step) => (
            <li key={step}>
              <code className="rounded-mark bg-sunken px-1.5 py-0.5 font-mono text-meta text-ink-soft">{step}</code>
            </li>
          ))}
        </ol>
        <p className="mt-2">목록이 비어 있으면 API가 켜져 있는지 확인하세요.</p>
      </details>
    </div>
  );
}
