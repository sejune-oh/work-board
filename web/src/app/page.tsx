"use client";

import Link from "next/link";
import { Fragment } from "react";
import { Empty, ErrorBox, Loading } from "@/components/Feedback";
import { useIssuePanel } from "@/components/useIssuePanel";
import { ROW_ATTR } from "@/components/useShortcuts";
import { Avatar, Card, Dot, Flag, Kbd, ProgressBar, StatusBadge, progressLine } from "@/components/ui";
import { useApi } from "@/lib/api";
import {
  STATUS_DOT,
  STATUS_LABEL,
  daysSince,
  formatDate,
  percent,
  personName,
  relativeDays,
  relativeTime,
  shortDate,
} from "@/lib/labels";
import { useMe, useSettings } from "@/lib/me";
import {
  type Clause,
  clauseText,
  clearNotes,
  dayLabel,
  headlineClauses,
  isEmptyTeam,
  oldestWaitDays,
  sprintCalendar,
} from "@/lib/overview";
import type { ActivityKind, Overview, OverviewIssueRef, Status } from "@/lib/types";

type O = Overview | null;

const row = { [ROW_ATTR]: "" };

export default function OverviewPage() {
  const overview = useApi<Overview>("/api/overview");
  const me = useMe();
  const o = overview.data;

  if (overview.error && !o)
    return (
      <div className="flex flex-col gap-4">
        <MetaLine o={null} />
        <ErrorBox error={overview.error} />
      </div>
    );

  return (
    <div>
      <section className="reveal" aria-labelledby="headline">
        <MetaLine o={o} />
        {o ? <Headline o={o} /> : <Loading />}
        {o && !isEmptyTeam(o) && <Stats o={o} />}
      </section>

      <div className="mt-6 grid grid-cols-1 items-start gap-4 min-[1100px]:grid-cols-[minmax(0,1.7fr)_minmax(0,1fr)]">
        <div className="contents min-[1100px]:flex min-[1100px]:min-w-0 min-[1100px]:flex-col min-[1100px]:gap-4">
          <section className="reveal order-1 min-w-0 [animation-delay:30ms]" aria-labelledby="att">
            <Attention o={o} myLogin={me.data?.member.githubLogin ?? null} />
          </section>
          <section className="reveal order-2 min-w-0 [animation-delay:60ms]" aria-labelledby="people">
            <People o={o} myId={me.data?.member.githubUserId ?? null} />
          </section>
          <section className="reveal order-5 min-w-0 [animation-delay:90ms]" aria-labelledby="feed">
            <Activity o={o} />
          </section>
        </div>
        <div className="contents min-[1100px]:flex min-[1100px]:min-w-0 min-[1100px]:flex-col min-[1100px]:gap-4">
          <section className="reveal order-3 min-w-0 [animation-delay:60ms]" aria-labelledby="proj">
            <Projects o={o} />
          </section>
          <section className="reveal order-4 min-w-0 [animation-delay:90ms]" aria-labelledby="actions">
            <ActionItems o={o} myLogin={me.data?.member.githubLogin ?? null} />
          </section>
        </div>
      </div>
    </div>
  );
}

/* 1. Meta line, headline and stat strip ------------------------------------------------------ */

function MetaLine({ o }: { o: O }) {
  const settings = useSettings();
  return (
    <p className="flex flex-wrap gap-x-3.5 gap-y-1 font-mono text-meta text-muted">
      <span>{formatDate(new Date().toISOString())}</span>
      {o && (
        <span>
          열린 일 <b className="font-medium text-ink-soft">{o.counts.open}</b>
        </span>
      )}
      {settings.sprintsEnabled && o?.sprint && (
        <span>
          {o.sprint.name} · <b className="font-medium text-ink-soft">{o.sprint.daysLeft}</b>일 남음
        </span>
      )}
    </p>
  );
}

function ClauseView({ clause, last }: { clause: Clause; last: boolean }) {
  return (
    <>
      {clause.parts.map((p, i) =>
        typeof p === "number" ? (
          <em key={i} className="font-mono font-semibold not-italic text-accent">
            {p}
          </em>
        ) : (
          <Fragment key={i}>{p}</Fragment>
        ),
      )}
      {last ? clause.end : clause.and}
    </>
  );
}

