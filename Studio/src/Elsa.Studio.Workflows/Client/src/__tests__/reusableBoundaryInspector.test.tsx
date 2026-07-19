import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { afterEach, describe, expect, it, vi } from "vitest";
import {
  getActivityExecutionDescendants,
  getActivityExecutionInspection,
  getActivityExecutionLayout
} from "../api/runtime";
import { ReusableBoundaryInspector } from "../workflow-editor/ReusableBoundaryInspector";
import type {
  ActivityExecutionHierarchyItem,
  ActivityExecutionHierarchyPage,
  ActivityExecutionInspection,
  ActivityExecutionLayout
} from "../workflowTypes";

vi.mock("../api/runtime", async importOriginal => ({
  ...(await importOriginal<typeof import("../api/runtime")>()),
  getActivityExecutionInspection: vi.fn(),
  getActivityExecutionDescendants: vi.fn(),
  getActivityExecutionLayout: vi.fn()
}));

let active: { root: Root; container: HTMLElement } | null = null;
const context = {} as StudioEndpointContext;

afterEach(() => {
  if (active) {
    flushSync(() => active!.root.unmount());
    active.container.remove();
    active = null;
  }
  vi.mocked(getActivityExecutionInspection).mockReset();
  vi.mocked(getActivityExecutionDescendants).mockReset();
  vi.mocked(getActivityExecutionLayout).mockReset();
});

