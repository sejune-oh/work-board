"use client";

import Link from "next/link";
import { Fragment } from "react";
import { Empty, ErrorBox, Loading } from "@/components/Feedback";
import { useIssuePanel } from "@/components/useIssuePanel";
import { ROW_ATTR } from "@/components/useShortcuts";
import { Avatar, Card, Dot, ExternalIcon, Flag, Kbd, MilestoneIcon, ProgressBar, StatusBadge, progressLine } from "@/components/ui";
import { MILESTONES_PATH, useApi } from "@/lib/api";
import { useFormat, useT } from "@/lib/i18n";
import { STATUS_DOT, daysSince, percent } from "@/lib/labels";
import { useMe, useSettings } from "@/lib/me";
import {
  type Clause,
  clauseText,
  clearNotes,
  headlineClauses,
  isEmptyTeam,
  oldestWaitDays,
  sprintCalendar,
} from "@/lib/overview";
import type { ActivityKind, Milestone, Overview, OverviewIssueRef, Status } from "@/lib/types";

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

      {/* One column below 768px; a two-card row for projects and meetings up to 1439px; a 360px rail from 1440px. */}
      <div className="mt-6 grid grid-cols-1 items-start gap-6 min-[768px]:grid-cols-2 min-[1440px]:grid-cols-[minmax(0,1fr)_360px]">
        <div className="contents min-[1440px]:flex min-[1440px]:min-w-0 min-[1440px]:flex-col min-[1440px]:gap-6">
          <section className="reveal order-1 col-span-full min-w-0 [animation-delay:30ms]" aria-labelledby="att">
            <Attention o={o} myLogin={me.data?.member.githubLogin ?? null} />
          </section>
          <section className="reveal order-2 col-span-full min-w-0 [animation-delay:60ms]" aria-labelledby="people">
            <People o={o} myId={me.data?.member.githubUserId ?? null} />
          </section>
          <section className="reveal order-5 col-span-full min-w-0 [animation-delay:90ms]" aria-labelledby="feed">
            <Activity o={o} />
          </section>
        </div>
        <div className="contents min-[1440px]:flex min-[1440px]:min-w-0 min-[1440px]:flex-col min-[1440px]:gap-6">
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
  const t = useT();
  const fmt = useFormat();
  return (
    <p className="flex flex-wrap gap-x-3 gap-y-1 text-small text-muted [&>*+*]:before:mr-3 [&>*+*]:before:text-faint [&>*+*]:before:content-['·']">
      <span>{fmt.date(new Date().toISOString())}</span>
      {o && (
        <span>
          {t.overview.open} <b className="font-mono font-medium text-ink">{fmt.num(o.counts.open)}</b>
        </span>
      )}
      {settings.sprintsEnabled && o?.sprint && (
        <span>
          {o.sprint.name} · <b className="font-mono font-medium text-ink">{o.sprint.daysLeft}</b>
          {t.overview.daysLeft(o.sprint.daysLeft)}
        </span>
      )}
    </p>
  );
}