function Headline({ o }: { o: Overview }) {
  const settings = useSettings();
  const h1 = "mt-2 max-w-[40em] text-[19px] font-semibold leading-[1.4] tracking-[-0.015em] min-[640px]:text-display";

  if (isEmptyTeam(o))
    return (
      <h1 id="headline" className={h1}>
        아직 이슈가 없습니다. 저장소를 연결하면 여기에 모입니다.
      </h1>
    );

  const { lead, rest } = headlineClauses(o);
  const allClear = lead.length === 1 && rest.length === 0 && o.counts.stale === 0 && o.attention.totals.reviewWaiting === 0;
  const good = clearNotes(o, settings.priorityEnabled);
  const sub = rest.length ? `그 밖에 ${rest.map(clauseText).join(" ")}` : allClear ? "" : good.slice(0, 2).map((g) => `${g}.`).join(" ");

  return (
    <>
      <h1 id="headline" className={h1}>
        {lead.map((c, i) => (
          <ClauseView key={i} clause={c} last={i === lead.length - 1} />
        ))}
      </h1>
      {sub && <p className="mt-0.5 text-body text-ink-soft">{sub}</p>}
    </>
  );
}

function Stats({ o }: { o: Overview }) {
  const moving = o.people.filter((p) => p.inProgress > 0).length;
  const wait = oldestWaitDays(o);
  const maxIdle = o.attention.stale.reduce((m, s) => Math.max(m, s.idleDays), 0);
  const finishers = o.people
    .filter((p) => p.doneThisWeek > 0)
    .sort((a, b) => b.doneThisWeek - a.doneThisWeek)
    .slice(0, 3)
    .map((p) => `${personName(p.member)} ${p.doneThisWeek}`)
    .join(" · ");
  const todo = Math.max(0, o.counts.open - o.counts.inProgress - o.counts.inReview);

  const stats = [
    { href: "/people?status=in_progress", dot: STATUS_DOT.in_progress, label: "진행 중", n: o.counts.inProgress, note: moving ? `${moving}명이 움직이는 중` : "움직이는 사람 없음", title: "사람별 보드에서 진행 중만 보기" },
    { href: "/people?status=in_review", dot: STATUS_DOT.in_review, label: "리뷰 대기", n: o.counts.inReview, note: wait === null ? "리뷰 요청된 PR 없음" : `가장 오래된 것 ${wait}일`, title: "사람별 보드에서 리뷰 중만 보기" },
    { href: "/people?stale=1", dot: STATUS_DOT.todo, label: "7일 넘게 멈춤", n: o.counts.stale, note: o.counts.stale ? `최장 ${maxIdle}일째` : "멈춘 일 없음", title: "7일 넘게 멈춘 일 보기", warn: o.counts.stale > 0 },
    { href: "/people?status=done&doneSince=week", dot: STATUS_DOT.done, label: "이번 주 완료", n: o.counts.doneThisWeek, note: finishers || "월요일부터 센 값", title: "이번 주 완료한 일 보기" },
  ];

  return (
    <>
      <div className="card mt-5 grid grid-cols-2 min-[640px]:grid-cols-4">
        {stats.map((s, i) => (
          <Link
            key={s.href}
            href={s.href}
            title={s.title}
            className={`grid grid-cols-[minmax(0,1fr)_auto] items-end gap-x-2 gap-y-0.5 px-3 pb-3 pt-2.5 transition-colors duration-[120ms] ease-tool hover:bg-hover focus-visible:-outline-offset-2 min-[640px]:px-4 min-[640px]:pb-3.5 min-[640px]:pt-3 ${
              i % 2 === 1 ? "border-l border-rule" : ""
            } ${i >= 2 ? "border-t border-rule min-[640px]:border-t-0" : ""} ${i === 2 ? "min-[640px]:border-l" : ""} ${
              s.warn ? "shadow-[inset_0_2px_0_var(--wb-accent)]" : ""
            }`}
          >
            <span className="col-span-full inline-flex items-center gap-1.5 text-[12.5px] text-ink-soft">
              <Dot className={s.dot} />
              {s.label}
            </span>
            <span className={`font-mono text-[24px] font-medium leading-[1.15] tracking-[-0.02em] min-[640px]:text-figure ${s.warn ? "text-accent" : ""}`}>
              {s.n}
              <small className="ml-0.5 font-sans text-meta font-normal tracking-normal text-muted">건</small>
            </span>
            <span className="truncate pb-1 text-right text-meta text-muted max-[640px]:col-span-full max-[640px]:pb-0 max-[640px]:text-left">
              {s.note}
            </span>
          </Link>
        ))}
      </div>
      <div className="mt-3 flex flex-wrap items-center gap-x-4 gap-y-2" aria-label="팀 전체 상태">
        <div className="min-w-[240px] flex-[1_1_280px]">
          <ProgressBar counts={{ done: o.counts.doneThisWeek, inReview: o.counts.inReview, inProgress: o.counts.inProgress, todo }} />
        </div>
        <div className="flex flex-wrap gap-x-3.5 gap-y-1 text-meta text-ink-soft">
          {(
            [
              ["done", o.counts.doneThisWeek],
              ["in_review", o.counts.inReview],
              ["in_progress", o.counts.inProgress],
              ["todo", todo],
            ] as [Status, number][]
          ).map(([s, n]) => (
            <span key={s} className="inline-flex items-center gap-[5px]">
              <Dot className={STATUS_DOT[s]} />
              {STATUS_LABEL[s]} <b className="font-mono font-medium text-ink">{n}</b>
            </span>
          ))}
          <span className="text-muted">완료는 이번 주(월요일부터)</span>
        </div>
      </div>
    </>
  );
}

