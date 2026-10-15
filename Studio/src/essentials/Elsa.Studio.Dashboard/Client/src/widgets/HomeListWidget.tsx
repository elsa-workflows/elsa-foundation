import React from "react";
import { requestStudioNavigation, type StudioDashboardWidgetBodyProps } from "@elsa-workflows/studio-sdk";
import type { HomeRunSummary, HomeWorkflowSummary } from "./homeData";

export function RecentWorkflowsWidget({ snapshot }: StudioDashboardWidgetBodyProps<HomeWorkflowSummary[], unknown>) {
  return (
    <HomeLinkList
      items={(snapshot ?? []).map(item => ({
        id: item.id,
        label: item.name,
        href: `/workflows/definitions/${encodeURIComponent(item.id)}`
      }))}
      empty="No workflows yet. Create one to see it here."
    />
  );
}

export function RecentRunsWidget({ snapshot }: StudioDashboardWidgetBodyProps<HomeRunSummary[], unknown>) {
  return (
    <HomeLinkList
      items={(snapshot ?? []).map(item => ({
        id: item.id,
        label: item.status ? `${item.name} · ${item.status}` : item.name,
        href: `/workflows/instances/${encodeURIComponent(item.id)}`
      }))}
      empty="No runs yet. Run a workflow to see it here."
    />
  );
}

export function NeedsAttentionWidget({ snapshot }: StudioDashboardWidgetBodyProps<HomeRunSummary[], unknown>) {
  return (
    <HomeLinkList
      items={(snapshot ?? []).map(item => ({
        id: item.id,
        label: item.status ? `${item.name} · ${item.status}` : item.name,
        href: `/workflows/instances?incidentHealth=active`
      }))}
      empty="All clear — nothing needs attention."
    />
  );
}

function HomeLinkList({ items, empty }: { items: Array<{ id: string; label: string; href: string }>; empty: string }) {
  if (!items.length) return <p className="home-widget-empty">{empty}</p>;
  return (
    <ul className="home-widget-list">
      {items.map(item => (
        <li key={item.id}>
          <a
            href={item.href}
            onClick={event => {
              event.preventDefault();
              requestStudioNavigation(window.location.pathname, item.href);
              window.history.pushState({}, "", item.href);
              window.dispatchEvent(new PopStateEvent("popstate"));
            }}
          >
            {item.label}
          </a>
        </li>
      ))}
    </ul>
  );
}
