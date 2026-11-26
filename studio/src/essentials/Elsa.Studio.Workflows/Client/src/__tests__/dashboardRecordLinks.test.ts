import { describe, expect, it } from "vitest";
import {
  workflowDashboardRecordLinks,
  workflowDefinitionDashboardRecordLink,
  workflowRunDashboardRecordLink,
  workflowRunsDatasetId
} from "../dashboardRecordLinks";

describe("workflow dashboard record links", () => {
  it("resolves workflow executionId cells to the workflow run inspector route", () => {
    const link = workflowRunDashboardRecordLink.resolveLink({
      datasetId: workflowRunsDatasetId,
      column: { id: "executionId", label: "Run", type: "string" },
      cell: { type: "string", value: "run/019b-abc" },
      row: { executionId: { type: "string", value: "run/019b-abc" } }
    });

    expect(link).toEqual({
      path: "/workflows/instances/run%2F019b-abc",
      title: "Open workflow run run/019b-abc",
      label: "Open workflow run"
    });
    expect(workflowRunDashboardRecordLink.requiresBackend).toEqual([{ id: "elsa.api.runtime", contractVersion: "1" }]);
  });

  it("resolves workflow definitionId cells to the workflow definition editor route", () => {
    const link = workflowDefinitionDashboardRecordLink.resolveLink({
      datasetId: workflowRunsDatasetId,
      column: { id: "definitionId", label: "Workflow definition", type: "string" },
      cell: { type: "string", value: "activity-def-42" },
      row: { definitionId: { type: "string", value: "activity-def-42" } }
    });

    expect(link).toEqual({
      path: "/workflows/definitions?definition=activity-def-42",
      title: "Open workflow definition activity-def-42",
      label: "Open workflow definition"
    });
    expect(workflowDefinitionDashboardRecordLink.requiresBackend).toEqual([{ id: "elsa.api.workflow-design", contractVersion: "1" }]);
  });

  it("returns null for null or blank identifiers", () => {
    for (const contributor of workflowDashboardRecordLinks) {
      expect(contributor.resolveLink({
        datasetId: workflowRunsDatasetId,
        column: { id: contributor.columnIds![0], label: "ID", type: "string" },
        cell: { type: "string", value: null, nullReason: "missing" },
        row: {}
      })).toBeNull();
      expect(contributor.resolveLink({
        datasetId: workflowRunsDatasetId,
        column: { id: contributor.columnIds![0], label: "ID", type: "string" },
        cell: { type: "string", value: "   " },
        row: {}
      })).toBeNull();
    }
  });
});
