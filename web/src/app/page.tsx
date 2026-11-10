"use client";

import Link from "next/link";
import { Fragment } from "react";
import { ErrorBox, Loading } from "@/components/Feedback";
import { useIssuePanel } from "@/components/useIssuePanel";
import { Avatar, Dot, ProgressBar, SectionHead, Stamp, StatusBadge, progressLine } from "@/components/ui";
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

export default function OverviewPage() {
  const overview = useApi<Overview>("/api/overview");
  const me = useMe();
  const o = overview.data;

  if (overview.error && !o)
    return (
      <div className="flex flex-col gap-6">
        <Dateline o={null} />
        <ErrorBox error={overview.error} />
      </div>
    );

  return (
    <div>
      <section className="reveal" aria-labelledby="headline">
        <Dateline o={o} />
        {o ? <Headline o={o} /> : <Loading />}
        {o && !isEmptyTeam(o) && <Figures o={o} />}
      </section>

      <div className="mt-12 grid grid-cols-1 items-start gap-12 max-[640px]:mt-8 max-[640px]:gap-8 min-[1100px]:grid-cols-[minmax(0,1.65fr)_minmax(0,1fr)]">
        <div className="contents min-[1100px]:flex min-[1100px]:min-w-0 min-[1100px]:flex-col min-[1100px]:gap-12">
          <section className="reveal order-1 min-w-0 [animation-delay:40ms]" aria-labelledby="att">
            <Attention o={o} myLogin={me.data?.member.githubLogin ?? null} />
          </section>
          <section className="reveal order-2 min-w-0 [animation-delay:80ms]" aria-labelledby="people">
            <People o={o} myId={me.data?.member.githubUserId ?? null} />
          </section>
          <section className="reveal order-5 min-w-0 [animation-delay:120ms]" aria-labelledby="feed">
            <Activity o={o} />
          </section>
        </div>
        <div className="contents min-[1100px]:flex min-[1100px]:min-w-0 min-[1100px]:flex-col min-[1100px]:gap-12">
          <section className="reveal order-3 min-w-0 [animation-delay:80ms]" aria-labelledby="proj">
            <Projects o={o} />
          </section>
          <section className="reveal order-4 min-w-0 [animation-delay:120ms]" aria-labelledby="actions">
            <ActionItems o={o} myLogin={me.data?.member.githubLogin ?? null} />
          </section>
        </div>
      </div>
    </div>
  );
}

/* 1. Dateline, headline and figures ---------------------------------------------------------- */

function Dateline({ o }: { o: O }) {
  const me = useMe();
  const settings = useSettings();
  const sep = <span aria-hidden className="h-[11px] w-px self-center bg-rule" />;
  return (
    <div className="flex flex-wrap items-baseline gap-x-3.5 gap-y-1.5 border-b-2 border-rule-strong pb-3 text-meta text-muted">
      <span className="eyebrow text-ink">개요</span>
      <span>{formatDate(new Date().toISOString())}</span>
      {me.data && (
        <>
          {sep}
          <span>
            <span className="font-mono">{me.data.tenant.githubOrgLogin}</span>
            {o && ` · 열린 일 ${o.counts.open}건`}
          </span>
        </>
      )}
      {settings.sprintsEnabled && o?.sprint && (
        <>
          {sep}
          <span>
            {o.sprint.name} · {o.sprint.daysLeft}일 남음
          </span>
        </>
      )}
    </div>
  );
}

