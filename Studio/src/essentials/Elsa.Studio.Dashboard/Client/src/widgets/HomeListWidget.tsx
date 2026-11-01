import React from "react";
import { ArrowRight, CheckCheck, CircleAlert, CircleCheck, Clock3, Inbox, Network, Play, Plus, Workflow, type LucideIcon } from "lucide-react";
import { qualifyStudioBackendPath, type StudioDashboardWidgetBodyProps, type StudioDashboardWidgetSize } from "@elsa-workflows/studio-sdk";
import { StatusChip } from "@elsa-workflows/studio-ui";
import type { HomeRunSummary, HomeWorkflowSummary } from "./homeData";
import { navigateStudioPath, runStatusTone } from "./homeData";

const previewRows: Record<StudioDashboardWidgetSize, number> = { small: 3, medium: 4, wide: 6, full: 8 };

// Compact, linked records give the author a route back to their work. Status has both an icon
// and a caption; technical identities remain secondary and can wrap at any widget size.
export function QuickActionsWidget({runtimeAvailable=true}:{runtimeAvailable?:boolean}) {
  return (
    <div className="home-actions">
      <ul className="home-quick-actions">
        <li><HomeLink href="/workflows/definitions" className="home-action home-action--primary">
          <span className="home-action-icon"><Plus size={22} aria-hidden="true" /></span>
          <span className="home-action-copy"><strong>Create a workflow</strong><span>Build your next automation.</span></span>
          <ArrowRight size={18} aria-hidden="true" />
        </HomeLink></li>
        <li><HomeLink href="/workflows/definitions" className="home-action">
          <span className="home-action-icon"><Workflow size={22} aria-hidden="true" /></span>
          <span className="home-action-copy"><strong>Open the designer</strong><span>Pick up a workflow and keep building.</span></span>
          <ArrowRight size={18} aria-hidden="true" />
        </HomeLink></li>
      </ul>
      {runtimeAvailable ? <HomeLink href="/workflows/instances" className="home-action-shortcut"><Play size={15} aria-hidden="true" /><span>Inspect workflow runs</span><ArrowRight size={15} aria-hidden="true" /></HomeLink> : null}
    </div>
  );
}

export function RecentWorkflowsWidget({ snapshot, size }: StudioDashboardWidgetBodyProps<HomeWorkflowSummary[], unknown>) {
  const workflows = snapshot ?? [];
  if (!workflows.length) return <HomeEmptyState icon={Workflow} title="No workflows yet" description="Your next automation starts with a workflow." action={{ href: "/workflows/definitions", label: "Create a workflow" }} />;
  return (
    <div className="home-records">
      <ul className="home-widget-list" aria-label="Recent workflows">
        {workflows.slice(0, previewRows[size]).map(item => <li key={item.id}>
          <HomeLink href={`/workflows/definitions?definition=${encodeURIComponent(item.id)}`} className="home-record">
            <span className="home-record-icon"><Network size={18} aria-hidden="true" /></span>
            <span className="home-record-copy">
              <strong className="home-record-title">{item.name}</strong>
              {item.description ? <span className="home-record-description">{item.description}</span> : null}
              <span className="home-record-meta">{item.latestVersion ? <span>v{item.latestVersion}</span> : null}<RecordTime value={item.lastModifiedAt ?? item.createdAt} label={item.lastModifiedAt ? "Updated" : "Created"} /></span>
            </span>
            <span className="home-record-trailing">{item.publicationState ? <StatusChip tone={item.publicationState.toLowerCase() === "published" ? "success" : "neutral"}>{item.publicationState}</StatusChip> : null}<ArrowRight size={16} aria-hidden="true" /></span>
          </HomeLink>
        </li>)}
      </ul>
      <HomeLink href="/workflows/definitions" className="home-browse-link">Browse workflows<ArrowRight size={15} aria-hidden="true" /></HomeLink>
    </div>
  );
}

export function RecentRunsWidget({ snapshot, size }: StudioDashboardWidgetBodyProps<HomeRunSummary[], unknown>) {
  return <RunRecords items={snapshot ?? []} size={size} />;
}

