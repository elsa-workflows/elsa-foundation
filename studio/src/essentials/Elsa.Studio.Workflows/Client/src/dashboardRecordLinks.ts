import type { StudioDashboardRecordLinkContribution } from "@elsa-workflows/studio-sdk";

export const workflowRunsDatasetId = "elsa.workflows.runs";

export const workflowRunDashboardRecordLink: StudioDashboardRecordLinkContribution = {
  id: "workflows.dashboard-record-links.run",
  order: 10,
  datasetIds: [workflowRunsDatasetId],
  columnIds: ["executionId"],
  requiresBackend: [{ id: "elsa.api.runtime", contractVersion: "1" }],
  resolveLink({ cell }) {
    const id = cell.value?.trim();
    if (!id) return null;
    return {
      path: `/workflows/instances/${encodeURIComponent(id)}`,
      title: `Open workflow run ${id}`,
      label: "Open workflow run"
    };
  }
};

export const workflowDefinitionDashboardRecordLink: StudioDashboardRecordLinkContribution = {
  id: "workflows.dashboard-record-links.definition",
  order: 20,
  datasetIds: [workflowRunsDatasetId],
  columnIds: ["definitionId"],
  requiresBackend: [{ id: "elsa.api.workflow-design", contractVersion: "1" }],
  resolveLink({ cell }) {
    const id = cell.value?.trim();
    if (!id) return null;
    return {
      path: `/workflows/definitions?definition=${encodeURIComponent(id)}`,
      title: `Open workflow definition ${id}`,
      label: "Open workflow definition"
    };
  }
};

export const workflowDashboardRecordLinks: readonly StudioDashboardRecordLinkContribution[] = [
  workflowRunDashboardRecordLink,
  workflowDefinitionDashboardRecordLink
];
