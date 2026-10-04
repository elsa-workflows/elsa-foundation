import React from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { buildExecutableActivityGraph } from "../executableGraph";
import {
  getIncidentRootCause,
  getIncidentHealthActionState,
  combineActivityExecutions,
  loadExactIncidentActivitySummary,
  loadActiveIncidentActivitySummaries,
  incidentActivityLookupFailureMessage,
  focusSelectedCanvasActivity,
  projectPinnedExecutable,
  resolveIncidentActivityAssociation,
  resolveInitialActivityEvidenceId,
  WorkflowIncidentList
} from "../workflow-editor/WorkflowInstances";
import type { ActivityCatalogItem, ActivityExecutionStateSummary, IncidentStateSummary, WorkflowExecutableDetails, WorkflowExecutableNode, WorkflowInstanceDetails } from "../workflowTypes";

let mounted: { root: Root; container: HTMLDivElement } | null = null;

afterEach(() => {
  if (!mounted) return;
  flushSync(() => mounted!.root.unmount());
  mounted.container.remove();
  mounted = null;
});

describe("Runtime-pinned workflow instance rendering", () => {
  it("projects the executable pinned by artifactId without a Design version", () => {
    const executable = {
      artifactId: "artifact-pinned",
      artifactHash: "sha256:pinned",
      createdAt: "2026-07-01T00:00:00Z",
      rootActivityType: "Example.Root",
      rootActivityVersion: "1.0.0",
      nodeCount: 1,
      resumeTargetCount: 0,
      rootActivity: {
        executableNodeId: "exec-root",
        authoredActivityId: "authored-root",
        activityType: "Example.Root",
        activityTypeVersion: "1.0.0",
        inputBindings: [],
        childSlots: []
      },
      chosenReference: {
        sourceReferenceId: "reference-1",
        selection: "requested",
        layout: [],
        activityPresentation: [{
          executableNodeId: "exec-root",
          displayName: "Frozen root",
          description: "Published wording"
        }]
      },
      references: []
    } satisfies WorkflowExecutableDetails;
    const details = {
      instance: {
        workflowExecutionId: "execution-1",
        artifactId: "artifact-pinned",
        artifactVersion: "3.0.0",
        artifactHash: "sha256:pinned",
        definitionId: "definition-1",
        definitionVersionId: "draft:synthetic",
        status: "Running",
        createdAt: "2026-07-01T00:00:00Z",
        activityCount: 0,
        incidentCount: 0
      },
      activities: [],
      incidents: []
    } satisfies WorkflowInstanceDetails;

    const projected = projectPinnedExecutable(executable, details, []);

    expect(projected.definition.description).toContain("artifact-pinned");
    expect(projected.state.rootActivity).toMatchObject({
      nodeId: "authored-root",
      activityVersionId: "executable-missing:Example.Root@1.0.0"
    });
    expect(projected.id).toBe("draft:synthetic");
    expect(projected.activityPresentation).toEqual([{
      nodeId: "authored-root",
      displayName: "Frozen root",
      description: "Published wording"
    }]);
  });

  it("focuses only an outer activity execution that exists in the loaded Runtime Evidence", () => {
    const activities = [
      { activityExecutionId: "outer-activity" },
      { activityExecutionId: "child-activity" }
    ];

    expect(resolveInitialActivityEvidenceId(activities, "outer-activity"))
      .toBe("outer-activity");
    expect(resolveInitialActivityEvidenceId(activities, "foreign-activity"))
      .toBeNull();
    expect(resolveInitialActivityEvidenceId(activities, null))
      .toBeNull();
  });

  it("uses an incident execution ID for exact occurrence and leaves node-only repeats unguessed", () => {
    const first = activityExecution({ activityExecutionId: "repeat-1" });
    const second = activityExecution({ activityExecutionId: "repeat-2" });
    const direct = incident({ activityExecutionId: "repeat-2", executableNodeId: "exec-node" });
    const nodeOnly = incident({ activityExecutionId: null, executableNodeId: "exec-node" });

    expect(resolveIncidentActivityAssociation(direct, [first, second])).toEqual({
      nodeId: "authored-node",
      activityExecution: second
    });
    expect(resolveIncidentActivityAssociation(nodeOnly, [first, second])).toEqual({
      nodeId: "authored-node",
      activityExecution: null
    });
  });

  it("maps repeated authored activity associations to the executable node shown in the pinned graph", () => {
    const child = (executableNodeId: string): WorkflowExecutableNode => ({
      executableNodeId,
      authoredActivityId: "shared-authored-node",
      activityType: "Example.Activity",
      activityTypeVersion: "1.0.0",
      inputBindings: [],
      childSlots: []
    });
    const graph = buildExecutableActivityGraph({
      executableNodeId: "root-executable",
      authoredActivityId: "root-authored",
      activityType: "Example.Root",
      activityTypeVersion: "1.0.0",
      inputBindings: [],
      childSlots: [{ name: "Sequence.Activities", activities: [child("placement-1"), child("placement-2")] }]
    }, [], [], [], null, [
      { executableNodeId: "placement-1", displayName: "First placement" },
      { executableNodeId: "placement-2", displayName: "Second placement" }
    ]);
    const first = activityExecution({
      activityExecutionId: "execution-1",
      authoredActivityId: "shared-authored-node",
      executableNodeId: "placement-1",
      incidentIds: ["relation-incident"]
    });
    const second = activityExecution({
      activityExecutionId: "execution-2",
      authoredActivityId: "shared-authored-node",
      executableNodeId: "placement-2",
      incidentIds: []
    });

    expect(resolveIncidentActivityAssociation(incident({ activityExecutionId: "execution-1" }), [first, second], graph)?.nodeId)
      .toBe("placement-1");
    expect(resolveIncidentActivityAssociation(incident({
      incidentId: "relation-incident",
      activityExecutionId: null,
      executableNodeId: null
    }), [first, second], graph)?.nodeId).toBe("placement-1");
    expect(resolveIncidentActivityAssociation(incident({
      activityExecutionId: null,
      executableNodeId: "placement-2"
    }), [first, second], graph)?.nodeId).toBe("placement-2");
    expect(resolveIncidentActivityAssociation(incident({
      activityExecutionId: null,
      executableNodeId: "shared-authored-node"
    }), [first, second], graph)).toEqual({ nodeId: null, activityExecution: null });
  });

  it("prefers pinned executable identity when a node ID collides with another node's authored ID", () => {
    const graph = buildExecutableActivityGraph({
      executableNodeId: "root-executable",
      authoredActivityId: "root-authored",
      activityType: "Example.Root",
      activityTypeVersion: "1.0.0",
      inputBindings: [],
      childSlots: [{ name: "Sequence.Activities", activities: [
        {
          executableNodeId: "first-executable",
          authoredActivityId: "shared-node-id",
          activityType: "Example.Activity",
          activityTypeVersion: "1.0.0",
          inputBindings: [],
          childSlots: []
        },
        {
          executableNodeId: "shared-node-id",
          authoredActivityId: "second-authored-node",
          activityType: "Example.Activity",
          activityTypeVersion: "1.0.0",
          inputBindings: [],
          childSlots: []
        }
      ] }]
    }, []);
    const onlyFirstExecutionIsLoaded = activityExecution({
      activityExecutionId: "first-execution",
      authoredActivityId: "shared-node-id",
      executableNodeId: "first-executable",
      incidentIds: []
    });
    const pageBoundIncident = incident({
      incidentId: "page-bound-node-only",
      activityExecutionId: "second-execution-not-loaded",
      executableNodeId: "shared-node-id"
    });

    expect(resolveIncidentActivityAssociation(pageBoundIncident, [onlyFirstExecutionIsLoaded], graph))
      .toEqual({ nodeId: "second-authored-node", activityExecution: null });
  });

  it("loads an active exact execution beyond the summary page for occurrence and input navigation", async () => {
    const first = activityExecution({ activityExecutionId: "repeat-1" });
    const second = activityExecution({ activityExecutionId: "repeat-2" });
    const targetIncident = incident({
      activityExecutionId: "repeat-3",
      executableNodeId: "compiled-node",
      metadata: { "runtime.inputKey": "greeting" }
    });
    const details = {
      instance: { workflowExecutionId: "workflow-execution-1" } as WorkflowInstanceDetails["instance"],
      activities: [first, second],
      activityNextContinuationToken: "next-activity-page",
      incidents: [targetIncident]
    } satisfies WorkflowInstanceDetails;
    const fetchInspection = vi.fn(async (activityExecutionId: string) => ({
      activityExecutionId,
      workflowExecutionId: "workflow-execution-1",
      executableNodeId: "compiled-node",
      authoredActivityId: "authored-node",
      activityType: "Example.Activity",
      activityTypeVersion: "1.0.0",
      status: "Faulted",
      scheduledAt: "2026-10-01T12:00:00Z",
      bookmarks: [],
      incidents: [{
        incidentId: "incident-1",
        severity: "Error",
        status: "Open",
        resolutionAction: "None",
        failureType: "ActivityInputFailed",
        message: "Wrapper failure",
        createdAt: "2026-10-01T12:00:00Z",
        isBlocking: true,
        metadata: {}
      }],
      metadata: {}
    }));

    const loaded = await loadActiveIncidentActivitySummaries(details, fetchInspection);
    const associationActivities = combineActivityExecutions(details.activities, loaded.activities);
    const association = resolveIncidentActivityAssociation(targetIncident, associationActivities);
    expect(fetchInspection).toHaveBeenCalledTimes(1);
    expect(fetchInspection).toHaveBeenCalledWith("repeat-3");
    expect(loaded.incomplete).toBe(false);
    expect(association).toMatchObject({ nodeId: "authored-node", activityExecution: { activityExecutionId: "repeat-3" } });

    const onViewInput = vi.fn();
    renderIncidentList([targetIncident], associationActivities, vi.fn(), onViewInput);
    click(buttonByText(mounted!.container, "View input evidence"));
    expect(onViewInput).toHaveBeenCalledWith(targetIncident);
  });

  it("retries transient inspection failures and preserves terminal reasons", async () => {
    const details: WorkflowInstanceDetails = {
      instance: { workflowExecutionId: "workflow-execution-1" } as WorkflowInstanceDetails["instance"],
      activities: [],
      incidents: [
        incident({ incidentId: "wrong-id", activityExecutionId: "requested-wrong" }),
        incident({ incidentId: "denied", activityExecutionId: "requested-denied" }),
        incident({ incidentId: "failed", activityExecutionId: "requested-failed" })
      ]
    };
    const fetchInspection = vi.fn(async (activityExecutionId: string) => {
      if (activityExecutionId === "requested-failed") throw new Error("Runtime unavailable");
      if (activityExecutionId === "requested-denied") throw Object.assign(new Error("Forbidden"), { status: 403 });
      return {
        activityExecutionId: "different-execution",
        workflowExecutionId: "workflow-execution-1",
        executableNodeId: "compiled-node",
        authoredActivityId: "authored-node",
        activityType: "Example.Activity",
        activityTypeVersion: "1.0.0",
        status: "Faulted",
        scheduledAt: "2026-10-01T12:00:00Z",
        bookmarks: [],
        incidents: [],
        metadata: {}
      };
    });

    const result = await loadActiveIncidentActivitySummaries(details, fetchInspection);

    expect(result.activities).toEqual([]);
    expect(result.incomplete).toBe(true);
    expect(result.pendingActivityExecutionIds).toEqual(["requested-failed"]);
    expect(result.unavailableFailures).toEqual([
      { activityExecutionId: "requested-wrong", reason: "identity-mismatch" },
      { activityExecutionId: "requested-denied", reason: "permission-denied" }
    ]);
  });

  it("exposes another bounded association batch for active incidents beyond the first fifty", async () => {
    const incidents = Array.from({ length: 51 }, (_, index) => incident({
      incidentId: `incident-${index + 1}`,
      activityExecutionId: `execution-${index + 1}`
    }));
    const details: WorkflowInstanceDetails = {
      instance: { workflowExecutionId: "workflow-execution-1" } as WorkflowInstanceDetails["instance"],
      activities: [],
      incidents
    };
    const fetchInspection = vi.fn(async (activityExecutionId: string) => {
      if (activityExecutionId !== "execution-51") throw new Error("Earlier activity inspection is unavailable");
      return {
        activityExecutionId,
        workflowExecutionId: "workflow-execution-1",
        executableNodeId: `compiled-${activityExecutionId}`,
        authoredActivityId: `authored-${activityExecutionId}`,
        activityType: "Example.Activity",
        activityTypeVersion: "1.0.0",
        status: "Faulted",
        scheduledAt: "2026-10-01T12:00:00Z",
        bookmarks: [],
        incidents: [],
        metadata: {}
      };
    });

    const firstBatch = await loadActiveIncidentActivitySummaries(details, fetchInspection);
    expect(firstBatch.activities).toHaveLength(0);
    expect(firstBatch.pendingActivityExecutionIds).toHaveLength(51);
    expect(firstBatch.pendingActivityExecutionIds[0]).toBe("execution-51");
    expect(firstBatch.incomplete).toBe(true);

    const nextBatch = await loadActiveIncidentActivitySummaries(
      { ...details, activities: combineActivityExecutions(details.activities, firstBatch.activities) },
      fetchInspection,
      firstBatch.pendingActivityExecutionIds);
    expect(nextBatch.activities).toHaveLength(1);
    expect(nextBatch.activities[0]?.activityExecutionId).toBe("execution-51");
    expect(nextBatch.pendingActivityExecutionIds).toHaveLength(50);
    expect(nextBatch.pendingActivityExecutionIds).not.toContain("execution-51");
    expect(nextBatch.incomplete).toBe(true);
  });

  it("loads selected execution 51 on demand for exact input navigation", async () => {
    const targetIncident = incident({
      incidentId: "incident-51",
      activityExecutionId: "execution-51",
      executableNodeId: "compiled-51",
      metadata: { "runtime.inputKey": "payload" }
    });
    const fetchInspection = vi.fn(async (activityExecutionId: string) => ({
      activityExecutionId,
      workflowExecutionId: "workflow-execution-1",
      executableNodeId: "compiled-51",
      authoredActivityId: "authored-51",
      activityType: "Example.Activity",
      activityTypeVersion: "1.0.0",
      status: "Faulted",
      scheduledAt: "2026-10-01T12:00:00Z",
      bookmarks: [],
      incidents: [],
      metadata: {}
    }));

    const exactResult = await loadExactIncidentActivitySummary("workflow-execution-1", "execution-51", fetchInspection);
    expect(fetchInspection).toHaveBeenCalledTimes(1);
    expect(fetchInspection).toHaveBeenCalledWith("execution-51");
    expect(exactResult).toMatchObject({ status: "found", summary: { activityExecutionId: "execution-51", authoredActivityId: "authored-51" } });
    const activities = combineActivityExecutions([], exactResult.status === "found" ? [exactResult.summary] : []);
    expect(resolveIncidentActivityAssociation(targetIncident, activities)?.activityExecution?.activityExecutionId).toBe("execution-51");

    const onViewInput = vi.fn();
    renderIncidentList([targetIncident], activities, vi.fn(), onViewInput);
    click(buttonByText(mounted!.container, "View input evidence"));
    expect(onViewInput).toHaveBeenCalledWith(targetIncident);
  });

  it("keeps exact inspection failures distinct for truthful operator feedback", async () => {
    const forbidden = await loadExactIncidentActivitySummary("workflow-execution-1", "execution-51", async () => {
      throw Object.assign(new Error("Forbidden"), { status: 403 });
    });
    const wrongIdentity = await loadExactIncidentActivitySummary("workflow-execution-1", "execution-51", async () => ({
      activityExecutionId: "different-execution",
      workflowExecutionId: "workflow-execution-1",
      executableNodeId: "compiled-51",
      authoredActivityId: "authored-51",
      activityType: "Example.Activity",
      activityTypeVersion: "1.0.0",
      status: "Faulted",
      scheduledAt: "2026-10-01T12:00:00Z",
      bookmarks: [],
      incidents: [],
      metadata: {}
    }));
    const unavailable = await loadExactIncidentActivitySummary("workflow-execution-1", "execution-51", async () => {
      throw new Error("Connection timed out");
    });
    const serverErrorWithPermissionWord = await loadExactIncidentActivitySummary("workflow-execution-1", "execution-51", async () => {
      throw Object.assign(new Error("Permission lookup unavailable"), { status: 500 });
    });

    expect(forbidden).toEqual({ status: "failed", reason: "permission-denied" });
    expect(wrongIdentity).toEqual({ status: "failed", reason: "identity-mismatch" });
    expect(unavailable).toEqual({ status: "failed", reason: "unavailable" });
    expect(serverErrorWithPermissionWord).toEqual({ status: "failed", reason: "unavailable" });
    expect(incidentActivityLookupFailureMessage("permission-denied")).toContain("do not have permission");
    expect(incidentActivityLookupFailureMessage("identity-mismatch")).toContain("different run or activity execution");
    expect(incidentActivityLookupFailureMessage("unavailable")).toContain("Use Show affected activity to try again");
  });

  it("shows an exact-activity inspection failure while keeping the incident available", () => {
    const targetIncident = incident({ activityExecutionId: "execution-51" });
    const message = incidentActivityLookupFailureMessage("permission-denied");
    renderIncidentList([targetIncident], [], vi.fn(), vi.fn(), [], message);

    expect(mounted?.container.querySelector('[role="status"]')?.textContent).toContain("do not have permission");
    expect(buttonByText(mounted!.container, "Show affected activity")).toBeDefined();
  });

  it("returns keyboard focus to a BPMN wrapper when the selected identity is its bound activity", () => {
    const canvas = document.createElement("div");
    canvas.className = "wf-instance-canvas";
    const wrapper = document.createElement("div");
    wrapper.dataset.id = "bpmn-element-1";
    wrapper.tabIndex = 0;
    const node = document.createElement("div");
    node.dataset.runtimeNodeId = "authored-activity-1";
    wrapper.appendChild(node);
    canvas.appendChild(wrapper);
    document.body.appendChild(canvas);

    focusSelectedCanvasActivity("authored-activity-1");

    expect(document.activeElement).toBe(wrapper);
    canvas.remove();
  });

  it("keeps an unattributed incident run-level and prefers structured root-cause metadata", () => {
    const runLevel = incident({
      activityExecutionId: null,
      executableNodeId: null,
      metadata: { "runtime.faultInnerMessage": "The input expression referenced a missing variable." }
    });

    expect(resolveIncidentActivityAssociation(runLevel, [])).toBeNull();
    expect(getIncidentRootCause(runLevel)).toBe("The input expression referenced a missing variable.");
    expect(getIncidentRootCause(incident({ metadata: {} }))).toBe("Wrapper failure");
  });

  it("offers input navigation from structured incident metadata and labels run-level evidence honestly", () => {
    const activity = activityExecution();
    const affectedIncident = incident({
      metadata: {
        "runtime.inputKey": "text",
        "runtime.inputFailureCode": "ExpressionEvaluationFailed",
        "runtime.expressionLanguage": "JavaScript",
        "runtime.inputEvaluationPhase": "Start",
        "runtime.faultInnerMessage": "The variable was not found."
      }
    });
    const onViewInput = vi.fn();
    const onShowAffectedActivity = vi.fn();
    const catalogItem = {
      activityVersionId: "example-activity@1",
      activityTypeKey: activity.activityType,
      version: "1.0.0",
      category: "Tests",
      displayName: "Write Line",
      executionType: "Activity",
      inputs: [{ referenceKey: "text", name: "Text", typeName: "string", displayName: "Text" }],
      outputs: []
    } satisfies ActivityCatalogItem;
    const newerCatalogItem = {
      ...catalogItem,
      activityVersionId: "example-activity@2",
      version: "2.0.0",
      inputs: [{ referenceKey: "text", name: "Text", typeName: "string", displayName: "Newer Text" }]
    } satisfies ActivityCatalogItem;
    renderIncidentList([affectedIncident], [activity], onShowAffectedActivity, onViewInput, [newerCatalogItem, catalogItem]);

    const summary = mounted?.container.querySelector(".wf-instance-incident-summary");
    expect(summary?.querySelector("strong")?.textContent).toBe("Write Line · Text input");
    expect(summary?.textContent).toContain("The variable was not found.");
    expect(summary?.textContent).toContain("Needs intervention · Open");
    expect(summary?.textContent).toContain("Expression language · JavaScript");
    expect(summary?.textContent).not.toContain("ExpressionEvaluationFailed");
    expect(summary?.textContent).not.toContain("Start");
    expect(mounted?.container.querySelector(".wf-incident-technical-details")?.textContent).toContain("ExpressionEvaluationFailed");
    click(buttonByText(mounted!.container, "Show affected activity"));
    click(buttonByText(mounted!.container, "View input evidence"));
    expect(onShowAffectedActivity).toHaveBeenCalledWith(affectedIncident);
    expect(onViewInput).toHaveBeenCalledWith(affectedIncident);

    renderIncidentList([incident({ metadata: { "runtime.inputKey": "legacyText" } })], [activity], vi.fn(), vi.fn(), [{
      ...catalogItem,
      inputs: [{ name: "legacyText", typeName: "string", displayName: "Legacy Text" }]
    }]);
    expect(mounted?.container.querySelector(".wf-instance-incident-summary strong")?.textContent).toBe("Write Line · Legacy Text input");

    renderIncidentList([incident({ activityExecutionId: null, executableNodeId: null })], [], vi.fn(), vi.fn());
    expect(mounted?.container.textContent).toContain("Run-level issue · no activity association was recorded.");
    expect(buttonByText(mounted!.container, "Show affected activity")).toBeUndefined();
  });

  it("uses the frozen occurrence presentation for the incident heading", () => {
    const graph = buildExecutableActivityGraph({
      executableNodeId: "root-executable",
      authoredActivityId: "root-authored",
      activityType: "Example.Root",
      activityTypeVersion: "1.0.0",
      inputBindings: [],
      childSlots: [{ name: "Sequence.Activities", activities: [{
        executableNodeId: "placement-2",
        authoredActivityId: "shared-authored-node",
        activityType: "Example.Activity",
        activityTypeVersion: "1.0.0",
        inputBindings: [],
        childSlots: []
      }] }]
    }, [], [], [], null, [{ executableNodeId: "placement-2", displayName: "Pinned occurrence name" }]);
    const activity = activityExecution({
      activityExecutionId: "execution-2",
      authoredActivityId: "shared-authored-node",
      executableNodeId: "placement-2"
    });
    const catalogItem = {
      activityVersionId: "example-activity@1",
      activityTypeKey: activity.activityType,
      version: activity.activityTypeVersion,
      category: "Tests",
      displayName: "Current catalog name",
      executionType: "Activity",
      inputs: [{ referenceKey: "text", name: "Text", typeName: "string", displayName: "Text" }],
      outputs: []
    } satisfies ActivityCatalogItem;

    renderIncidentList(
      [incident({ activityExecutionId: "execution-2", executableNodeId: "placement-2", metadata: { "runtime.inputKey": "text" } })],
      [activity],
      vi.fn(),
      vi.fn(),
      [catalogItem],
      null,
      graph
    );

    expect(mounted?.container.querySelector(".wf-instance-incident-summary strong")?.textContent)
      .toBe("Pinned occurrence name · Text input");
  });

  it("reports active and blocking totals together in the header health action", () => {
    const health = getIncidentHealthActionState([
      incident({ incidentId: "blocking", isBlocking: true }),
      incident({ incidentId: "active-1", isBlocking: false }),
      incident({ incidentId: "active-2", isBlocking: false }),
      incident({ incidentId: "resolved", status: "Resolved" })
    ]);

    expect(health).toEqual({
      health: "blocking",
      label: "Needs intervention · 1 blocking · 3 active"
    });
  });
});