/* 2. Attention table -------------------------------------------------------------------------- */

/** Below 36rem of card width each row folds into ref/age, title, status/who. */
const gridCols = "grid grid-cols-[minmax(0,1fr)_auto] gap-x-3 @[36rem]:grid-cols-[8.5rem_minmax(0,1fr)_6rem_auto_3.5rem]";
const rowClass = `${gridCols} w-full cursor-pointer items-center gap-y-0.5 border-t border-rule px-3.5 py-2 text-left transition-colors duration-[120ms] ease-tool hover:bg-hover focus-visible:bg-hover focus-visible:-outline-offset-2 @[36rem]:min-h-9 @[36rem]:py-1`;
const cell = {
  ref: "col-start-1 row-start-1 truncate font-mono text-meta text-muted @[36rem]:col-start-auto @[36rem]:row-start-auto",
  title: "col-span-2 row-start-2 min-w-0 text-ui font-medium @[36rem]:col-span-1 @[36rem]:row-start-auto",
  status: "col-start-1 row-start-3 @[36rem]:col-start-auto @[36rem]:row-start-auto",
  who: "col-start-2 row-start-3 justify-self-end whitespace-nowrap text-[12.5px] text-ink-soft @[36rem]:col-start-auto @[36rem]:row-start-auto @[36rem]:justify-self-start",
  age: "col-start-2 row-start-1 justify-self-end whitespace-nowrap font-mono text-meta text-muted @[36rem]:col-start-auto @[36rem]:row-start-auto",
};

function Who({ people }: { people: { displayName: string | null; githubLogin: string | null }[] }) {
  if (people.length === 0)
    return (
      <span className="inline-flex items-center gap-1.5 text-accent">
        <span aria-hidden className="h-[18px] w-[18px] rounded-full border border-dashed border-accent" />
        <span className="@max-[44rem]:sr-only">담당자 없음</span>
      </span>
    );
  const first = people[0];
  return (
    <span className="inline-flex items-center gap-1.5">
      <Avatar person={first} size="sm" />
      <span className="@max-[44rem]:sr-only">
        {personName(first)}
        {people.length > 1 && ` 외 ${people.length - 1}`}
      </span>
    </span>
  );
}

function IssueRow({
  issue,
  sub,
  status,
  age,
  faded = false,
  gutter = false,
}: {
  issue: OverviewIssueRef;
  sub?: React.ReactNode;
  status: React.ReactNode;
  age: React.ReactNode;
  faded?: boolean;
  gutter?: boolean;
}) {
  const { open } = useIssuePanel();
  const fade = faded ? "opacity-55" : "";
  return (
    <li>
      <button type="button" {...row} onClick={() => open(issue.id)} className={`${rowClass} ${gutter ? "gutter" : ""}`}>
        <span className={`${cell.ref} ${fade}`}>
          {issue.repositoryName} #{issue.number}
        </span>
        <span className={`${cell.title} ${fade}`}>
          <span className="block truncate">{issue.title}</span>
          {sub && <span className="block truncate text-meta font-normal text-muted">{sub}</span>}
        </span>
        <span className={`${cell.status} ${fade}`}>{status}</span>
        <span className={`${cell.who} ${fade}`}>
          <Who people={issue.assignees} />
        </span>
        <span className={cell.age}>{age}</span>
      </button>
    </li>
  );
}

