import React from "react";
import type { StudioDashboardWidgetBodyProps } from "@elsa-workflows/studio-sdk";
import type { WorkflowPortfolioSnapshot } from "./workflowDashboardApi";
export function WorkflowPortfolioWidget({ snapshot }: StudioDashboardWidgetBodyProps<WorkflowPortfolioSnapshot, unknown>) {
  if (!snapshot) return null;
  if (snapshot.status === "unavailable") return <div className="workflow-dashboard-unavailable" role="status">Portfolio statistics are unavailable{snapshot.errorCode ? ` (${snapshot.errorCode})` : ""}.</div>;
  return <div className="workflow-portfolio-kpis"><Kpi label="Active" value={snapshot.activeDefinitionCount}/><Kpi label="Published" value={snapshot.publishedDefinitionCount}/><Kpi label="Unpublished drafts" value={snapshot.unpublishedDraftCount}/><Kpi label="Invalid drafts" value={snapshot.invalidDraftCount}/><p>Published, draft, and invalid counters overlap.</p></div>;
}
function Kpi({label,value}:{label:string;value:number}) { return <div><strong>{value}</strong><span>{label}</span></div>; }
