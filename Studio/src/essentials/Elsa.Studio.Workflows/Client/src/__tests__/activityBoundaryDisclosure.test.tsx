import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { createEndpointContext } from "@elsa-workflows/studio-sdk";
import { afterEach, describe, expect, it, vi } from "vitest";
import { getActivityExecutionInspection } from "../api/runtime";
import { WorkflowActivityExecutionDetails } from "../workflow-editor/WorkflowInstances";
import type {
  ActivityExecutionInspection,
  ActivityExecutionStateSummary
} from "../workflowTypes";

vi.mock("../api/runtime", async importOriginal => ({
  ...(await importOriginal<typeof import("../api/runtime")>()),
  getActivityExecutionInspection: vi.fn()
}));

vi.mock("../workflow-editor/ReusableBoundaryInspector", () => ({
  ReusableBoundaryInspector: ({ inspection }: { inspection: ActivityExecutionInspection }) => (
    <section aria-label="Reusable boundary Runtime Evidence">
      <h4>Boundary lifecycle</h4>
      <p>Exact version {inspection.boundary?.version} ({inspection.boundary?.definitionVersionId})</p>
    </section>
  )
}));

let active: { root: Root; container: HTMLElement } | null = null;

afterEach(() => {
  if (active) {
    flushSync(() => active!.root.unmount());
    active.container.remove();
    active = null;
  }
  vi.mocked(getActivityExecutionInspection).mockReset();
});

function render(ui: React.ReactElement) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  flushSync(() => root.render(ui));
  active = { root, container };
  return container;
}

function rerender(ui: React.ReactElement) {
  if (!active) throw new Error("No active render to update.");
  flushSync(() => active!.root.render(ui));
  return active.container;
}

async function waitFor(assertion: () => void) {
  let lastError: unknown;
  for (let attempt = 0; attempt < 100; attempt++) {
    try {
      flushSync(() => {});
      assertion();
      return;
    } catch (error) {
      lastError = error;
      await new Promise(resolve => setTimeout(resolve, 0));
    }
  }
  throw lastError;
}

const activity = (activityExecutionId: string, workflowExecutionId: string): ActivityExecutionStateSummary => ({
  activityExecutionId,
  workflowExecutionId,
  executableNodeId: `node-${activityExecutionId}`,
  authoredActivityId: "reusable-step",
  activityType: "Example.ReusableActivity",
  activityTypeVersion: "1.0.0",
  status: "Completed",
  subStatus: null,
  scheduledAt: "2026-10-18T10:00:00Z",
  startedAt: "2026-10-18T10:00:01Z",
  completedAt: "2026-10-18T10:00:02Z",
  schedulingActivityExecutionId: null,
  parentActivityExecutionId: null,
  branchId: null,
  iterationId: null,
  callStackDepth: null,
  bookmarkIds: [],
  incidentIds: [],
  metadata: {}
});

function boundaryInspection(activityExecutionId: string, workflowExecutionId: string, definitionId: string): ActivityExecutionInspection {
  const definitionVersionId = `${definitionId}-version`;
  return {
    ...activity(activityExecutionId, workflowExecutionId),
    executionSequence: 1,
    lastCommittedAt: "2026-10-18T10:00:02Z",
    provenance: { metadata: {} },
    outcomeNames: [],
    bookmarks: [],
    incidents: [],
    valueSnapshots: [],
    boundary: {
      kind: "ReusableActivity",
      definitionId,
      definitionVersionId,
      version: "1.0.0",
      templateHash: "sha256:template",
      invocationOrigin: [{ kind: "TemplateBoundary", id: definitionVersionId }],
      executionScopeId: `scope-${activityExecutionId}`,
      hasChildren: true,
      directChildCount: 1,
      committedDescendantCount: 1,
      aggregate: {
        status: "Completed",
        total: 1,
        scheduled: 0,
        running: 0,
        suspended: 0,
        completed: 1,
        faulted: 0,
        cancelled: 0,
        blockingIncidentCount: 0,
        retryCount: 0,
        lastExecutionSequence: 1
      },
      layoutAvailable: true
    },
    metadata: {}
  } as ActivityExecutionInspection;
}

describe("activity boundary disclosure", () => {
  it("mounts the inspector only when opened and resets after endpoint and activity changes", async () => {
    await import("../workflow-editor/ReusableBoundaryInspector");
    const context = createEndpointContext("https://one.example");
    const nextContext = createEndpointContext("https://two.example");
    const firstActivity = activity("ae-1", "wf-1");
    const nextActivity = activity("ae-2", "wf-2");

    vi.mocked(getActivityExecutionInspection).mockImplementation(async (_context, workflowExecutionId, activityExecutionId) =>
      boundaryInspection(activityExecutionId, workflowExecutionId, `definition-${activityExecutionId}`));

    const container = render(
      <WorkflowActivityExecutionDetails context={context} activity={firstActivity} activityCatalog={[]} />
    );
    const findBoundaryDetails = () => [...container.querySelectorAll<HTMLElement>("summary")]
      .find(summary => summary.textContent?.includes("Boundary details"));
    await waitFor(() => expect(findBoundaryDetails()).toBeTruthy());

    expect(container.querySelector('[aria-label="Reusable boundary Runtime Evidence"]')).toBeNull();

    const openBoundary = async (openContext: ReturnType<typeof createEndpointContext>, openActivity: ActivityExecutionStateSummary) => {
      const summary = findBoundaryDetails();
      expect(summary).toBeTruthy();
      flushSync(() => summary!.click());
      await waitFor(() => expect((summary!.parentElement as HTMLDetailsElement).open).toBe(true));
      await waitFor(() => expect(
        container.querySelector('[aria-label="Reusable boundary Runtime Evidence"]') ||
        container.textContent?.includes("Loading boundary details...")
      ).toBeTruthy());
      rerender(<WorkflowActivityExecutionDetails context={openContext} activity={openActivity} activityCatalog={[]} />);
      await waitFor(() => expect(container.querySelector('[aria-label="Reusable boundary Runtime Evidence"]')).toBeTruthy());
      expect(container.textContent).toContain("Boundary lifecycle");
    };

    await openBoundary(context, firstActivity);
    expect(container.textContent).toContain("Exact version 1.0.0 (definition-ae-1-version)");

    rerender(
      <WorkflowActivityExecutionDetails context={nextContext} activity={firstActivity} activityCatalog={[]} />
    );
    await waitFor(() => expect(getActivityExecutionInspection).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(findBoundaryDetails()).toBeTruthy());
    expect((findBoundaryDetails()!.parentElement as HTMLDetailsElement).open).toBe(false);
    expect(container.querySelector('[aria-label="Reusable boundary Runtime Evidence"]')).toBeNull();

    await openBoundary(nextContext, firstActivity);
    expect(container.textContent).toContain("Exact version 1.0.0 (definition-ae-1-version)");

    rerender(
      <WorkflowActivityExecutionDetails context={nextContext} activity={nextActivity} activityCatalog={[]} />
    );
    await waitFor(() => expect(getActivityExecutionInspection).toHaveBeenCalledTimes(3));
    await waitFor(() => expect(findBoundaryDetails()).toBeTruthy());
    expect((findBoundaryDetails()!.parentElement as HTMLDetailsElement).open).toBe(false);
    expect(container.querySelector('[aria-label="Reusable boundary Runtime Evidence"]')).toBeNull();
  });
});