function ClauseView({ clause, last }: { clause: Clause; last: boolean }) {
  return (
    <>
      {clause.parts.map((p, i) =>
        typeof p === "number" ? (
          <em
            key={i}
            className="not-italic text-accent underline decoration-[color-mix(in_srgb,var(--wb-accent)_45%,transparent)] decoration-[3px] underline-offset-[7px]"
          >
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
  const h1 = "mb-1.5 mt-[22px] max-w-[26em] font-display text-[clamp(26px,3.4vw,40px)] font-semibold leading-[1.28] tracking-[-0.02em]";

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
      {sub && <p className="text-body text-ink-soft">{sub}</p>}
    </>
  );
}

function Figures({ o }: { o: Overview }) {
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

  const figures = [
    { href: "/people?status=in_progress", dot: STATUS_DOT.in_progress, label: "진행 중", n: o.counts.inProgress, note: moving ? `팀원 ${moving}명이 움직이는 중` : "움직이는 사람 없음", title: "사람별 보드에서 진행 중만 보기" },
    { href: "/people?status=in_review", dot: STATUS_DOT.in_review, label: "리뷰 대기", n: o.counts.inReview, note: wait === null ? "리뷰 요청된 PR 없음" : `가장 오래된 것 ${wait}일`, title: "사람별 보드에서 리뷰 중만 보기" },
    { href: "/people?stale=1", dot: STATUS_DOT.todo, label: "7일 넘게 멈춤", n: o.counts.stale, note: o.counts.stale ? `최장 ${maxIdle}일째` : "멈춘 일 없음", title: "7일 넘게 멈춘 일 보기", warn: o.counts.stale > 0 },
    { href: "/people?status=done&doneSince=week", dot: STATUS_DOT.done, label: "이번 주 완료", n: o.counts.doneThisWeek, note: finishers || "월요일부터 센 값", title: "이번 주 완료한 일 보기" },
  ];

  return (
    <>
      <div className="mt-7 grid grid-cols-2 border-y border-rule min-[640px]:grid-cols-4">
        {figures.map((f, i) => (
          <Link
            key={f.href}
            href={f.href}
            title={f.title}
            className={`flex flex-col gap-0.5 px-3.5 pb-3.5 pt-3 transition-colors duration-[120ms] ease-paper hover:bg-surface min-[640px]:px-5 min-[640px]:pb-[18px] min-[640px]:pt-4 ${
              i % 2 === 1 ? "border-l border-rule" : ""
            } ${i >= 2 ? "border-t border-rule min-[640px]:border-t-0" : ""} ${i === 2 ? "min-[640px]:border-l" : ""}`}
          >
            <span className="inline-flex items-center gap-[7px] text-[13px] text-ink-soft">
              <Dot className={f.dot} />
              {f.label}
            </span>
            <span className={`font-display text-[34px] font-semibold leading-[1.1] tracking-[-0.03em] min-[640px]:text-[44px] ${f.warn ? "text-accent" : ""}`}>
              {f.n}
              <small className="ml-[3px] font-sans text-ui font-medium tracking-normal text-muted">건</small>
            </span>
            <span className="text-meta text-muted">{f.note}</span>
          </Link>
        ))}
      </div>
      <div className="mt-4 flex flex-col gap-2" aria-label="팀 전체 상태">
        <ProgressBar thick counts={{ done: o.counts.doneThisWeek, inReview: o.counts.inReview, inProgress: o.counts.inProgress, todo }} />
        <div className="flex flex-wrap gap-x-4 gap-y-1 text-meta text-ink-soft">
          {(
            [
              ["done", o.counts.doneThisWeek],
              ["in_review", o.counts.inReview],
              ["in_progress", o.counts.inProgress],
              ["todo", todo],
            ] as [Status, number][]
          ).map(([s, n]) => (
            <span key={s} className="inline-flex items-center gap-1.5">
              <Dot className={STATUS_DOT[s]} />
              {STATUS_LABEL[s]} <b className="font-semibold text-ink">{n}</b>
            </span>
          ))}
          <span className="text-muted">완료는 이번 주 월요일부터</span>
        </div>
      </div>
    </>
  );
}

/* 2. Attention -------------------------------------------------------------------------------- */

const rowClass =
  "-mx-2.5 grid w-[calc(100%+1.25rem)] cursor-pointer grid-cols-[minmax(0,1fr)_auto] items-center gap-x-3.5 gap-y-1 border-b border-rule bg-transparent px-2.5 py-2.5 text-left transition-[background-color,box-shadow] duration-[120ms] ease-paper hover:rounded-card hover:border-transparent hover:bg-surface hover:shadow-lift min-[640px]:grid-cols-[8.75rem_minmax(0,1fr)_auto]";

function Who({ people }: { people: { displayName: string | null; githubLogin: string | null }[] }) {
  if (people.length === 0)
    return (
      <span className="inline-flex items-center gap-1.5 text-accent">
        <span aria-hidden className="h-5 w-5 rounded-full border border-dashed border-accent" />
        <span className="max-[640px]:hidden">담당자 없음</span>
      </span>
    );
  const first = people[0];
  return (
    <span className="inline-flex items-center gap-1.5">
      <Avatar person={first} size="sm" />
      <span className="max-[640px]:hidden">
        {personName(first)}
        {people.length > 1 && ` 외 ${people.length - 1}`}
      </span>
    </span>
  );
}

function IssueRow({
  issue,
  sub,
  meta,
  faded = false,
}: {
  issue: OverviewIssueRef;
  sub: React.ReactNode;
  meta: React.ReactNode;
  faded?: boolean;
}) {
  const { open } = useIssuePanel();
  const fade = faded ? "opacity-55" : "";
  return (
    <li className="[&:last-child>button]:border-b-0">
      <button type="button" onClick={() => open(issue.id)} className={rowClass}>
        <span className={`truncate font-mono text-meta text-muted max-[640px]:col-span-full ${fade}`}>
          {issue.repositoryName} #{issue.number}
        </span>
        <span className={`min-w-0 text-ui font-medium ${fade}`}>
          <span className="block truncate">{issue.title}</span>
          <span className="block truncate text-meta font-normal text-muted">{sub}</span>
        </span>
        <span className="flex items-center gap-2.5 whitespace-nowrap text-meta text-ink-soft">
          {meta}
          <span className={fade}>
            <Who people={issue.assignees} />
          </span>
        </span>
      </button>
    </li>
  );
}

function Group({
  stamp,
  total,
  why,
  more,
  children,
}: {
  stamp: React.ReactNode;
  total: number;
  why: string;
  more: { href: string; shown: number };
  children: React.ReactNode;
}) {
  return (
    <div className="[&+&]:mt-[18px]">
      <h3 className="mb-1 flex flex-wrap items-center gap-2 text-meta font-semibold text-ink-soft">
        {stamp}
        <span className="font-mono font-normal text-muted">{total}</span>
        <span className="font-normal text-muted">· {why}</span>
      </h3>
      <ul>{children}</ul>
      {total > more.shown && (
        <Link href={more.href} className="link mt-1 inline-block text-meta text-ink-soft">
          외 {total - more.shown}건 보드에서 보기 →
        </Link>
      )}
    </div>
  );
}

function Attention({ o, myLogin }: { o: O; myLogin: string | null }) {
  const settings = useSettings();
  const head = (n?: number) => (
    <SectionHead
      id="att"
      eyebrow="지금 봐야 할 것"
      title={
        <>
          손봐야 할 일 {n !== undefined && <span className="font-mono text-meta font-normal text-muted">{n}</span>}
        </>
      }
    />
  );
  if (!o)
    return (
      <>
        {head()}
        <Loading />
      </>
    );

  const a = o.attention;
  const t = a.totals;
  const sum = t.urgent + t.reviewWaiting + t.stale + t.unassigned + t.actionItemsWithoutIssue;
  const clear = clearNotes(o, settings.priorityEnabled);

  if (sum === 0)
    return (
      <>
        {head(0)}
        <p className="rounded-card border border-dashed border-rule px-3 py-2.5 text-meta text-muted">지금 손봐야 할 일이 없습니다.</p>
      </>
    );

  return (
    <>
      {head(sum)}
      {a.urgent.length > 0 && (
        <Group stamp={<Stamp fill>긴급</Stamp>} total={t.urgent} why="우선순위 긴급, 아직 끝나지 않음" more={{ href: "/people", shown: a.urgent.length }}>
          {a.urgent.map(({ issue, notStarted }) => (
            <IssueRow
              key={issue.id}
              issue={issue}
              sub={notStarted ? "아직 시작 전" : STATUS_LABEL[issue.status]}
              meta={notStarted ? <Stamp>시작 전</Stamp> : <StatusBadge status={issue.status} />}
            />
          ))}
        </Group>
      )}
      {a.reviewWaiting.length > 0 && (
        <Group stamp={<Stamp>리뷰 대기</Stamp>} total={t.reviewWaiting} why="리뷰 요청된 PR, 오래된 순" more={{ href: "/people?status=in_review", shown: a.reviewWaiting.length }}>
          {a.reviewWaiting.map(({ issue, pullRequest: pr }) => {
            const mine = myLogin !== null && (pr.authorGithubLogin === myLogin || issue.assignees.some((x) => x.githubLogin === myLogin));
            return (
              <IssueRow
                key={issue.id}
                issue={issue}
                sub={`PR #${pr.number} ${pr.title}${mine ? " · 내 PR" : ""}`}
                meta={
                  <span className="font-mono text-muted" title="PR이 마지막으로 바뀐 때부터 센 값">
                    {daysSince(pr.waitingSince)}일
                  </span>
                }
              />
            );
          })}
        </Group>
      )}
      {a.stale.length > 0 && (
        <Group stamp={<Stamp>멈춤</Stamp>} total={t.stale} why={o.counts.stale > t.stale ? `7일 넘게 아무 움직임 없음, 위 묶음에 나온 ${o.counts.stale - t.stale}건은 뺌` : "7일 넘게 아무 움직임 없음"} more={{ href: "/people?stale=1", shown: a.stale.length }}>
          {a.stale.map(({ issue, idleDays }) => (
            <IssueRow key={issue.id} issue={issue} faded sub={STATUS_LABEL[issue.status]} meta={<Stamp>{idleDays}일째</Stamp>} />
          ))}
        </Group>
      )}
      {a.unassigned.length > 0 && (
        <Group stamp={<Stamp>담당자 없음</Stamp>} total={t.unassigned} why="열린 일인데 맡은 사람이 없음" more={{ href: "/people", shown: a.unassigned.length }}>
          {a.unassigned.map((issue) => (
            <IssueRow key={issue.id} issue={issue} sub={`${STATUS_LABEL[issue.status]} · ${relativeDays(issue.updatedAt)}`} meta={null} />
          ))}
        </Group>
      )}
      {a.actionItemsWithoutIssue.length > 0 && (
        <Group stamp={<Stamp>이슈 없음</Stamp>} total={t.actionItemsWithoutIssue} why="회의에서 정했지만 이슈로 만들지 않은 할 일" more={{ href: "/notes", shown: a.actionItemsWithoutIssue.length }}>
          {a.actionItemsWithoutIssue.map((item) => (
            <li key={item.id} className="[&:last-child>a]:border-b-0">
              <Link href={`/notes/view?id=${item.note.id}`} className={rowClass}>
                <span className="truncate font-mono text-meta text-muted max-[640px]:col-span-full">{shortDate(item.note.heldAt)} 회의</span>
                <span className="min-w-0 text-ui font-medium">
                  <span className="block truncate">{item.text}</span>
                  <span className="block truncate text-meta font-normal text-muted">{item.note.title}에서 나옴</span>
                </span>
                <span className="flex items-center gap-2.5 whitespace-nowrap text-meta text-ink-soft">
                  {item.assignee?.githubLogin === myLogin && <span className="text-muted">나</span>}
                  <Who people={item.assignee ? [item.assignee] : []} />
                </span>
              </Link>
            </li>
          ))}
        </Group>
      )}
      {clear.length > 0 && (
        <p className="mt-4 flex flex-wrap gap-x-[18px] gap-y-1.5 rounded-card border border-dashed border-rule px-3 py-2.5 text-meta text-muted">
          {clear.map((c) => (
            <span key={c}>
              <span aria-hidden className="text-status-done">✓ </span>
              {c}
            </span>
          ))}
        </p>
      )}
    </>
  );
}

/* 3. People ----------------------------------------------------------------------------------- */

function People({ o, myId }: { o: O; myId: number | null }) {
  const { open } = useIssuePanel();
  const head = (
    <SectionHead
      id="people"
      eyebrow="누가 무엇을"
      title="사람"
      action={
        <Link href="/people" className="link">
          사람별 보드에서 보기 →
        </Link>
      }
    />
  );
  if (!o)
    return (
      <>
        {head}
        <Loading />
      </>
    );
  if (o.people.length === 0)
    return (
      <>
        {head}
        <p className="rounded-card border border-dashed border-rule px-3 py-2.5 text-meta text-muted">팀원이 없습니다. 설정에서 GitHub 조직을 연결하세요.</p>
      </>
    );

  const maxOpen = Math.max(1, ...o.people.map((p) => p.todo + p.inProgress + p.inReview));

  return (
    <>
      {head}
      <ul>
        {o.people.map((p) => {
          const openCount = p.todo + p.inProgress + p.inReview;
          const href = `/people?person=${encodeURIComponent(p.member.githubLogin)}`;
          return (
            <li
              key={p.member.id}
              className="grid grid-cols-1 items-center gap-x-[18px] gap-y-2.5 border-b border-rule py-3 last:border-b-0 min-[640px]:grid-cols-2 min-[900px]:grid-cols-[9.5rem_minmax(120px,1fr)_minmax(0,1.35fr)]"
            >
              <Link href={href} className="group flex min-w-0 items-center gap-2.5">
                <Avatar person={p.member} size="lg" ghost={openCount === 0} />
                <span className="min-w-0">
                  <span className="flex items-center gap-1.5 text-ui font-semibold group-hover:underline group-hover:underline-offset-3">
                    <span className="truncate">{personName(p.member)}</span>
                    {p.member.githubUserId === myId && (
                      <span className="rounded-mark border border-rule px-1 text-[11px] font-medium text-muted">나</span>
                    )}
                  </span>
                  <span className="block text-xs text-muted">{p.lastActivityAt ? relativeTime(p.lastActivityAt) : "활동 없음"}</span>
                </span>
              </Link>
              {openCount === 0 ? (
                <span className="text-[13px] text-muted">맡은 일 없음</span>
              ) : (
                <Link href={href} className="flex flex-col gap-1.5" aria-label={`${personName(p.member)} 열린 일 ${openCount}건`}>
                  <div className="bar !bg-transparent">
                    <div className="flex h-full gap-0.5" style={{ width: `${(openCount / maxOpen) * 100}%` }}>
                      {p.inProgress > 0 && <span className="bg-status-progress" style={{ flex: p.inProgress }} />}
                      {p.inReview > 0 && <span className="bg-status-review" style={{ flex: p.inReview }} />}
                      {p.todo > 0 && <span className="bg-status-todo opacity-45" style={{ flex: p.todo }} />}
                    </div>
                  </div>
                  <span className="flex flex-wrap items-center gap-x-2.5 text-xs text-muted">
                    <span>
                      열린 일 <b className="font-semibold text-ink">{openCount}</b>
                    </span>
                    <span>진행·리뷰 {p.inProgress + p.inReview}</span>
                    {p.doneThisWeek > 0 && <span>이번 주 완료 {p.doneThisWeek}</span>}
                    {p.overloaded && <Stamp>몰림</Stamp>}
                  </span>
                </Link>
              )}
              {p.current ? (
                <button
                  type="button"
                  onClick={() => open(p.current!.id)}
                  className="group flex min-w-0 cursor-pointer flex-col gap-px text-left min-[640px]:max-[899px]:col-span-full"
                >
                  <span className="inline-flex items-center gap-1.5 text-[11.5px] text-muted">
                    <Dot className={STATUS_DOT[p.current.status]} />
                    {STATUS_LABEL[p.current.status]} · <span className="font-mono">{p.current.repositoryName} #{p.current.number}</span>
                  </span>
                  <span className="truncate text-[13.5px] font-medium group-hover:underline group-hover:underline-offset-3">{p.current.title}</span>
                </button>
              ) : (
                <span className="text-[13px] text-muted min-[640px]:max-[899px]:col-span-full">{openCount ? "지금 진행 중인 일 없음" : ""}</span>
              )}
            </li>
          );
        })}
      </ul>
    </>
  );
}

/* 4. Projects, sprint, milestones ------------------------------------------------------------- */

function Projects({ o }: { o: O }) {
  const settings = useSettings();
  const head = (
    <SectionHead
      id="proj"
      eyebrow="어디까지 왔나"
      title="프로젝트와 목표"
      action={
        <Link href="/projects" className="link">
          보드 →
        </Link>
      }
    />
  );
  if (!o)
    return (
      <>
        {head}
        <Loading />
      </>
    );

  return (
    <>
      {head}
      {o.projects.length === 0 ? (
        <p className="rounded-card border border-dashed border-rule px-3 py-2.5 text-meta text-muted">프로젝트(저장소 묶음)가 없습니다. 설정에서 만드세요.</p>
      ) : (
        <div className="flex flex-col gap-2.5">
          {o.projects.map(({ project: p, byStatus, openPullRequests, latestNote }) => {
            const total = byStatus.todo + byStatus.inProgress + byStatus.inReview + byStatus.done;
            return (
              <Link
                key={p.id}
                href={`/projects?project=${p.id}`}
                className="block rounded-card border border-rule bg-surface px-4 pb-[15px] pt-3.5 transition-[border-color,box-shadow] duration-[120ms] ease-paper hover:border-ink-soft hover:shadow-lift"
              >
                <div className="mb-1 flex items-center gap-2">
                  <span aria-hidden className="h-2 w-2 shrink-0 rounded-mark" style={{ background: p.color ?? "var(--wb-muted)" }} />
                  <span className="font-display text-base font-semibold">{p.name}</span>
                  <span className="ml-auto font-display text-xl font-semibold tracking-[-0.02em]">
                    {percent(byStatus.done, total)}
                    <small className="text-[13px] text-muted">%</small>
                  </span>
                </div>
                <p className="mb-2.5 truncate font-mono text-meta text-muted">{p.repositories.join(" · ") || "저장소 없음"}</p>
                <ProgressBar counts={byStatus} />
                <div className="mt-[9px] flex flex-wrap justify-between gap-x-3 gap-y-1 text-meta text-ink-soft">
                  <span>
                    {total > 0 ? `완료 ${byStatus.done} / 전체 ${total}` : "이슈 없음"} · 열린 PR {openPullRequests}
                  </span>
                  <span className="text-muted">{latestNote ? `회의 ${relativeDays(latestNote.heldAt)}` : "회의 없음"}</span>
                </div>
              </Link>
            );
          })}
        </div>
      )}

      {settings.sprintsEnabled && o.sprint && <Sprint sprint={o.sprint} />}

      {o.milestones === null && (
        <p className="mt-2.5 rounded-card border border-dashed border-rule px-3.5 py-3 text-meta leading-normal text-muted">
          <b className="block text-[13px] font-semibold text-ink-soft">마일스톤</b>
          GitHub 마일스톤을 가져오면 여기에 마감일과 진행률이 함께 보입니다.
        </p>
      )}
    </>
  );
}

function Sprint({ sprint }: { sprint: NonNullable<Overview["sprint"]> }) {
  const days = sprintCalendar(sprint.startDate, sprint.endDate);
  const todayIndex = days.findIndex((d) => d.today);
  const s = sprint.byStatus;
  return (
    <Link
      href={`/projects?sprint=${sprint.id}`}
      className="mt-[18px] block rounded-card border border-rule bg-sunken px-4 py-3.5 transition-[border-color] duration-[120ms] ease-paper hover:border-ink-soft"
    >
      <div className="mb-2.5 flex items-baseline justify-between gap-2.5">
        <div>
          <div className="text-ui font-semibold">{sprint.name}</div>
          <div className="font-mono text-meta text-muted">
            {sprint.startDate.slice(5).replace("-", ".")} – {sprint.endDate.slice(5).replace("-", ".")}
          </div>
        </div>
        <div className="font-display text-[22px] font-semibold tracking-[-0.02em]">
          {sprint.daysLeft}
          <small className="ml-0.5 font-sans text-xs font-medium text-muted">일 남음</small>
        </div>
      </div>
      {days.length > 0 && (
        <div
          className="mb-3 mt-0.5 grid gap-[3px]"
          style={{ gridTemplateColumns: `repeat(${days.length}, minmax(0, 1fr))` }}
          role="img"
          aria-label={todayIndex >= 0 ? `${days.length}일 중 ${todayIndex + 1}일째` : `${days.length}일 주기`}
        >
          {days.map((d) => (
            <i
              key={d.key}
              className={`h-3.5 rounded-mark ${
                d.today
                  ? "bg-ink shadow-[0_0_0_2px_var(--wb-paper),0_0_0_3px_var(--wb-ink)]"
                  : d.past
                    ? "bg-ink-soft opacity-60"
                    : "bg-rule"
              } ${d.weekend && !d.today ? "!opacity-45" : ""}`}
            />
          ))}
        </div>
      )}
      <ProgressBar counts={s} />
      <div className="mt-[9px] flex flex-wrap justify-between gap-x-3 gap-y-1 text-meta text-ink-soft">
        <span>{progressLine(s)}</span>
        <span className="text-muted">
          리뷰 중 {s.inReview} · 진행 중 {s.inProgress}
        </span>
      </div>
    </Link>
  );
}

/* 5. Meeting action items --------------------------------------------------------------------- */

function ActionItems({ o, myLogin }: { o: O; myLogin: string | null }) {
  const { open } = useIssuePanel();
  const head = (
    <SectionHead
      id="actions"
      eyebrow="최근 14일 회의"
      title="회의에서 나온 할 일"
      action={
        <Link href="/notes" className="link">
          회의 노트 →
        </Link>
      }
    />
  );
  if (!o)
    return (
      <>
        {head}
        <Loading />
      </>
    );
  if (o.actionItems.length === 0)
    return (
      <>
        {head}
        <p className="rounded-card border border-dashed border-rule px-3 py-2.5 text-meta text-muted">최근 회의에서 남은 할 일이 없습니다.</p>
      </>
    );

  return (
    <>
      {head}
      <ul>
        {o.actionItems.map((a) => (
          <li key={a.id} className="flex flex-col gap-1.5 border-b border-rule py-3 last:border-b-0">
            <span className="text-ui font-medium">{a.text}</span>
            <span className="flex flex-wrap items-center gap-x-3 gap-y-1.5 text-meta text-ink-soft">
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
                  className="inline-flex cursor-pointer items-center gap-1.5 hover:underline hover:underline-offset-3"
                >
                  <Dot className={STATUS_DOT[a.issue.status]} />
                  <span className="font-mono">
                    {a.issue.repositoryName} #{a.issue.number}
                  </span>
                  {STATUS_LABEL[a.issue.status]}
                </button>
              ) : (
                <Stamp>이슈 없음</Stamp>
              )}
              <Link href={`/notes/view?id=${a.note.id}`} className="link text-muted">
                {a.note.title} · {formatDate(a.note.heldAt)}
              </Link>
            </span>
          </li>
        ))}
      </ul>
    </>
  );
}

/* 6. Activity --------------------------------------------------------------------------------- */

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
  const head = <SectionHead id="feed" eyebrow="지난 3일" title="최근 흐름" />;
  if (!o)
    return (
      <>
        {head}
        <Loading />
      </>
    );
  if (o.activity.length === 0)
    return (
      <>
        {head}
        <p className="rounded-card border border-dashed border-rule px-3 py-2.5 text-meta text-muted">지난 3일 동안 움직임이 없습니다.</p>
      </>
    );

  const days: { label: string; events: Overview["activity"] }[] = [];
  for (const e of o.activity) {
    const label = dayLabel(e.at);
    const last = days[days.length - 1];
    if (last && last.label === label) last.events.push(e);
    else days.push({ label, events: [e] });
  }

  const issueRef = (issue: NonNullable<Overview["activity"][number]["issue"]>) => (
    <button
      type="button"
      onClick={() => open(issue.id)}
      className="cursor-pointer text-left font-medium underline decoration-rule underline-offset-3 hover:decoration-current"
    >
      {issue.repositoryName} #{issue.number}
    </button>
  );

  return (
    <>
      {head}
      <ul>
        {days.map((d, di) => (
          <li key={d.label}>
            <div
              className={`flex items-center gap-2.5 pb-1.5 font-mono text-[11.5px] tracking-[0.02em] text-muted after:h-px after:flex-1 after:bg-rule ${di === 0 ? "" : "pt-3.5"}`}
            >
              {d.label}
            </div>
            {d.events.map((e, i) => {
              const k = KIND[e.kind];
              const who = e.actor ? personName(e.actor) : null;
              return (
                <div key={`${e.at}-${i}`} className="grid grid-cols-[26px_minmax(0,1fr)_auto] items-start gap-3 py-2">
                  {e.actor ? <Avatar person={e.actor} /> : <span aria-hidden className="h-[26px] w-[26px] rounded-full border border-dashed border-rule" />}
                  <div className="min-w-0 text-ui">
                    {e.kind !== "comment" && (
                      <span className="mr-1 inline-flex items-center gap-[5px] text-[11.5px] text-muted">
                        {k.dot && <Dot className={k.dot} />}
                        {k.label}
                      </span>
                    )}
                    {who && <b className="font-semibold">{who}</b>}{" "}
                    {e.kind === "comment" && e.issue && (
                      <>
                        <span className="text-ink-soft">님이 </span>
                        {issueRef(e.issue)}
                        <span className="text-ink-soft">에 댓글</span>
                        {e.snippet && <p className="mt-1 border-l-2 border-rule pl-2.5 text-[13px] text-ink-soft">{e.snippet}</p>}
                      </>
                    )}
                    {e.pullRequest && (
                      <>
                        <span className="text-ink-soft">
                          · {e.pullRequest.repositoryName} PR #{e.pullRequest.number}
                        </span>{" "}
                        {e.issue ? (
                          <button
                            type="button"
                            onClick={() => open(e.issue!.id)}
                            className="cursor-pointer text-left underline decoration-rule underline-offset-3 hover:decoration-current"
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
                        <span className="text-ink-soft">· </span>
                        {issueRef(e.issue)} <span className="text-ink-soft">{e.issue.title}</span>
                      </>
                    )}
                    {e.kind === "meeting_note" && e.note && (
                      <Link href={`/notes/view?id=${e.note.id}`} className="link font-medium">
                        {e.note.title}
                      </Link>
                    )}
                  </div>
                  <time dateTime={e.at} className="whitespace-nowrap pt-0.5 font-mono text-[11.5px] text-muted">
                    {relativeTime(e.at)}
                  </time>
                </div>
              );
            })}
          </li>
        ))}
      </ul>
    </>
  );
}