function Group({
  flag,
  total,
  why,
  more,
  children,
}: {
  flag: React.ReactNode;
  total: number;
  why: string;
  more: { href: string; shown: number };
  children: React.ReactNode;
}) {
  return (
    <div>
      <h3 className="flex flex-wrap items-center gap-2 px-3.5 pb-1.5 pt-2.5 text-meta font-normal text-muted">
        {flag}
        <span className="font-mono">{total}</span>
        <span>{why}</span>
      </h3>
      <ul className="[&>li:first-child>*]:border-t-transparent">{children}</ul>
      {total > more.shown && (
        <Link href={more.href} className="link mx-3.5 mb-2 mt-1 inline-block text-meta text-ink-soft">
          외 {total - more.shown}건 보드에서 보기 →
        </Link>
      )}
    </div>
  );
}

function Attention({ o, myLogin }: { o: O; myLogin: string | null }) {
  const settings = useSettings();
  const card = (n: number | undefined, body: React.ReactNode) => (
    <Card id="att" title="손봐야 할 일" count={n} hint="지금 봐야 할 것" className="@container">
      {body}
    </Card>
  );
  if (!o) return card(undefined, <Loading />);

  const a = o.attention;
  const t = a.totals;
  const sum = t.urgent + t.reviewWaiting + t.stale + t.unassigned + t.actionItemsWithoutIssue;
  const clear = clearNotes(o, settings.priorityEnabled);

  if (sum === 0)
    return card(
      0,
      <div className="p-3.5">
        <Empty>지금 손봐야 할 일이 없습니다.</Empty>
      </div>,
    );

  return card(
    sum,
    <>
      <div aria-hidden className={`${gridCols} hidden h-[30px] items-center border-b border-rule bg-canvas px-3.5 font-mono text-[11px] text-muted @[36rem]:grid`}>
        <span>저장소 #번호</span>
        <span>제목</span>
        <span>상태</span>
        <span>담당</span>
        <span className="text-right">경과</span>
      </div>
      {a.urgent.length > 0 && (
        <Group flag={<Flag fill>긴급</Flag>} total={t.urgent} why="우선순위 긴급, 아직 끝나지 않음" more={{ href: "/people", shown: a.urgent.length }}>
          {a.urgent.map(({ issue, notStarted }) => (
            <IssueRow
              key={issue.id}
              issue={issue}
              gutter={notStarted}
              status={notStarted ? <Flag>시작 전</Flag> : <StatusBadge status={issue.status} />}
              age={relativeDays(issue.updatedAt)}
            />
          ))}
        </Group>
      )}
      {a.reviewWaiting.length > 0 && (
        <Group flag={<Flag>리뷰 대기</Flag>} total={t.reviewWaiting} why="리뷰 요청된 PR, 오래된 순" more={{ href: "/people?status=in_review", shown: a.reviewWaiting.length }}>
          {a.reviewWaiting.map(({ issue, pullRequest: pr }) => {
            const mine = myLogin !== null && (pr.authorGithubLogin === myLogin || issue.assignees.some((x) => x.githubLogin === myLogin));
            return (
              <IssueRow
                key={issue.id}
                issue={issue}
                sub={`PR #${pr.number} ${pr.title}${mine ? " · 내 PR" : ""}`}
                status={<StatusBadge status={issue.status} />}
                age={<span title="PR이 마지막으로 바뀐 때부터 센 값">{daysSince(pr.waitingSince)}일</span>}
              />
            );
          })}
        </Group>
      )}
      {a.stale.length > 0 && (
        <Group flag={<Flag>멈춤</Flag>} total={t.stale} why={o.counts.stale > t.stale ? `7일 넘게 아무 움직임 없음, 위 묶음에 나온 ${o.counts.stale - t.stale}건은 뺌` : "7일 넘게 아무 움직임 없음"} more={{ href: "/people?stale=1", shown: a.stale.length }}>
          {a.stale.map(({ issue, idleDays }) => (
            <IssueRow key={issue.id} issue={issue} faded status={<StatusBadge status={issue.status} />} age={<Flag>{idleDays}일째</Flag>} />
          ))}
        </Group>
      )}
      {a.unassigned.length > 0 && (
        <Group flag={<Flag>담당자 없음</Flag>} total={t.unassigned} why="열린 일인데 맡은 사람이 없음" more={{ href: "/people", shown: a.unassigned.length }}>
          {a.unassigned.map((issue) => (
            <IssueRow key={issue.id} issue={issue} status={<StatusBadge status={issue.status} />} age={relativeDays(issue.updatedAt)} />
          ))}
        </Group>
      )}
      {a.actionItemsWithoutIssue.length > 0 && (
        <Group flag={<Flag>이슈 없음</Flag>} total={t.actionItemsWithoutIssue} why="회의에서 정했지만 이슈로 만들지 않은 할 일" more={{ href: "/notes", shown: a.actionItemsWithoutIssue.length }}>
          {a.actionItemsWithoutIssue.map((item) => (
            <li key={item.id}>
              <Link href={`/notes/view?id=${item.note.id}`} {...row} className={rowClass}>
                <span className={cell.ref}>{shortDate(item.note.heldAt)} 회의</span>
                <span className={cell.title}>
                  <span className="block truncate">{item.text}</span>
                  <span className="block truncate text-meta font-normal text-muted">{item.note.title}에서 나옴</span>
                </span>
                <span className={`${cell.status} text-[12.5px] text-muted`}>이슈 없음</span>
                <span className={cell.who}>
                  <Who people={item.assignee ? [item.assignee] : []} />
                  {item.assignee?.githubLogin === myLogin && <span className="ml-1 text-muted">(나)</span>}
                </span>
                <span className={cell.age}>{relativeDays(item.note.heldAt)}</span>
              </Link>
            </li>
          ))}
        </Group>
      )}
      {clear.length > 0 && (
        <p className="mt-1 flex flex-wrap gap-x-4 gap-y-1 border-t border-rule px-3.5 py-2 text-meta text-muted">
          {clear.map((c) => (
            <span key={c} className="inline-flex items-center gap-1.5">
              <Dot className="bg-status-done" />
              {c}
            </span>
          ))}
        </p>
      )}
    </>,
  );
}