function renderIncidentList(
  incidents: IncidentStateSummary[],
  activities: ActivityExecutionStateSummary[],
  onShowAffectedActivity: (incident: IncidentStateSummary) => void,
  onViewInput: (incident: IncidentStateSummary) => void,
  activityCatalog: ActivityCatalogItem[] = [],
  associationLookupMessage: string | null = null,
  executableGraph: Parameters<typeof WorkflowIncidentList>[0]["executableGraph"] = null
) {
  if (mounted) {
    flushSync(() => mounted!.root.unmount());
    mounted.container.remove();
  }
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  mounted = { root, container };
  flushSync(() => root.render(
    <WorkflowIncidentList incidents={incidents} activities={activities} activityCatalog={activityCatalog} executableGraph={executableGraph} associationLookupMessage={associationLookupMessage} onShowAffectedActivity={onShowAffectedActivity} onViewInput={onViewInput} />
  ));
}

function buttonByText(container: HTMLElement, text: string) {
  return [...container.querySelectorAll<HTMLButtonElement>("button")].find(button => button.textContent === text);
}

function click(element: HTMLElement | undefined) {
  flushSync(() => element?.click());
}

function activityExecution(overrides: Partial<ActivityExecutionStateSummary> = {}): ActivityExecutionStateSummary {
  return {
    activityExecutionId: "activity-execution-1",
    workflowExecutionId: "workflow-execution-1",
    executableNodeId: "exec-node",
    authoredActivityId: "authored-node",
    activityType: "Example.Activity",
    activityTypeVersion: "1.0.0",
    status: "Running",
    scheduledAt: "2026-10-01T12:00:00Z",
    startedAt: "2026-10-01T12:00:00Z",
    completedAt: null,
    parentActivityExecutionId: null,
    branchId: null,
    iterationId: null,
    bookmarkIds: [],
    incidentIds: ["incident-1"],
    faultCount: 0,
    aggregateFaultCount: 0,
    metadata: {},
    ...overrides
  };
}

function incident(overrides: Partial<IncidentStateSummary> = {}): IncidentStateSummary {
  return {
    incidentId: "incident-1",
    workflowExecutionId: "workflow-execution-1",
    activityExecutionId: "activity-execution-1",
    executableNodeId: "exec-node",
    severity: "Error",
    status: "Open",
    resolutionAction: "None",
    failureType: "ActivityInputFailed",
    message: "Wrapper failure",
    createdAt: "2026-10-01T12:00:00Z",
    resolvedAt: null,
    isBlocking: true,
    metadata: {},
    ...overrides
  };
}