describe("ReusableBoundaryInspector", () => {
  it("loads pinned structure independently, virtualizes descendants, and does not preload a nested boundary", async () => {
    const layoutDeferred = deferred<ActivityExecutionLayout>();
    const descendantsDeferred = deferred<ActivityExecutionHierarchyPage>();
    vi.mocked(getActivityExecutionLayout).mockReturnValue(layoutDeferred.promise);
    vi.mocked(getActivityExecutionDescendants).mockReturnValue(descendantsDeferred.promise);

    const container = render(<ReusableBoundaryInspector context={context} inspection={boundaryInspection("outer")} />);

    await waitFor(() => {
      expect(getActivityExecutionLayout).toHaveBeenCalledTimes(1);
      expect(getActivityExecutionDescendants).toHaveBeenCalledTimes(1);
    });
    layoutDeferred.resolve(layout("outer", [{
      templateNodeId: "template-missing",
      authoredActivityId: "stored-authored-id",
      executableNodeId: "stored-executable-id",
      x: 10,
      y: 20,
      hasPinnedGeometry: true
    }]));

    await waitFor(() => expect(container.textContent).toContain("Pinned historical layout"));
    expect(container.textContent).toContain("historical descriptor unavailable");
    expect(container.textContent).not.toContain("Committed through sequence");
    expect(getActivityExecutionInspection).not.toHaveBeenCalled();

    const nested = hierarchyItem("nested", 2, {
      activityType: "Example.NestedReusable",
      boundary: boundaryInspection("nested").boundary
    });
    descendantsDeferred.resolve(page(20, [
      nested,
      ...Array.from({ length: 149 }, (_, index) => hierarchyItem(`loop-${index}`, index + 3, {
        iterationId: `iteration-${index}`
      }))
    ]));
    click(button(container, "Runtime Evidence"));

    await waitFor(() => expect(container.textContent).toContain("Committed through sequence 20"));
    expect(container.querySelectorAll("[role=treeitem]").length).toBeLessThan(20);
    expect(getActivityExecutionInspection).not.toHaveBeenCalled();
    expect(button(container, "Open boundary")).not.toBeNull();
  });

  it("opens nested boundaries explicitly and restores the parent pane, selection, and loaded snapshot", async () => {
    const interruptedContinuation = deferred<ActivityExecutionHierarchyPage>();
    vi.mocked(getActivityExecutionLayout).mockImplementation(async (_context, _run, activityExecutionId) =>
      layout(activityExecutionId, []));
    vi.mocked(getActivityExecutionDescendants).mockImplementation(async (_context, _run, activityExecutionId, request) =>
      request?.cursor
        ? interruptedContinuation.promise
        : activityExecutionId === "outer"
        ? page(10, [
            hierarchyItem("selected-child", 1),
            hierarchyItem("nested", 2, {
              activityType: "Example.NestedReusable",
              boundary: boundaryInspection("nested").boundary
            })
          ], "outer-next")
        : page(12, [hierarchyItem("nested-child", 3)]));
    vi.mocked(getActivityExecutionInspection).mockImplementation(async (_context, _run, activityExecutionId) =>
      activityExecutionId === "nested"
        ? boundaryInspection("nested")
        : canonicalInspection(activityExecutionId));

    const container = render(<ReusableBoundaryInspector context={context} inspection={boundaryInspection("outer")} />);
    click(button(container, "Runtime Evidence"));
    await waitFor(() => expect(container.textContent).toContain("selected-child"));

    click(treeItem(container, "selected-child"));
    await waitFor(() => expect(container.textContent).toContain("Canonical execution selected-child"));
    click(button(container, "Load next committed page"));
    await waitFor(() => expect(container.textContent).toContain("Loading next committed page"));
    click(button(container, "Open boundary"));

    await waitFor(() => expect(container.textContent).toContain("Execution nested"));
    click(button(container, "Runtime Evidence"));
    await waitFor(() => expect(container.textContent).toContain("Committed through sequence 12"));
    expect(getActivityExecutionDescendants).toHaveBeenCalledTimes(3);
    expect(getActivityExecutionLayout).toHaveBeenCalledTimes(2);

    click(button(container, "OuterReusable · attempt 1"));
    await waitFor(() => expect(container.textContent).toContain("Canonical execution selected-child"));
    expect(container.textContent).toContain("Committed through sequence 10");
    expect(container.querySelector("[role=tab][aria-selected=true]")?.textContent).toContain("Runtime Evidence");
    expect(button(container, "Load next committed page").hasAttribute("disabled")).toBe(false);
  });

  it("preserves stale evidence on cursor failure and replaces it only after an explicit snapshot restart", async () => {
    vi.mocked(getActivityExecutionLayout).mockResolvedValue(layout("outer", []));
    vi.mocked(getActivityExecutionDescendants)
      .mockResolvedValueOnce(page(40, [hierarchyItem("old-evidence", 1)], "next-cursor"))
      .mockRejectedValueOnce({
        message: "The hierarchy cursor no longer matches.",
        payload: {
          errorCode: "activity.cursor.binding-mismatch",
          detail: "The hierarchy cursor no longer matches the current request."
        }
      })
      .mockResolvedValueOnce(page(50, [hierarchyItem("new-evidence", 2)]));

    const container = render(<ReusableBoundaryInspector context={context} inspection={boundaryInspection("outer")} />);
    click(button(container, "Runtime Evidence"));
    await waitFor(() => expect(container.textContent).toContain("old-evidence"));

    click(button(container, "Load next committed page"));
    await waitFor(() => expect(container.textContent).toContain("Loaded evidence is stale"));
    expect(container.textContent).toContain("old-evidence");
    expect(container.textContent).not.toContain("new-evidence");

    click(button(container, "Restart from first page"));
    await waitFor(() => expect(container.textContent).toContain("new-evidence"));
    expect(container.textContent).not.toContain("old-evidence");
    expect(container.textContent).toContain("Committed through sequence 50");
    expect(getActivityExecutionDescendants).toHaveBeenNthCalledWith(
      2,
      context,
      "run",
      "outer",
      {
        cursor: "next-cursor",
        limit: 100,
        include: ["outcomes", "bookmarks", "incidents"]
      },
      expect.any(AbortSignal)
    );
    expect(getActivityExecutionDescendants).toHaveBeenNthCalledWith(
      3,
      context,
      "run",
      "outer",
      { limit: 100, include: ["outcomes", "bookmarks", "incidents"] },
      expect.any(AbortSignal)
    );
  });

  it("shows attempt-specific canonical incidents, bookmarks, causal links, and distinct value evidence states", async () => {
    vi.mocked(getActivityExecutionLayout).mockResolvedValue(layout("outer", []));
    vi.mocked(getActivityExecutionDescendants).mockResolvedValue(page(4, [
      hierarchyItem("attempt-2", 4, {
        status: "Cancelling",
        attempt: {
          attemptNumber: 2,
          firstAttemptActivityExecutionId: "attempt-1",
          previousAttemptActivityExecutionId: "attempt-1",
          totalAttempts: 2
        },
        bookmarkCount: 1,
        incidentCount: 1,
        blockingIncidentCount: 1
      })
    ]));
    vi.mocked(getActivityExecutionInspection).mockResolvedValue({
      ...canonicalInspection("attempt-2"),
      status: "Cancelling",
      attempt: {
        attemptNumber: 2,
        firstAttemptActivityExecutionId: "attempt-1",
        previousAttemptActivityExecutionId: "attempt-1",
        totalAttempts: 2
      },
      provenance: {
        ...canonicalInspection("attempt-2").provenance,
        parentActivityExecutionId: "parent-execution"
      },
      bookmarks: [{
        bookmarkId: "bookmark-attempt-2",
        resumeTargetId: "resume-target",
        stimulusType: "Message",
        stimulusHash: "sha256:message",
        createdAt: "2026-07-19T00:00:01Z",
        metadata: {}
      }],
      incidents: [{
        incidentId: "incident-attempt-2",
        severity: "Error",
        status: "Blocking",
        resolutionAction: "Retry",
        failureType: "Fault",
        message: "The first fault remains visible.",
        createdAt: "2026-07-19T00:00:02Z",
        isBlocking: true,
        metadata: {}
      }],
      valueSnapshots: [
        value("Captured value", { state: "captured", snapshot: { kind: "string", preview: "visible" } }),
        value("Redacted value", { state: "captured", snapshot: { kind: "redacted", reason: "secret" } }),
        value("Not captured value", { state: "notCaptured", captureMode: "None", captureReason: "Policy omitted it." }),
        value("Capture failed value", { state: "captureFailed", failure: { code: "capture.failed", message: "Serializer failed." } }),
        value("Payload value", {
          state: "captured",
          snapshot: {
            kind: "payloadReference",
            referenceKind: "blob",
            referenceId: "protected-reference",
            displayName: "Protected blob",
            resolution: { canResolve: false, reason: "Separate authorization required." }
          }
        })
      ]
    });

    const container = render(<ReusableBoundaryInspector context={context} inspection={boundaryInspection("outer")} />);
    click(button(container, "Runtime Evidence"));
    await waitFor(() => expect(container.textContent).toContain("attempt-2"));
    click(treeItem(container, "attempt-2"));

    await waitFor(() => expect(container.textContent).toContain("Canonical execution attempt-2"));
    expect(container.textContent).toContain("bookmark-attempt-2");
    expect(container.textContent).toContain("incident-attempt-2");
    expect(container.textContent).toContain("The first fault remains visible.");
    expect(container.textContent).toContain("Previous attempt");
    expect(container.textContent).toContain("Parent execution");
    expect(container.textContent).toContain("Cancelling is nonterminal");
    expect([...container.querySelectorAll(".wf-boundary-value-card > header > span:last-child")].map(node => node.textContent))
      .toEqual(["Captured", "Redacted", "Not captured", "Capture failed", "Payload reference"]);
    expect(container.textContent).not.toContain("Reveal");
    expect(container.textContent).not.toContain("Download");
  });

  it("supports roving tree focus and opens canonical evidence with the keyboard", async () => {
    vi.mocked(getActivityExecutionLayout).mockResolvedValue(layout("outer", []));
    vi.mocked(getActivityExecutionDescendants).mockResolvedValue(page(2, [
      hierarchyItem("first", 1),
      hierarchyItem("second", 2)
    ]));
    vi.mocked(getActivityExecutionInspection).mockImplementation(async (_context, _run, id) =>
      canonicalInspection(id));

    const container = render(<ReusableBoundaryInspector context={context} inspection={boundaryInspection("outer")} />);
    click(button(container, "Runtime Evidence"));
    await waitFor(() => expect(container.textContent).toContain("second"));

    const first = treeItem(container, "first");
    first.focus();
    first.dispatchEvent(new KeyboardEvent("keydown", { key: "ArrowDown", bubbles: true }));
    await waitFor(() => expect(document.activeElement?.textContent).toContain("second"));
    document.activeElement?.dispatchEvent(new KeyboardEvent("keydown", { key: "Enter", bubbles: true }));

    await waitFor(() => expect(container.textContent).toContain("Canonical execution second"));
  });
});