/* 3. People ----------------------------------------------------------------------------------- */

function People({ o, myId }: { o: O; myId: number | null }) {
  const { open } = useIssuePanel();
  const card = (body: React.ReactNode) => (
    <Card
      id="people"
      title="사람"
      count={o?.people.length}
      hint="누가 무엇을"
      className="@container"
      action={
        <Link href="/people" className="inline-flex items-center gap-1.5 hover:text-ink">
          사람별 보드 <Kbd>g p</Kbd>
        </Link>
      }
    >
      {body}
    </Card>
  );
  if (!o) return card(<Loading />);
  if (o.people.length === 0)
    return card(
      <div className="p-3.5">
        <Empty>팀원이 없습니다. 설정에서 GitHub 조직을 연결하세요.</Empty>
      </div>,
    );

  const maxOpen = Math.max(1, ...o.people.map((p) => p.todo + p.inProgress + p.inReview));

  return card(
    <ul>
      {o.people.map((p) => {
        const openCount = p.todo + p.inProgress + p.inReview;
        const href = `/people?person=${encodeURIComponent(p.member.githubLogin)}`;
        return (
          <li
            key={p.member.id}
            className="grid grid-cols-[minmax(0,1fr)_auto] items-center gap-x-3.5 gap-y-1.5 border-t border-rule px-3.5 py-2 first:border-t-0 @[40rem]:grid-cols-[8.5rem_minmax(100px,1fr)_7rem_minmax(0,1.4fr)]"
          >
            <Link href={href} className="group flex min-w-0 items-center gap-2">
              <Avatar person={p.member} ghost={openCount === 0} />
              <span className="min-w-0">
                <span className="flex items-center gap-1.5 text-[13px] font-semibold group-hover:underline group-hover:underline-offset-3">
                  <span className="truncate">{personName(p.member)}</span>
                  {p.member.githubUserId === myId && (
                    <span className="rounded-[3px] border border-rule px-[3px] font-mono text-[10px] font-medium text-muted">나</span>
                  )}
                </span>
                <span className="block font-mono text-[11px] text-muted">{p.lastActivityAt ? relativeTime(p.lastActivityAt) : "활동 없음"}</span>
              </span>
            </Link>
            {openCount === 0 ? (
              <span className="col-span-full row-start-2 text-[12.5px] text-muted @[40rem]:col-span-1 @[40rem]:row-start-auto">맡은 일 없음</span>
            ) : (
              <Link
                href={href}
                className="col-span-full row-start-2 block @[40rem]:col-span-1 @[40rem]:row-start-auto"
                aria-label={`${personName(p.member)} 열린 일 ${openCount}건`}
              >
                <div className="bar !bg-transparent">
                  <div className="flex h-full gap-0.5" style={{ width: `${(openCount / maxOpen) * 100}%` }}>
                    {p.inProgress > 0 && <span className="bg-status-progress" style={{ flex: p.inProgress }} />}
                    {p.inReview > 0 && <span className="bg-status-review" style={{ flex: p.inReview }} />}
                    {p.todo > 0 && <span className="bg-status-todo opacity-50" style={{ flex: p.todo }} />}
                  </div>
                </div>
              </Link>
            )}
            <span className="col-start-2 row-start-1 flex flex-wrap items-center justify-end gap-x-2 whitespace-nowrap font-mono text-meta text-muted @[40rem]:col-start-auto @[40rem]:row-start-auto @[40rem]:justify-start">
              <span title={`진행·리뷰 ${p.inProgress + p.inReview}`}>
                열린 <b className="font-medium text-ink">{openCount}</b>
              </span>
              {p.doneThisWeek > 0 && <span>· 완료 {p.doneThisWeek}</span>}
              {p.overloaded && <Flag className="font-sans">몰림</Flag>}
            </span>
            {p.current ? (
              <button
                type="button"
                {...row}
                onClick={() => open(p.current!.id)}
                className="group col-span-full flex min-w-0 cursor-pointer items-center gap-2 text-left text-[13px] @[40rem]:col-span-1"
              >
                <Dot className={STATUS_DOT[p.current.status]} />
                <span className="sr-only">{STATUS_LABEL[p.current.status]}</span>
                <span className="whitespace-nowrap font-mono text-meta text-muted">
                  {p.current.repositoryName} #{p.current.number}
                </span>
                <span className="truncate font-medium group-hover:underline group-hover:underline-offset-3">{p.current.title}</span>
              </button>
            ) : (
              <span className="col-span-full text-[12.5px] text-muted @[40rem]:col-span-1">{openCount ? "지금 진행 중인 일 없음" : ""}</span>
            )}
          </li>
        );
      })}
    </ul>,
  );
}

