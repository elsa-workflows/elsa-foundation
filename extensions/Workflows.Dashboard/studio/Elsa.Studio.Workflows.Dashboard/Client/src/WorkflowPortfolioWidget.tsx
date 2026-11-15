import React from "react";
import type { StudioDashboardWidgetBodyProps } from "@elsa-workflows/studio-sdk";
import type { WorkflowPortfolioSnapshot } from "./workflowDashboardApi";
import { WorkflowDashboardMetric } from "./WorkflowDashboardMetric";
import { WorkflowDashboardUnavailable } from "./WorkflowDashboardUnavailable";

export function WorkflowPortfolioWidget({ snapshot }: StudioDashboardWidgetBodyProps<WorkflowPortfolioSnapshot, unknown>) {
  if (!snapshot) return null;

  if (snapshot.status === "unavailable") {
    return <WorkflowDashboardUnavailable title="Portfolio statistics are unavailable." errorCode={snapshot.errorCode} />;
  }

  return (
    <div className="workflow-portfolio">
      <dl className="workflow-dashboard-metrics" aria-label="Workflow definition counts">
        <WorkflowDashboardMetric label="Active definitions" value={snapshot.activeDefinitionCount} detail="Not deleted" tone="accent" />
        <WorkflowDashboardMetric label="Published" value={snapshot.publishedDefinitionCount} detail="Available to run" tone="success" />
        <WorkflowDashboardMetric label="Unpublished drafts" value={snapshot.unpublishedDraftCount} detail="Changes not yet published" tone="warning" />
        <WorkflowDashboardMetric label="Invalid drafts" value={snapshot.invalidDraftCount} detail="Drafts with validation issues" tone="danger" />
      </dl>
      <p className="workflow-dashboard-note" role="note">
        Counts can overlap: a published workflow can also have an unpublished draft.
      </p>
    </div>
  );
}