function render(ui: React.ReactElement) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  flushSync(() => root.render(ui));
  active = { root, container };
  return container;
}

async function waitFor(assertion: () => void) {
  let lastError: unknown;
  for (let attempt = 0; attempt < 80; attempt++) {
    try {
      assertion();
      return;
    } catch (error) {
      lastError = error;
      await new Promise(resolve => setTimeout(resolve, 0));
    }
  }
  throw lastError;
}

function click(element: HTMLElement) {
  flushSync(() => element.dispatchEvent(new MouseEvent("click", { bubbles: true })));
}

function button(container: HTMLElement, text: string, index = -1) {
  const matches = [...container.querySelectorAll<HTMLButtonElement>("button")]
    .filter(candidate => candidate.textContent?.includes(text));
  const match = index < 0 ? matches.at(index) : matches[index];
  if (!match) throw new Error(`Button containing "${text}" was not found.`);
  return match;
}

function treeItem(container: HTMLElement, executionId: string) {
  const match = [...container.querySelectorAll<HTMLElement>("[role=treeitem]")]
    .find(candidate => candidate.textContent?.includes(executionId));
  if (!match) throw new Error(`Tree item containing "${executionId}" was not found.`);
  return match;
}

function boundaryInspection(activityExecutionId: string): ActivityExecutionInspection {
  return {
    ...canonicalInspection(activityExecutionId),
    activityType: activityExecutionId === "outer" ? "Example.OuterReusable" : "Example.NestedReusable",
    boundary: {
      kind: "ReusableActivity",
      definitionId: `definition-${activityExecutionId}`,
      definitionVersionId: `definition-version-${activityExecutionId}`,
      version: "2.0.0",
      templateHash: `sha256:${activityExecutionId}`,
      invocationOrigin: [{ kind: "TemplateBoundary", id: `origin-${activityExecutionId}` }],
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
    }
  };
}