/* 4. Projects, sprint, milestones ------------------------------------------------------------- */

function Projects({ o }: { o: O }) {
  const settings = useSettings();
  const card = (body: React.ReactNode) => (
    <Card
      id="proj"
      title="프로젝트와 목표"
      hint="어디까지 왔나"
      action={
        <Link href="/projects" className="inline-flex items-center gap-1.5 hover:text-ink">
          보드 <Kbd>g b</Kbd>
        </Link>
      }
    >
      {body}
    </Card>
  );
  if (!o) return card(<Loading />);

  return card(
    <>
      {o.projects.length === 0 ? (
        <div className="p-3.5">
          <Empty>프로젝트(저장소 묶음)가 없습니다. 설정에서 만드세요.</Empty>
        </div>
      ) : (
        <div>
          {o.projects.map(({ project: p, byStatus, openPullRequests, latestNote }) => {
            const total = byStatus.todo + byStatus.inProgress + byStatus.inReview + byStatus.done;
            return (
              <Link
                key={p.id}
                href={`/projects?project=${p.id}`}
                className="block border-t border-rule px-3.5 pb-3 pt-2.5 transition-colors duration-[120ms] ease-tool first:border-t-0 hover:bg-hover focus-visible:-outline-offset-2"
              >
                <div className="mb-[7px] flex min-w-0 items-center gap-2">
                  <span aria-hidden className="h-2 w-2 shrink-0 rounded-mark" style={{ background: p.color ?? "var(--wb-muted)" }} />
                  <span className="shrink-0 text-heading font-semibold">{p.name}</span>
                  <span className="truncate font-mono text-[11.5px] text-muted">{p.repositories.join(" · ") || "저장소 없음"}</span>
                  <span className="ml-auto shrink-0 font-mono text-ui font-medium">{percent(byStatus.done, total)}%</span>
                </div>
                <ProgressBar counts={byStatus} />
                <div className="mt-1.5 flex flex-wrap justify-between gap-x-2.5 gap-y-0.5 text-meta text-muted">
                  <span>
                    {total > 0 ? (
                      <>
                        완료 <b className="font-mono font-medium text-ink-soft">{byStatus.done}/{total}</b>
                      </>
                    ) : (
                      "이슈 없음"
                    )}{" "}
                    · 열린 PR <b className="font-mono font-medium text-ink-soft">{openPullRequests}</b>
                  </span>
                  <span>{latestNote ? `회의 ${relativeDays(latestNote.heldAt)}` : "회의 없음"}</span>
                </div>
              </Link>
            );
          })}
        </div>
      )}

      {settings.sprintsEnabled && o.sprint && <Sprint sprint={o.sprint} />}

      {o.milestones === null && (
        <p className="m-3.5 rounded-control border border-dashed border-rule-strong px-3 py-2 text-meta text-muted">
          <b className="mr-1.5 font-semibold text-ink-soft">마일스톤</b>
          GitHub 마일스톤을 가져오면 마감일과 진행률이 여기에 보입니다.
        </p>
      )}
    </>,
  );
}