function ClauseView({ clause, last }: { clause: Clause; last: boolean }) {
  const fmt = useFormat();
  return (
    <>
      {clause.parts.map((p, i) =>
        typeof p === "number" ? (
          <em key={i} className="not-italic text-accent">
            {fmt.num(p)}
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
  const t = useT();
  const h1 = "mt-3 max-w-[28em] text-display font-semibold";

  if (isEmptyTeam(o))
    return (
      <h1 id="headline" className={h1}>
        {t.overview.emptyTeam}
      </h1>
    );

  const { lead, rest } = headlineClauses(o, t);
  const allClear = lead.length === 1 && rest.length === 0 && o.counts.stale === 0 && o.attention.totals.reviewWaiting === 0;
  const good = clearNotes(o, settings.priorityEnabled, t);
  const sub = rest.length ? t.overview.headline.rest(rest.map(clauseText).join(" ")) : allClear ? "" : good.slice(0, 2).map((g) => `${g}.`).join(" ");

  return (
    <>
      <h1 id="headline" className={h1}>
        {lead.map((c, i) => (
          <ClauseView key={i} clause={c} last={i === lead.length - 1} />
        ))}
      </h1>
      {sub && <p className="mt-2 text-body text-ink-soft">{sub}</p>}
    </>
  );
}

function Stats({ o }: { o: Overview }) {
  const t = useT();
  const fmt = useFormat();
  const st = t.overview.stats;
  const moving = o.people.filter((p) => p.inProgress > 0).length;
  const wait = oldestWaitDays(o);
  const maxIdle = o.attention.stale.reduce((m, s) => Math.max(m, s.idleDays), 0);
  const finishers = o.people
    .filter((p) => p.doneThisWeek > 0)
    .sort((a, b) => b.doneThisWeek - a.doneThisWeek)
    .slice(0, 3)
    .map((p) => `${fmt.person(p.member)} ${p.doneThisWeek}`)
    .join(" · ");
  const todo = Math.max(0, o.counts.open - o.counts.inProgress - o.counts.inReview);

  const stats = [
    { href: "/people?status=in_progress", dot: STATUS_DOT.in_progress, label: st.inProgress, n: o.counts.inProgress, note: moving ? st.movingPeople(moving) : st.nobodyMoving, title: st.inProgressTitle },
    { href: "/people?status=in_review", dot: STATUS_DOT.in_review, label: st.review, n: o.counts.inReview, note: wait === null ? st.noReviewRequested : st.oldestWait(wait), title: st.reviewTitle },
    { href: "/people?stale=1", dot: STATUS_DOT.todo, label: st.stale, n: o.counts.stale, note: o.counts.stale ? st.longestIdle(maxIdle) : st.nothingStale, title: st.staleTitle, warn: o.counts.stale > 0 },
    { href: "/people?status=done&doneSince=week", dot: STATUS_DOT.done, label: st.doneThisWeek, n: o.counts.doneThisWeek, note: finishers || st.sinceMonday, title: st.doneTitle },
  ];

  return (
    <div className="card mt-8">
      <div className="grid grid-cols-2 gap-px bg-rule min-[900px]:grid-cols-4">
        {stats.map((s) => (
          <Link
            key={s.href}
            href={s.href}
            title={s.title}
            className={`relative flex min-w-0 flex-col gap-2 bg-surface p-5 transition-colors duration-[120ms] ease-tool hover:bg-[linear-gradient(var(--wb-hover),var(--wb-hover))] focus-visible:-outline-offset-2 max-[639px]:p-4 ${
              s.warn ? "before:absolute before:inset-x-5 before:top-0 before:h-0.5 before:rounded-b-sm before:bg-accent" : ""
            }`}
          >
            <span className="inline-flex items-center gap-2 text-small font-medium text-ink-soft">
              <Dot className={s.dot} />
              {s.label}
            </span>
            <span className={`font-mono text-figure font-medium ${s.warn ? "text-accent" : ""}`}>
              {fmt.num(s.n)}
              {t.common.countUnit && <small className="ml-1.5 font-sans text-small font-normal tracking-normal text-muted">{t.common.countUnit}</small>}
            </span>
            <span className="truncate text-small text-muted">{s.note}</span>
          </Link>
        ))}
      </div>
      <div className="flex flex-col gap-3 border-t border-rule px-5 pb-5 pt-4 max-[639px]:px-4" aria-label={st.teamAria}>
        <ProgressBar counts={{ done: o.counts.doneThisWeek, inReview: o.counts.inReview, inProgress: o.counts.inProgress, todo }} />
        <div className="flex flex-wrap gap-x-4 gap-y-1 text-small text-ink-soft">
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
              {t.status[s]} <b className="font-mono text-meta font-medium text-ink">{fmt.num(n)}</b>
            </span>
          ))}
          <span className="text-muted">{st.doneNote}</span>
        </div>
      </div>
    </div>
  );
}

/* 2. Attention table -------------------------------------------------------------------------- */

/** Below 560px of card width each row folds into ref/age, title, status/who. Row lines are inset to the card padding. */
const gridCols = "grid grid-cols-[minmax(0,1fr)_auto] gap-x-4 @[35rem]:grid-cols-[8.5rem_minmax(0,1fr)_6.5rem_7.5rem_4.5rem]";
const rowClass = `${gridCols} relative w-full cursor-pointer items-center gap-y-1 px-5 py-4 text-left transition-colors duration-[120ms] ease-tool after:absolute after:inset-x-5 after:top-0 after:h-px after:bg-rule hover:bg-hover focus-visible:bg-hover focus-visible:-outline-offset-2 @[35rem]:min-h-[46px] @[35rem]:py-3`;
const cell = {
  ref: "col-start-1 row-start-1 truncate font-mono text-meta text-muted @[35rem]:col-start-auto @[35rem]:row-start-auto",
  title: "col-span-2 row-start-2 min-w-0 text-ui font-medium @[35rem]:col-span-1 @[35rem]:row-start-auto",
  status: "col-start-1 row-start-3 @[35rem]:col-start-auto @[35rem]:row-start-auto",
  who: "col-start-2 row-start-3 justify-self-end whitespace-nowrap text-small text-ink-soft @[35rem]:col-start-auto @[35rem]:row-start-auto @[35rem]:justify-self-start",
  age: "col-start-2 row-start-1 justify-self-end whitespace-nowrap text-small text-muted @[35rem]:col-start-auto @[35rem]:row-start-auto",
};

function Who({ people }: { people: { displayName: string | null; githubLogin: string | null }[] }) {
  const t = useT();
  const fmt = useFormat();
  if (people.length === 0)
    return (
      <span className="inline-flex items-center gap-1.5 text-accent">
        <span aria-hidden className="h-5 w-5 rounded-full border border-dashed border-accent" />
        <span className="@max-[44rem]:sr-only">{t.common.unassigned}</span>
      </span>
    );
  const first = people[0];
  return (
    <span className="inline-flex items-center gap-1.5">
      <Avatar person={first} size="sm" />
      <span className="@max-[44rem]:sr-only">
        {fmt.person(first)}
        {people.length > 1 && t.overview.attention.others(people.length - 1)}
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
          {sub && <span className="block truncate text-small font-normal text-muted">{sub}</span>}
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
  const t = useT();
  return (
    <div>
      <h3 className="flex flex-wrap items-center gap-x-3 gap-y-1 px-5 pb-1 pt-5 text-small font-normal text-muted">
        {flag}
        <span className="font-mono text-meta text-ink">{total}</span>
        <span>{why}</span>
      </h3>
      <ul className="[&>li:first-child>*]:after:hidden">{children}</ul>
      {total > more.shown && (
        <Link href={more.href} className="link mx-5 mb-2 mt-1 inline-block text-small text-ink-soft">
          {t.overview.attention.more(total - more.shown)}
        </Link>
      )}
    </div>
  );
}

function Attention({ o, myLogin }: { o: O; myLogin: string | null }) {
  const settings = useSettings();
  const t = useT();
  const fmt = useFormat();
  const at = t.overview.attention;
  const card = (n: number | undefined, body: React.ReactNode) => (
    <Card id="att" title={at.title} count={n} hint={at.hint} className="@container">
      {body}
    </Card>
  );
  if (!o) return card(undefined, <Loading />);

  const a = o.attention;
  const tt = a.totals;
  const sum = tt.urgent + tt.reviewWaiting + tt.stale + tt.unassigned + tt.actionItemsWithoutIssue;
  const clear = clearNotes(o, settings.priorityEnabled, t);

  if (sum === 0)
    return card(
      0,
      <div className="px-5 pb-5">
        <Empty>{at.empty}</Empty>
      </div>,
    );

  return card(
    sum,
    <>
      <div aria-hidden className={`${gridCols} hidden h-9 items-center border-b border-rule px-5 text-meta text-muted @[35rem]:grid`}>
        <span>{at.colRef}</span>
        <span>{at.colTitle}</span>
        <span>{at.colStatus}</span>
        <span>{at.colWho}</span>
        <span className="text-right">{at.colAge}</span>
      </div>
      {a.urgent.length > 0 && (
        <Group flag={<Flag fill>{t.priority.urgent}</Flag>} total={tt.urgent} why={at.urgentWhy} more={{ href: "/people", shown: a.urgent.length }}>
          {a.urgent.map(({ issue, notStarted }) => (
            <IssueRow
              key={issue.id}
              issue={issue}
              gutter={notStarted}
              status={notStarted ? <Flag>{at.notStarted}</Flag> : <StatusBadge status={issue.status} />}
              age={fmt.relativeDays(issue.updatedAt, true)}
            />
          ))}
        </Group>
      )}
      {a.reviewWaiting.length > 0 && (
        <Group flag={<Flag>{at.review}</Flag>} total={tt.reviewWaiting} why={at.reviewWhy} more={{ href: "/people?status=in_review", shown: a.reviewWaiting.length }}>
          {a.reviewWaiting.map(({ issue, pullRequest: pr }) => {
            const mine = myLogin !== null && (pr.authorGithubLogin === myLogin || issue.assignees.some((x) => x.githubLogin === myLogin));
            return (
              <IssueRow
                key={issue.id}
                issue={issue}
                sub={`PR #${pr.number} ${pr.title}${mine ? at.myPr : ""}`}
                status={<StatusBadge status={issue.status} />}
                age={<span title={at.waitTitle}>{at.days(daysSince(pr.waitingSince))}</span>}
              />
            );
          })}
        </Group>
      )}
      {a.stale.length > 0 && (
        <Group flag={<Flag>{at.stalled}</Flag>} total={tt.stale} why={o.counts.stale > tt.stale ? at.staleWhyExcluded(o.counts.stale - tt.stale) : at.staleWhy} more={{ href: "/people?stale=1", shown: a.stale.length }}>
          {a.stale.map(({ issue, idleDays }) => (
            <IssueRow key={issue.id} issue={issue} faded status={<StatusBadge status={issue.status} />} age={<Flag>{at.idleDays(idleDays)}</Flag>} />
          ))}
        </Group>
      )}
      {a.unassigned.length > 0 && (
        <Group flag={<Flag>{t.common.unassigned}</Flag>} total={tt.unassigned} why={at.unassignedWhy} more={{ href: "/people", shown: a.unassigned.length }}>
          {a.unassigned.map((issue) => (
            <IssueRow key={issue.id} issue={issue} status={<StatusBadge status={issue.status} />} age={fmt.relativeDays(issue.updatedAt, true)} />
          ))}
        </Group>
      )}
      {a.actionItemsWithoutIssue.length > 0 && (
        <Group flag={<Flag>{t.common.noIssue}</Flag>} total={tt.actionItemsWithoutIssue} why={at.noIssueWhy} more={{ href: "/notes", shown: a.actionItemsWithoutIssue.length }}>
          {a.actionItemsWithoutIssue.map((item) => (
            <li key={item.id}>
              <Link href={`/notes/view?id=${item.note.id}`} {...row} className={rowClass}>
                <span className={`${cell.ref} font-sans text-small`}>{at.meetingOn(fmt.shortDate(item.note.heldAt))}</span>
                <span className={cell.title}>
                  <span className="block truncate">{item.text}</span>
                  <span className="block truncate text-small font-normal text-muted">{at.fromNote(item.note.title)}</span>
                </span>
                <span className={`${cell.status} text-small text-muted`}>{t.common.noIssue}</span>
                <span className={cell.who}>
                  <Who people={item.assignee ? [item.assignee] : []} />
                  {item.assignee?.githubLogin === myLogin && <span className="ml-1 text-muted">{t.common.meParen}</span>}
                </span>
                <span className={cell.age}>{fmt.relativeDays(item.note.heldAt, true)}</span>
              </Link>
            </li>
          ))}
        </Group>
      )}
      {clear.length > 0 && (
        <p className="mt-1 flex flex-wrap gap-x-4 gap-y-1 border-t border-rule px-5 pb-5 pt-4 text-small text-muted">
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
  const t = useT();
  const fmt = useFormat();
  const pp = t.overview.people;
  const card = (body: React.ReactNode) => (
    <Card
      id="people"
      title={pp.title}
      count={o?.people.length}
      hint={pp.hint}
      className="@container"
      action={
        <Link href="/people" className="inline-flex items-center gap-1.5 hover:text-ink">
          {pp.board} <Kbd>g p</Kbd>
        </Link>
      }
    >
      {body}
    </Card>
  );
  if (!o) return card(<Loading />);
  if (o.people.length === 0)
    return card(
      <div className="px-5 pb-5">
        <Empty>{pp.empty}</Empty>
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
            className="relative grid grid-cols-[minmax(0,1fr)_auto] items-center gap-x-4 gap-y-2 px-5 py-3.5 after:absolute after:inset-x-5 after:top-0 after:h-px after:bg-rule first:after:hidden @[35rem]:grid-cols-[11rem_minmax(100px,1fr)_6.5rem_minmax(0,1.4fr)]"
          >
            <Link href={href} className="group flex min-w-0 items-center gap-2">
              <Avatar person={p.member} ghost={openCount === 0} />
              <span className="min-w-0">
                <span className="flex items-center gap-1.5 text-ui font-semibold group-hover:underline group-hover:underline-offset-3">
                  <span className="truncate">{fmt.person(p.member)}</span>
                  {p.member.githubUserId === myId && (
                    <span className="text-small font-normal text-muted">{t.common.meParen}</span>
                  )}
                </span>
                <span className="block text-meta text-muted">{p.lastActivityAt ? fmt.relativeTime(p.lastActivityAt) : pp.noActivity}</span>
              </span>
            </Link>
            {openCount === 0 ? (
              <span className="col-span-full row-start-2 text-small text-muted @[35rem]:col-span-1 @[35rem]:row-start-auto">{pp.noWork}</span>
            ) : (
              <Link
                href={href}
                className="col-span-full row-start-2 block @[35rem]:col-span-1 @[35rem]:row-start-auto"
                aria-label={pp.openAria(fmt.person(p.member), openCount)}
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
            <span className="col-start-2 row-start-1 flex flex-wrap items-center justify-end gap-x-2 whitespace-nowrap text-small text-muted @[35rem]:col-start-auto @[35rem]:row-start-auto @[35rem]:justify-start">
              <span title={pp.activeTitle(p.inProgress + p.inReview)}>
                {pp.open} <b className="font-mono text-meta font-medium text-ink">{openCount}</b>
              </span>
              {p.doneThisWeek > 0 && <span>{pp.done(p.doneThisWeek)}</span>}
              {p.overloaded && <Flag>{t.common.crowded}</Flag>}
            </span>
            {p.current ? (
              <button
                type="button"
                {...row}
                onClick={() => open(p.current!.id)}
                className="group col-span-full flex min-w-0 cursor-pointer items-center gap-2 text-left text-small @[35rem]:col-span-1"
              >
                <Dot className={STATUS_DOT[p.current.status]} />
                <span className="sr-only">{t.status[p.current.status]}</span>
                <span className="whitespace-nowrap font-mono text-meta text-muted">
                  {p.current.repositoryName} #{p.current.number}
                </span>
                <span className="truncate font-medium group-hover:underline group-hover:underline-offset-3">{p.current.title}</span>
              </button>
            ) : (
              <span className="col-span-full text-small text-muted @[35rem]:col-span-1">{openCount ? pp.nothingInProgress : ""}</span>
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
  const t = useT();
  const fmt = useFormat();
  const pj = t.overview.projects;
  const card = (body: React.ReactNode) => (
    <Card
      id="proj"
      title={pj.title}
      hint={pj.hint}
      action={
        <Link href="/projects" className="inline-flex items-center gap-1.5 hover:text-ink">
          {pj.board} <Kbd>g b</Kbd>
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
        <div className="px-5 pb-5">
          <Empty>{pj.empty}</Empty>
        </div>
      ) : (
        <div>
          {o.projects.map(({ project: p, byStatus, openPullRequests, latestNote }) => {
            const total = byStatus.todo + byStatus.inProgress + byStatus.inReview + byStatus.done;
            return (
              <Link
                key={p.id}
                href={`/projects?project=${p.id}`}
                className="relative block px-5 py-4 transition-colors duration-[120ms] ease-tool after:absolute after:inset-x-5 after:top-0 after:h-px after:bg-rule first:after:hidden hover:bg-hover focus-visible:-outline-offset-2"
              >
                <div className="mb-3 flex min-w-0 items-center gap-2">
                  <span aria-hidden className="h-2.5 w-2.5 shrink-0 rounded-mark" style={{ background: p.color ?? "var(--wb-muted)" }} />
                  <span className="shrink-0 text-ui font-semibold">{p.name}</span>
                  <span className={`truncate text-meta text-muted ${p.repositories.length ? "font-mono" : ""}`}>{p.repositories.join(" · ") || t.common.noRepos}</span>
                  <span className="ml-auto shrink-0 font-mono text-small font-medium">{percent(byStatus.done, total)}%</span>
                </div>
                <ProgressBar counts={byStatus} />
                <div className="mt-2.5 flex flex-wrap justify-between gap-x-3 gap-y-0.5 text-small text-muted">
                  <span>
                    {total > 0 ? (
                      <>
                        {pj.done} <b className="font-mono font-medium text-ink-soft">{byStatus.done}/{total}</b>
                      </>
                    ) : (
                      pj.noIssues
                    )}{" "}
                    {pj.openPrs} <b className="font-mono font-medium text-ink-soft">{openPullRequests}</b>
                  </span>
                  <span>{latestNote ? pj.meeting(fmt.relativeDays(latestNote.heldAt)) : pj.noMeeting}</span>
                </div>
              </Link>
            );
          })}
        </div>
      )}

      {settings.sprintsEnabled && o.sprint && <Sprint sprint={o.sprint} />}

      <Milestones />
    </>,
  );
}

const MILESTONES_SHOWN = 4;

/** Open milestones with done / all (cancelled left out by the API). Before any are imported, a hint instead. */
function Milestones() {
  const { data, error } = useApi<Milestone[]>(MILESTONES_PATH);
  const t = useT();
  const pj = t.overview.projects;
  const fmt = useFormat();
  if (!data && !error) return null;

  if (!data || data.length === 0)
    return (
      <p className="mx-5 mb-5 mt-1 rounded-item border border-dashed border-rule-strong px-4 py-3 text-small text-muted">
        <b className="mr-1.5 font-semibold text-ink-soft">{pj.milestones}</b>
        {pj.milestonesHint}
      </p>
    );

  const open = data.filter((m) => m.state === "open");
  const closed = data.length - open.length;
  const now = Date.now();

  return (
    <div className="border-t border-rule px-5 pb-5 pt-4">
      <h3 className="mb-1 flex items-center gap-2 text-small font-semibold text-ink-soft">
        <MilestoneIcon />
        {pj.milestonesOpen}
        <span className="font-mono text-meta font-normal text-muted">{open.length}</span>
      </h3>
      {open.length === 0 && <p className="py-2 text-small text-muted">{pj.milestonesAllClosed}</p>}
      <ul>
        {open.slice(0, MILESTONES_SHOWN).map((m) => {
          const pct = percent(m.doneCount, m.issueCount);
          const overdue = m.dueOn !== null && Date.parse(m.dueOn) < now;
          const title = (
            <>
              <span className="truncate text-ui font-medium">{m.title}</span>
              <span className="shrink-0 font-mono text-meta text-muted">{m.repository.name}</span>
            </>
          );
          return (
            <li key={m.id} className="relative py-3 after:absolute after:inset-x-0 after:top-0 after:h-px after:bg-rule first:after:hidden">
              <div className="mb-2 flex min-w-0 items-center gap-2">
                {m.htmlUrl ? (
                  <a
                    href={m.htmlUrl}
                    target="_blank"
                    rel="noopener noreferrer"
                    title={t.github.newTab}
                    className="group flex min-w-0 items-center gap-2 hover:text-ink"
                  >
                    {title}
                    <span className="text-muted group-hover:text-ink">
                      <ExternalIcon size={12} />
                    </span>
                  </a>
                ) : (
                  <span className="flex min-w-0 items-center gap-2">{title}</span>
                )}
                <span className="ml-auto shrink-0 font-mono text-small font-medium">{pct}%</span>
              </div>
              {m.issueCount > 0 && (
                <div className="bar" role="img" aria-label={pj.milestonesAria(m.title, m.doneCount, m.issueCount)}>
                  {m.doneCount > 0 && <span className="bg-status-done" style={{ width: `${pct}%` }} />}
                </div>
              )}
              <div className="mt-2 flex flex-wrap justify-between gap-x-3 gap-y-0.5 text-small text-muted">
                {m.issueCount === 0 ? (
                  <span>{t.progress.empty}</span>
                ) : (
                  <span>
                    {pj.done}{" "}
                    <b className="font-mono font-medium text-ink-soft">
                      {m.doneCount}/{m.issueCount}
                    </b>
                  </span>
                )}
                <span className={`ml-auto ${overdue ? "font-medium text-ink-soft" : ""}`}>
                  {m.dueOn ? (overdue ? pj.overdue : pj.due)(fmt.date(m.dueOn)) : pj.noDue}
                </span>
              </div>
            </li>
          );
        })}
      </ul>
      {closed > 0 && <p className="mt-1 text-small text-muted">{pj.milestonesMore(closed)}</p>}
    </div>
  );
}

function Sprint({ sprint }: { sprint: NonNullable<Overview["sprint"]> }) {
  const t = useT();
  const sp = t.overview.sprint;
  const days = sprintCalendar(sprint.startDate, sprint.endDate);
  const todayIndex = days.findIndex((d) => d.today);
  const s = sprint.byStatus;
  return (
    <Link
      href={`/projects?sprint=${sprint.id}`}
      className="block border-t border-rule bg-canvas px-5 pb-5 pt-4 transition-colors duration-[120ms] ease-tool hover:bg-hover focus-visible:-outline-offset-2"
    >
      <div className="mb-2 flex items-baseline justify-between gap-2.5">
        <div className="min-w-0">
          <span className="text-ui font-semibold">{sprint.name}</span>{" "}
          <span className="font-mono text-meta text-muted">
            {sprint.startDate.slice(5).replace("-", ".")}–{sprint.endDate.slice(5).replace("-", ".")}
          </span>
        </div>
        <div className="whitespace-nowrap font-mono text-small font-medium">
          {sprint.daysLeft}
          <small className="ml-0.5 font-sans text-meta font-normal text-muted">{sp.daysLeft(sprint.daysLeft)}</small>
        </div>
      </div>
      {days.length > 0 && (
        <div
          className="mb-2.5 grid gap-0.5"
          style={{ gridTemplateColumns: `repeat(${days.length}, minmax(0, 1fr))` }}
          role="img"
          aria-label={todayIndex >= 0 ? sp.dayOf(todayIndex + 1, days.length) : sp.length(days.length)}
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
      <div className="mt-2.5 flex flex-wrap justify-between gap-x-3 gap-y-0.5 text-small text-muted">
        <span>{progressLine(s, t)}</span>
        <span>{sp.reviewAndProgress(s.inReview, s.inProgress)}</span>
      </div>
    </Link>
  );
}

/* 5. Meeting action items --------------------------------------------------------------------- */

function ActionItems({ o, myLogin }: { o: O; myLogin: string | null }) {
  const { open } = useIssuePanel();
  const t = useT();
  const fmt = useFormat();
  const ac = t.overview.actions;
  const card = (body: React.ReactNode) => (
    <Card
      id="actions"
      title={ac.title}
      hint={ac.hint}
      action={
        <Link href="/notes" className="inline-flex items-center gap-1.5 hover:text-ink">
          {ac.notes} <Kbd>g n</Kbd>
        </Link>
      }
    >
      {body}
    </Card>
  );
  if (!o) return card(<Loading />);
  if (o.actionItems.length === 0)
    return card(
      <div className="px-5 pb-5">
        <Empty>{ac.empty}</Empty>
      </div>,
    );

  return card(
    <ul>
      {o.actionItems.map((a) => (
        <li key={a.id} className="relative flex flex-col gap-1.5 px-5 py-4 after:absolute after:inset-x-5 after:top-0 after:h-px after:bg-rule first:after:hidden">
          <span className="text-ui font-medium">{a.text}</span>
          <span className="flex flex-wrap items-center gap-x-3 gap-y-1 text-small text-ink-soft">
            {a.assignee ? (
              <span className="inline-flex items-center gap-1.5">
                <Avatar person={a.assignee} size="sm" />
                {fmt.person(a.assignee)}
                {a.assignee.githubLogin === myLogin && <span className="text-muted">{t.common.meParen}</span>}
              </span>
            ) : (
              <span className="text-muted">{t.common.unassigned}</span>
            )}
            {a.issue ? (
              <button
                type="button"
                onClick={() => open(a.issue!.id)}
                className="group inline-flex cursor-pointer items-center gap-1.5 [&>.font-mono]:text-meta"
              >
                <Dot className={STATUS_DOT[a.issue.status]} />
                <span className="font-mono group-hover:underline group-hover:underline-offset-3">
                  {a.issue.repositoryName} #{a.issue.number}
                </span>
                {t.status[a.issue.status]}
              </button>
            ) : (
              <Flag>{t.common.noIssue}</Flag>
            )}
            <Link href={`/notes/view?id=${a.note.id}`} className="link text-muted">
              {a.note.title} · {fmt.date(a.note.heldAt)}
            </Link>
          </span>
        </li>
      ))}
    </ul>,
  );
}

/* 6. Activity log ----------------------------------------------------------------------------- */

const KIND_DOT: Record<ActivityKind, string | null> = {
  comment: null,
  pr_opened: STATUS_DOT.in_progress,
  pr_review_requested: STATUS_DOT.in_review,
  pr_merged: STATUS_DOT.done,
  issue_closed: STATUS_DOT.done,
  meeting_note: null,
};

function Activity({ o }: { o: O }) {
  const { open } = useIssuePanel();
  const t = useT();
  const fmt = useFormat();
  const av = t.overview.activity;
  const card = (body: React.ReactNode) => (
    <Card id="feed" title={av.title} hint={av.hint}>
      {body}
    </Card>
  );
  if (!o) return card(<Loading />);
  if (o.activity.length === 0)
    return card(
      <div className="px-5 pb-5">
        <Empty>{av.empty}</Empty>
      </div>,
    );

  const days: { label: string; events: Overview["activity"] }[] = [];
  for (const e of o.activity) {
    const label = fmt.dayLabel(e.at);
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
    <ul className="pb-4">
      {days.map((d) => (
        <li key={d.label}>
          <div className="px-5 pb-1.5 pt-4 text-meta font-medium text-muted">{d.label}</div>
          {d.events.map((e, i) => {
            const dot = KIND_DOT[e.kind];
            const who = e.actor ? fmt.person(e.actor) : null;
            return (
              <div
                key={`${e.at}-${i}`}
                className="grid grid-cols-[20px_minmax(0,1fr)_auto] items-start gap-3 px-5 py-1.5 min-[640px]:grid-cols-[5.5rem_20px_minmax(0,1fr)]"
              >
                <time
                  dateTime={e.at}
                  className="col-start-3 row-start-1 whitespace-nowrap text-small text-muted min-[640px]:col-start-1"
                >
                  {fmt.relativeTime(e.at, true)}
                </time>
                <span className="col-start-1 row-start-1 min-[640px]:col-start-2">
                  {e.actor ? <Avatar person={e.actor} size="sm" /> : <span aria-hidden className="block h-5 w-5 rounded-full border border-dashed border-rule-strong" />}
                </span>
                <div className="col-start-2 row-start-1 min-w-0 text-small min-[640px]:col-start-3">
                  {e.kind !== "comment" && (
                    <span className="mr-1.5 inline-flex items-center gap-1.5 text-ink-soft">
                      {dot && <Dot className={dot} />}
                      {av.kind[e.kind]}
                    </span>
                  )}
                  {who && <b className="font-semibold">{who}</b>}{" "}
                  {e.kind === "comment" && e.issue && (
                    <>
                      <span className="text-muted">{av.commentBefore}</span>
                      {issueRef(e.issue)}
                      {av.commentAfter && <span className="text-muted">{av.commentAfter}</span>}
                      {e.snippet && <p className="mt-1 border-l-2 border-rule-strong pl-2.5 text-small text-ink-soft">{e.snippet}</p>}
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
