import { UserPicker } from "./UserPicker";

const RUN_STEPS = [
  "docker compose up -d",
  "cd server/src/WorkBoard.Api && dotnet run",
  "cd web && pnpm install && pnpm dev",
];

/** Shown instead of the boards until someone picks who they are. */
export function StartScreen({ tenantRequired = false }: { tenantRequired?: boolean }) {
  return (
    <div className="reveal mx-auto flex max-w-[560px] flex-col gap-6 py-6 min-[900px]:py-12">
      <div>
        <p className="font-mono text-meta text-muted">업무 보드</p>
        <h1 className="mt-1 text-display font-semibold tracking-[-0.015em]">누가, 어느 프로젝트에서, 무엇을, 어디까지.</h1>
        <p className="mt-1 text-body text-ink-soft">이슈, PR, 회의 노트를 한 화면에 모아 매일 아침 확인합니다.</p>
      </div>

      <div className="card p-4">
        <p className="mb-3 text-ui text-ink-soft">
          {tenantRequired
            ? "이 사용자는 여러 조직에 속해 있습니다. 괄호 안의 조직까지 맞는 항목을 고르세요."
            : "시작하려면 누구로 볼지 고르세요."}
        </p>
        <UserPicker large />
      </div>

      <details className="card px-4 py-3 text-meta text-muted">
        <summary className="cursor-pointer hover:text-ink-soft">내 컴퓨터에서 실행하는 방법</summary>
        <ol className="mt-3 flex list-decimal flex-col gap-1.5 pl-5">
          {RUN_STEPS.map((step) => (
            <li key={step}>
              <code className="rounded-control bg-sunken px-1.5 py-0.5 font-mono text-meta text-ink-soft">{step}</code>
            </li>
          ))}
        </ol>
        <p className="mt-2">목록이 비어 있으면 API가 켜져 있는지 확인하세요.</p>
      </details>
    </div>
  );
}