export function NeedsAttentionWidget({ snapshot, size }: StudioDashboardWidgetBodyProps<HomeRunSummary[], unknown>) {
  return <RunRecords items={snapshot ?? []} size={size} attention />;
}

export function TasksWidget() {
  return <HomeEmptyState icon={Inbox} title="Open tasks" description="Assigned work lives in Tasks." action={{ href: "/tasks", label: "Open tasks" }} />;
}

function RunRecords({ items, size, attention = false }: { items: HomeRunSummary[]; size: StudioDashboardWidgetSize; attention?: boolean }) {
  if (!items.length) return attention
    ? <HomeEmptyState icon={CheckCheck} title="All clear" description="No runs need your attention right now." tone="success" />
    : <HomeEmptyState icon={Play} title="No runs yet" description="Run a workflow, then follow its progress here." action={{ href: "/workflows/definitions", label: "Open the designer" }} />;
  return <div className="home-records">
    <ul className="home-widget-list" aria-label={attention ? "Runs needing attention" : "Recent runs"}>
      {items.slice(0, previewRows[size]).map(item => {
        const tone = attention ? "danger" : runStatusTone(item.status);
        const Icon = attention ? CircleAlert : runIcon(item.status);
        const incidents = item.activeIncidentCount ?? item.blockingIncidentCount;
        return <li key={item.id}>
          <HomeLink href={`/workflows/instances/${encodeURIComponent(item.id)}`} className="home-record">
            <span className="home-record-icon" data-tone={tone}><Icon size={18} aria-hidden="true" /></span>
            <span className="home-record-copy">
              <strong className="home-record-title">{item.name && item.name !== item.id ? item.name : "Workflow run"}</strong>
              <span className="home-record-id" title={item.id}>{item.id}</span>
              <span className="home-record-meta"><RecordTime value={item.completedAt ?? item.startedAt ?? item.createdAt} label={item.completedAt ? "Completed" : item.startedAt ? "Started" : "Created"} />{incidents ? <span className="home-incident-count">{incidents} active {incidents === 1 ? "incident" : "incidents"}</span> : null}</span>
            </span>
            <span className="home-record-trailing">{item.status ? <StatusChip tone={runStatusTone(item.status)}>{item.status}</StatusChip> : null}<ArrowRight size={16} aria-hidden="true" /></span>
          </HomeLink>
        </li>;
      })}
    </ul>
    <HomeLink href="/workflows/instances" className="home-browse-link">{attention ? "Inspect workflow runs" : "Browse runs"}<ArrowRight size={15} aria-hidden="true" /></HomeLink>
  </div>;
}

function runIcon(status: string | undefined): LucideIcon {
  switch (runStatusTone(status)) {
    case "success": return CircleCheck;
    case "danger": return CircleAlert;
    case "warning": return Clock3;
    default: return Play;
  }
}

function RecordTime({ value, label }: { value?: string; label: string }) {
  if (!value) return null;
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return null;
  return <time dateTime={value} title={date.toLocaleString()}>{label} {date.toLocaleDateString(undefined, { month: "short", day: "numeric" })}, {date.toLocaleTimeString(undefined, { hour: "2-digit", minute: "2-digit" })}</time>;
}

function HomeEmptyState({ icon: Icon, title, description, action, tone }: { icon: LucideIcon; title: string; description: string; action?: { href: string; label: string }; tone?: string }) {
  return <div className="home-empty" role="status" data-tone={tone}>
    <span className="home-empty-icon"><Icon size={27} aria-hidden="true" /></span>
    <strong>{title}</strong><span>{description}</span>
    {action ? <HomeLink href={action.href} className="home-empty-action">{action.label}<ArrowRight size={15} aria-hidden="true" /></HomeLink> : null}
  </div>;
}

function HomeLink({ href, className, children }: { href: string; className?: string; children: React.ReactNode }) {
  const targetHref=qualifyStudioBackendPath(href);
  return <a href={targetHref} className={className} onClick={event => {
    if (event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
    event.preventDefault();
    navigateStudioPath(targetHref);
  }}>{children}</a>;
}
