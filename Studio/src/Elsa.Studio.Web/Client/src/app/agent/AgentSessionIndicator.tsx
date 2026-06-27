import React from "react";

export type AgentSessionIndicatorStatus = "active" | "background" | "waiting" | "blocked" | "failed" | "completed";

export interface AgentSessionIndicatorSession {
  id: string;
  title: string;
  status: AgentSessionIndicatorStatus;
  pendingProposals?: number;
  pendingArtifacts?: number;
  pendingPrompts?: number;
  childSessions?: AgentSessionIndicatorSession[];
}

export interface AgentSessionIndicatorSummary {
  total: number;
  activeOrBackground: number;
  waiting: number;
  blocked: number;
  failed: number;
  pendingProposals: number;
  pendingArtifacts: number;
  pendingPrompts: number;
  childSessions: number;
}

export function AgentSessionIndicator({ sessions }: { sessions: AgentSessionIndicatorSession[] }) {
  const summary = summarizeAgentSessions(sessions);
  if (summary.total === 0) {
    return null;
  }

  return (
    <span className="agent-session-indicator" aria-label={formatIndicatorLabel(summary)}>
      <span>{summary.activeOrBackground}</span>
      {summary.waiting > 0 ? <span data-state="waiting">{summary.waiting} waiting</span> : null}
      {summary.blocked > 0 ? <span data-state="blocked">{summary.blocked} blocked</span> : null}
      {summary.failed > 0 ? <span data-state="failed">{summary.failed} failed</span> : null}
      {summary.pendingProposals > 0 ? <span>{formatCount(summary.pendingProposals, "proposal")}</span> : null}
      {summary.pendingArtifacts > 0 ? <span>{formatCount(summary.pendingArtifacts, "artifact")}</span> : null}
      {summary.pendingPrompts > 0 ? <span>{formatCount(summary.pendingPrompts, "prompt")}</span> : null}
      {summary.childSessions > 0 ? <span>{formatCount(summary.childSessions, "child", "children")}</span> : null}
    </span>
  );
}

export function summarizeAgentSessions(sessions: AgentSessionIndicatorSession[]): AgentSessionIndicatorSummary {
  const summary: AgentSessionIndicatorSummary = {
    total: 0,
    activeOrBackground: 0,
    waiting: 0,
    blocked: 0,
    failed: 0,
    pendingProposals: 0,
    pendingArtifacts: 0,
    pendingPrompts: 0,
    childSessions: 0
  };

  for (const session of sessions) {
    addSession(summary, session, false);
  }

  return summary;
}

function addSession(summary: AgentSessionIndicatorSummary, session: AgentSessionIndicatorSession, isChild: boolean) {
  summary.total += 1;
  if (isChild) {
    summary.childSessions += 1;
  }

  if (session.status === "active" || session.status === "background") {
    summary.activeOrBackground += 1;
  } else if (session.status === "waiting") {
    summary.waiting += 1;
  } else if (session.status === "blocked") {
    summary.blocked += 1;
  } else if (session.status === "failed") {
    summary.failed += 1;
  }

  summary.pendingProposals += session.pendingProposals ?? 0;
  summary.pendingArtifacts += session.pendingArtifacts ?? 0;
  summary.pendingPrompts += session.pendingPrompts ?? 0;

  for (const child of session.childSessions ?? []) {
    addSession(summary, child, true);
  }
}

function formatIndicatorLabel(summary: AgentSessionIndicatorSummary) {
  const parts = [`${summary.activeOrBackground} active or background Weaver sessions`];
  if (summary.waiting > 0) parts.push(`${summary.waiting} waiting`);
  if (summary.blocked > 0) parts.push(`${summary.blocked} blocked`);
  if (summary.failed > 0) parts.push(`${summary.failed} failed`);
  if (summary.pendingProposals > 0) parts.push(`${summary.pendingProposals} pending proposals`);
  if (summary.pendingArtifacts > 0) parts.push(`${summary.pendingArtifacts} pending artifacts`);
  if (summary.pendingPrompts > 0) parts.push(`${summary.pendingPrompts} pending prompts`);
  if (summary.childSessions > 0) parts.push(`${summary.childSessions} child sessions`);
  return parts.join(", ");
}

function formatCount(count: number, singular: string, plural = `${singular}s`) {
  return `${count} ${count === 1 ? singular : plural}`;
}
