import { UserPicker } from "./UserPicker";

const RUN_STEPS = [
  "docker compose up -d",
  "cd server/src/WorkBoard.Api && dotnet run",
  "cd web && pnpm install && pnpm dev",
];

/** Shown instead of the boards until someone picks who they are. */
export function StartScreen({ tenantRequired = false }: { tenantRequired?: boolean }) {
  return (
    <div className="mx-auto flex max-w-lg flex-col gap-6 py-10">
      <div>
        <h1 className="text-2xl font-bold">업무 보드</h1>
        <p className="mt-2 text-slate-600 dark:text-slate-300">
          누가, 어느 프로젝트에서, 무엇을, 어디까지 했는지 이슈·PR·회의 노트를 한 화면에 모아 보여 줍니다.
        </p>
      </div>

      <div className="rounded-xl border border-slate-200 p-5 dark:border-slate-800">
        <p className="mb-3 text-sm text-slate-500">
          {tenantRequired
            ? "이 사용자는 여러 조직에 속해 있습니다. 괄호 안의 조직까지 맞는 항목을 고르세요."
            : "시작하려면 누구로 볼지 고르세요."}
        </p>
        <UserPicker large />
      </div>

      <details className="text-sm text-slate-500">
        <summary className="cursor-pointer">내 컴퓨터에서 실행하는 방법</summary>
        <ol className="mt-2 flex list-decimal flex-col gap-1 pl-5">
          {RUN_STEPS.map((step) => (
            <li key={step}>
              <code className="rounded bg-slate-100 px-1.5 py-0.5 text-xs dark:bg-slate-800">{step}</code>
            </li>
          ))}
        </ol>
        <p className="mt-2">목록이 비어 있으면 API가 켜져 있는지 확인하세요.</p>
      </details>
    </div>
  );
}