function Sprint({ sprint }: { sprint: NonNullable<Overview["sprint"]> }) {
  const days = sprintCalendar(sprint.startDate, sprint.endDate);
  const todayIndex = days.findIndex((d) => d.today);
  const s = sprint.byStatus;
  return (
    <Link
      href={`/projects?sprint=${sprint.id}`}
      className="block border-t border-rule bg-canvas px-3.5 pb-3.5 pt-3 transition-colors duration-[120ms] ease-tool hover:bg-hover focus-visible:-outline-offset-2"
    >
      <div className="mb-2 flex items-baseline justify-between gap-2.5">
        <div className="min-w-0">
          <span className="text-[13px] font-semibold">{sprint.name}</span>{" "}
          <span className="font-mono text-meta text-muted">
            {sprint.startDate.slice(5).replace("-", ".")}–{sprint.endDate.slice(5).replace("-", ".")}
          </span>
        </div>
        <div className="whitespace-nowrap font-mono text-[13px] font-medium">
          {sprint.daysLeft}
          <small className="ml-0.5 font-sans text-meta font-normal text-muted">일 남음</small>
        </div>
      </div>
      {days.length > 0 && (
        <div
          className="mb-2.5 grid gap-0.5"
          style={{ gridTemplateColumns: `repeat(${days.length}, minmax(0, 1fr))` }}
          role="img"
          aria-label={todayIndex >= 0 ? `${days.length}일 중 ${todayIndex + 1}일째` : `${days.length}일 주기`}
        >
          {days.map((d) => (
            <i
              key={d.key}
              className={`h-2.5 rounded-[1px] ${d.today ? "bg-ink" : d.past ? "bg-muted opacity-60" : "bg-rule"} ${
                d.weekend && !d.today ? "!opacity-40" : ""
              }`}
            />
          ))}
        </div>
      )}
      <ProgressBar counts={s} />
      <div className="mt-1.5 flex flex-wrap justify-between gap-x-2.5 gap-y-0.5 text-meta text-muted">
        <span className="font-mono">{progressLine(s)}</span>
        <span>
          리뷰 중 {s.inReview} · 진행 중 {s.inProgress}
        </span>
      </div>
    </Link>
  );
}

/* 5. Meeting action items --------------------------------------------------------------------- */

function ActionItems({ o, myLogin }: { o: O; myLogin: string | null }) {
  const { open } = useIssuePanel();
  const card = (body: React.ReactNode) => (
    <Card
      id="actions"
      title="회의에서 나온 할 일"
      hint="최근 14일"
      action={
        <Link href="/notes" className="inline-flex items-center gap-1.5 hover:text-ink">
          회의 노트 <Kbd>g n</Kbd>
        </Link>
      }
    >
      {body}
    </Card>
  );
  if (!o) return card(<Loading />);
  if (o.actionItems.length === 0)
    return card(
      <div className="p-3.5">
        <Empty>최근 회의에서 남은 할 일이 없습니다.</Empty>
      </div>,
    );

  return card(
    <ul>
      {o.actionItems.map((a) => (
        <li key={a.id} className="flex flex-col gap-1 border-t border-rule px-3.5 py-2.5 first:border-t-0">
          <span className="text-ui font-medium">{a.text}</span>
          <span className="flex flex-wrap items-center gap-x-2.5 gap-y-1 text-meta text-ink-soft">
            {a.assignee ? (
              <span className="inline-flex items-center gap-1.5">
                <Avatar person={a.assignee} size="sm" />
                {personName(a.assignee)}
                {a.assignee.githubLogin === myLogin && <span className="text-muted">(나)</span>}
              </span>
            ) : (
              <span className="text-muted">담당자 없음</span>
            )}
            {a.issue ? (
              <button
                type="button"
                onClick={() => open(a.issue!.id)}
                className="group inline-flex cursor-pointer items-center gap-1.5"
              >
                <Dot className={STATUS_DOT[a.issue.status]} />
                <span className="font-mono group-hover:underline group-hover:underline-offset-3">
                  {a.issue.repositoryName} #{a.issue.number}
                </span>
                {STATUS_LABEL[a.issue.status]}
              </button>
            ) : (
              <Flag>이슈 없음</Flag>
            )}
            <Link href={`/notes/view?id=${a.note.id}`} className="link text-muted">
              {a.note.title} · {formatDate(a.note.heldAt)}
            </Link>
          </span>
        </li>
      ))}
    </ul>,
  );
}

/* 6. Activity log ----------------------------------------------------------------------------- */

