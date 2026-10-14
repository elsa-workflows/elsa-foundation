import React from "react";
import { type StudioDashboardWidgetBodyProps } from "@elsa-workflows/studio-sdk";
import { StatusChip } from "@elsa-workflows/studio-ui";
import type { HomeRunSummary, HomeWorkflowSummary } from "./homeData";
import { navigateStudioPath, runStatusTone } from "./homeData";

export function QuickActionsWidget() {
  return (
    <ul className="home-quick-actions">
      <li>
        <a
          href="/workflows/definitions"
          onClick={event => { event.preventDefault(); navigateStudioPath("/workflows/definitions"); }}
        >
          Create a workflow
        </a>
      </li>
      <li>
        <a
          href="/workflows/definitions"
          onClick={event => { event.preventDefault(); navigateStudioPath("/workflows/definitions"); }}
        >
          Open the designer
        </a>
      </li>
    </ul>
  );
}

export function RecentWorkflowsWidget({ snapshot }: StudioDashboardWidgetBodyProps<HomeWorkflowSummary[], unknown>) {
  return (
    <HomeLinkList
      items={(snapshot ?? []).map(item => ({
        id: item.id,
        label: item.name,
        href: `/workflows/definitions/${encodeURIComponent(item.id)}`
      }))}
      emptyTitle="No workflows yet"
      emptyDescription="Create a workflow definition to see it here."
      emptyAction={{ href: "/workflows/definitions", label: "Create a workflow" }}
    />
  );
}

export function RecentRunsWidget({ snapshot }: StudioDashboardWidgetBodyProps<HomeRunSummary[], unknown>) {
  return (
    <HomeLinkList
      items={(snapshot ?? []).map(item => ({
        id: item.id,
        label: item.name,
        href: `/workflows/instances/${encodeURIComponent(item.id)}`,
        status: item.status
      }))}
      emptyTitle="No runs yet"
      emptyDescription="Run a workflow to see its status here."
      emptyAction={{ href: "/workflows/definitions", label: "Open the designer" }}
    />
  );
}

export function NeedsAttentionWidget({ snapshot }: StudioDashboardWidgetBodyProps<HomeRunSummary[], unknown>) {
  return (
    <HomeLinkList
      items={(snapshot ?? []).map(item => ({
        id: item.id,
        label: item.name,
        href: "/workflows/instances?incidentHealth=active",
        status: item.status
      }))}
      emptyTitle="All clear"
      emptyDescription="Nothing needs attention."
    />
  );
}

export function TasksWidget() {
  return (
    <div className="home-empty" role="status">
      <strong>Open tasks</strong>
      <span>Assigned work lives in Tasks.</span>
      <a
        href="/tasks"
        onClick={event => {
          event.preventDefault();
          navigateStudioPath("/tasks");
        }}
      >
        Open tasks
      </a>
    </div>
  );
}

function HomeLinkList({
  items,
  emptyTitle,
  emptyDescription,
  emptyAction
}: {
  items: Array<{ id: string; label: string; href: string; status?: string }>;
  emptyTitle: string;
  emptyDescription: string;
  emptyAction?: { href: string; label: string };
}) {
  if (!items.length) {
    return (
      <div className="home-empty" role="status">
        <strong>{emptyTitle}</strong>
        <span>{emptyDescription}</span>
        {emptyAction ? (
          <a
            href={emptyAction.href}
            onClick={event => {
              event.preventDefault();
              navigateStudioPath(emptyAction.href);
            }}
          >
            {emptyAction.label}
          </a>
        ) : null}
      </div>
    );
  }
  return (
    <ul className="home-widget-list">
      {items.map(item => (
        <li key={item.id}>
          <a
            href={item.href}
            onClick={event => {
              event.preventDefault();
              navigateStudioPath(item.href);
            }}
          >
            <span>{item.label}</span>
            {item.status ? <StatusChip tone={runStatusTone(item.status)}>{item.status}</StatusChip> : null}
          </a>
        </li>
      ))}
    </ul>
  );
}