function canonicalInspection(activityExecutionId: string): ActivityExecutionInspection {
  return {
    activityExecutionId,
    workflowExecutionId: "run",
    executableNodeId: `node-${activityExecutionId}`,
    authoredActivityId: `authored-${activityExecutionId}`,
    activityType: "Example.Activity",
    activityTypeVersion: "1.0.0",
    status: "Completed",
    executionSequence: 1,
    scheduledAt: "2026-07-19T00:00:00Z",
    lastCommittedAt: "2026-07-19T00:00:01Z",
    provenance: {
      parentActivityExecutionId: null,
      schedulingActivityExecutionId: null,
      schedulingWorkflowExecutionId: "run",
      branchId: null,
      iterationId: null,
      executionPathId: null,
      executionScopeId: null,
      schedulingCause: null,
      metadata: {}
    },
    outcomeNames: [],
    bookmarks: [],
    incidents: [],
    valueSnapshots: [],
    metadata: {}
  };
}

function page(
  watermark: number,
  items: ActivityExecutionHierarchyItem[],
  nextCursor: string | null = null
): ActivityExecutionHierarchyPage {
  return {
    root: {
      workflowExecutionId: "run",
      activityExecutionId: "outer",
      executionScopeId: "scope-outer",
      definitionVersionId: "definition-version-outer",
      templateHash: "sha256:outer"
    },
    committedThroughSequence: watermark,
    effectiveLimit: 100,
    items,
    nextCursor
  };
}

function hierarchyItem(
  activityExecutionId: string,
  executionSequence: number,
  overrides: Partial<ActivityExecutionHierarchyItem> = {}
): ActivityExecutionHierarchyItem {
  return {
    activityExecutionId,
    workflowExecutionId: "run",
    executableNodeId: `node-${activityExecutionId}`,
    authoredActivityId: `authored-${activityExecutionId}`,
    activityType: "Example.Activity",
    activityTypeVersion: "1.0.0",
    status: "Completed",
    executionSequence,
    scheduledAt: "2026-07-19T00:00:00Z",
    relativeDepth: 1,
    outcomeNames: [],
    bookmarkCount: 0,
    incidentCount: 0,
    blockingIncidentCount: 0,
    metadata: {},
    ...overrides
  };
}

function layout(
  activityExecutionId: string,
  nodes: ActivityExecutionLayout["nodes"]
): ActivityExecutionLayout {
  return {
    workflowExecutionId: "run",
    activityExecutionId,
    artifactId: "artifact",
    sourceReferenceId: `source-reference-${activityExecutionId}`,
    selection: "ExecutedReference",
    boundaryOrigin: [{ kind: "TemplateBoundary", id: `origin-${activityExecutionId}` }],
    templateHash: `sha256:${activityExecutionId}`,
    nodes,
    connections: [],
    nestedBoundaries: []
  };
}

function value(
  name: string,
  overrides: Partial<ActivityExecutionInspection["valueSnapshots"][number]>
): ActivityExecutionInspection["valueSnapshots"][number] {
  return {
    name,
    subject: "ActivityInput",
    captureMode: "DiagnosticSnapshot",
    capturedAt: "2026-07-19T00:00:00Z",
    captureReason: "Test evidence.",
    isSensitive: false,
    metadata: {},
    ...overrides
  };
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise;
    reject = rejectPromise;
  });
  return { promise, resolve, reject };
}