const KIND: Record<ActivityKind, { label: string; dot: string | null }> = {
  comment: { label: "댓글", dot: null },
  pr_opened: { label: "PR 열림", dot: STATUS_DOT.in_progress },
  pr_review_requested: { label: "리뷰 요청", dot: STATUS_DOT.in_review },
  pr_merged: { label: "병합", dot: STATUS_DOT.done },
  issue_closed: { label: "완료", dot: STATUS_DOT.done },
  meeting_note: { label: "회의 노트", dot: null },
};

function Activity({ o }: { o: O }) {
  const { open } = useIssuePanel();
  const card = (body: React.ReactNode) => (
    <Card id="feed" title="최근 흐름" hint="지난 3일">
      {body}
    </Card>
  );
  if (!o) return card(<Loading />);
  if (o.activity.length === 0)
    return card(
      <div className="p-3.5">
        <Empty>지난 3일 동안 움직임이 없습니다.</Empty>
      </div>,
    );

  const days: { label: string; events: Overview["activity"] }[] = [];
  for (const e of o.activity) {
    const label = dayLabel(e.at);
    const last = days[days.length - 1];
    if (last && last.label === label) last.events.push(e);
    else days.push({ label, events: [e] });
  }

  const refClass = "cursor-pointer text-left font-mono text-meta underline decoration-rule-strong underline-offset-3 hover:decoration-current";
  const issueRef = (issue: NonNullable<Overview["activity"][number]["issue"]>) => (
    <button type="button" onClick={() => open(issue.id)} className={refClass}>
      {issue.repositoryName} #{issue.number}
    </button>
  );

  return card(
    <ul className="pb-2">
      {days.map((d) => (
        <li key={d.label}>
          <div className="px-3.5 pb-1 pt-2.5 font-mono text-[11px] text-muted">{d.label}</div>
          {d.events.map((e, i) => {
            const k = KIND[e.kind];
            const who = e.actor ? personName(e.actor) : null;
            return (
              <div
                key={`${e.at}-${i}`}
                className="grid grid-cols-[18px_minmax(0,1fr)_auto] items-start gap-2.5 px-3.5 py-[5px] min-[640px]:grid-cols-[4.75rem_18px_minmax(0,1fr)]"
              >
                <time
                  dateTime={e.at}
                  className="col-start-3 row-start-1 whitespace-nowrap pt-px font-mono text-[11.5px] text-muted min-[640px]:col-start-1"
                >
                  {relativeTime(e.at)}
                </time>
                <span className="col-start-1 row-start-1 pt-px min-[640px]:col-start-2">
                  {e.actor ? <Avatar person={e.actor} size="sm" /> : <span aria-hidden className="block h-[18px] w-[18px] rounded-full border border-dashed border-rule-strong" />}
                </span>
                <div className="col-start-2 row-start-1 min-w-0 text-[13px] min-[640px]:col-start-3">
                  {e.kind !== "comment" && (
                    <span className="mr-1.5 inline-flex items-center gap-[5px] text-meta text-ink-soft">
                      {k.dot && <Dot className={k.dot} />}
                      {k.label}
                    </span>
                  )}
                  {who && <b className="font-semibold">{who}</b>}{" "}
                  {e.kind === "comment" && e.issue && (
                    <>
                      <span className="text-muted">님이 </span>
                      {issueRef(e.issue)}
                      <span className="text-muted">에 댓글</span>
                      {e.snippet && <p className="mt-0.5 border-l-2 border-rule-strong pl-2 text-[12.5px] text-ink-soft">{e.snippet}</p>}
                    </>
                  )}
                  {e.pullRequest && (
                    <>
                      <span className="text-muted">
                        · <span className="font-mono text-meta">{e.pullRequest.repositoryName} PR #{e.pullRequest.number}</span>
                      </span>{" "}
                      {e.issue ? (
                        <button
                          type="button"
                          onClick={() => open(e.issue!.id)}
                          className="cursor-pointer text-left underline decoration-rule-strong underline-offset-3 hover:decoration-current"
                        >
                          {e.pullRequest.title}
                        </button>
                      ) : (
                        e.pullRequest.title
                      )}
                    </>
                  )}
                  {e.kind === "issue_closed" && e.issue && (
                    <>
                      <span className="text-muted">· </span>
                      {issueRef(e.issue)} <span className="text-ink-soft">{e.issue.title}</span>
                    </>
                  )}
                  {e.kind === "meeting_note" && e.note && (
                    <Link href={`/notes/view?id=${e.note.id}`} className="link font-medium">
                      {e.note.title}
                    </Link>
                  )}
                </div>
              </div>
            );
          })}
        </li>
      ))}
    </ul>,
  );
}
