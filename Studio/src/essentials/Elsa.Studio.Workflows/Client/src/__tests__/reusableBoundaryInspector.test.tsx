import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { afterEach, describe, expect, it, vi } from "vitest";
import { getActivityDefinitionVersion } from "../api/activityDesign";
import {
  getActivityExecutionDescendants,
  getActivityExecutionInspection,
  getActivityExecutionLayout
} from "../api/runtime";
import { getActivityExecutionValuePayload } from "../api/activityExecutionValuePayload";
import { ReusableBoundaryInspector } from "../workflow-editor/ReusableBoundaryInspector";
import { WorkflowActivityExecutionDetails } from "../workflow-editor/WorkflowInstances";
import type {
  ActivityExecutionHierarchyItem,
  ActivityExecutionHierarchyPage,
  ActivityExecutionInspection,
  ActivityExecutionLayout,
  ActivityExecutionStateSummary
} from "../workflowTypes";

vi.mock("../api/runtime", async importOriginal => ({
  ...(await importOriginal<typeof import("../api/runtime")>()),
  getActivityExecutionInspection: vi.fn(),
  getActivityExecutionDescendants: vi.fn(),
  getActivityExecutionLayout: vi.fn()
}));

vi.mock("../api/activityDesign", async importOriginal => ({
  ...(await importOriginal<typeof import("../api/activityDesign")>()),
  getActivityDefinitionVersion: vi.fn()
}));

vi.mock("../api/activityExecutionValuePayload", () => ({ getActivityExecutionValuePayload: vi.fn() }));

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
  vi.mocked(getActivityExecutionValuePayload).mockReset();
  vi.mocked(getActivityDefinitionVersion).mockReset();
});

describe("ReusableBoundaryInspector", () => {
  it("names historical intrinsic steps without needing the live catalog", async () => {
    vi.mocked(getActivityExecutionLayout).mockResolvedValue(layout("outer", []));
    vi.mocked(getActivityExecutionDescendants).mockResolvedValue(page(2, [
      hierarchyItem("set", 1, { activityType: "elsa.intrinsic.set" }),
      hierarchyItem("output", 2, { activityType: "elsa.intrinsic.setoutput" })
    ]));
    const container = render(<ReusableBoundaryInspector context={context} inspection={boundaryInspection("outer")} />);
    await waitFor(() => expect([...container.querySelectorAll('[role="treeitem"]')].map(item => item.textContent).join(" "))
      .toContain("Set Variable"));
    expect([...container.querySelectorAll('[role="treeitem"]')].map(item => item.textContent).join(" ")).toContain("Set Output");
  });

  it("starts on readable Steps and keeps identities and sequence in collapsed Technical details", async () => {
    vi.mocked(getActivityExecutionLayout).mockResolvedValue(layout("outer", []));
    vi.mocked(getActivityExecutionDescendants).mockResolvedValue(page(7, [hierarchyItem("step-1", 6, {
      activityType: "Elsa.WriteLine",
      status: "Completed"
    })]));

    const container = render(<ReusableBoundaryInspector context={context} inspection={boundaryInspection("outer")} />);

    await waitFor(() => expect(getActivityExecutionDescendants).toHaveBeenCalledTimes(1));
    const tabs = [...container.querySelectorAll<HTMLElement>("[role=tab]")];
    expect(container.querySelector('[role="tablist"][aria-label="Activity details"]')).not.toBeNull();
    expect(tabs.map(tab => [tab.getAttribute("aria-label"), tab.getAttribute("aria-selected")]))
      .toEqual([["Steps", "true"], ["Definition", "false"]]);
    await waitFor(() => expect(container.querySelector('[role="treeitem"]')?.textContent).toContain("Write Line"));
    expect(container.querySelector('[role="treeitem"]')?.textContent).toContain("Completed");
    expect(container.querySelector(".wf-boundary-evidence-toolbar")?.textContent).toContain("1 recorded step");
    expect(container.querySelector<HTMLButtonElement>('button[aria-label="Refresh activity steps"]')?.textContent).toContain("Refresh");
    expect(container.querySelector('nav[aria-label="Activity boundary path"]')).toBeNull();

    const technical = container.querySelector<HTMLDetailsElement>("details.wf-boundary-technical");
    expect(technical).not.toBeNull();
    expect(technical!.open).toBe(false);
    click(technical!.querySelector("summary")!);
    expect(technical!.open).toBe(true);
    expect(technical!.textContent).toContain("definition-outer");
    expect(technical!.textContent).toContain("definition-version-outer");
    expect(technical!.textContent).toContain("Snapshot through sequence7");
  });

  it("keeps the selected nested step status readable while the outer activity status stays intact", async () => {
    const nestedInspection = {
      ...boundaryInspection("nested"),
      status: "Faulted",
      subStatus: "Retryable"
    };
    vi.mocked(getActivityExecutionInspection).mockImplementation(async (_context, _run, activityExecutionId) =>
      activityExecutionId === "nested" ? nestedInspection : boundaryInspection("outer"));
    vi.mocked(getActivityExecutionLayout).mockImplementation(async (_context, _run, activityExecutionId) =>
      layout(activityExecutionId, []));
    vi.mocked(getActivityExecutionDescendants).mockResolvedValue(page(1, [hierarchyItem("nested", 1, {
      activityType: "Example.NestedReusable",
      status: "Faulted",
      subStatus: "Retryable",
      boundary: nestedInspection.boundary
    })]));
    vi.mocked(getActivityDefinitionVersion).mockResolvedValue({
      definition: { definitionId: "definition-outer" },
      versionId: "definition-version-outer"
    } as Awaited<ReturnType<typeof getActivityDefinitionVersion>>);

    const rootActivity = activitySummary("outer");
    const container = render(
      <WorkflowActivityExecutionDetails context={context} activity={rootActivity} activityCatalog={[]} />
    );
    const boundarySummary = () => container.querySelector<HTMLElement>(".wf-activity-boundary-details summary");
    await waitFor(() => expect(boundarySummary()?.textContent).toContain("Inside this activity"));
    click(boundarySummary()!);
    await waitFor(() => expect((boundarySummary()!.parentElement as HTMLDetailsElement).open).toBe(true));
    await waitFor(() => expect(
      container.querySelector(".wf-boundary-inspector") || container.textContent?.includes("Loading activity steps...")
    ).toBeTruthy());
    await import("../workflow-editor/ReusableBoundaryInspector");
    rerender(<WorkflowActivityExecutionDetails context={context} activity={rootActivity} activityCatalog={[]} />);
    await waitFor(() => expect(container.querySelector(".wf-boundary-inspector")).not.toBeNull());

    await waitFor(() => expect(treeItem(container, "Nested Reusable")).toBeDefined());
    click(treeItem(container, "Nested Reusable"));
    const selectedStep = () => container.querySelector<HTMLElement>('[aria-label="Selected activity step"]');
    await waitFor(() => expect(selectedStep()?.textContent).toContain("Faulted"));
    expect(selectedStep()?.textContent).toContain("Retryable");
    expect(container.querySelector(".wf-activity-overview")?.textContent).toContain("Completed");
    expect(boundarySummary()?.textContent).not.toContain("Faulted");
    const technical = container.querySelector<HTMLDetailsElement>("details.wf-boundary-technical");
    expect(technical?.open).toBe(false);
    expect(technical?.textContent).not.toContain("Retryable");

    click(button(container, "View steps"));
    const path = () => container.querySelector<HTMLElement>('[aria-label="Activity boundary path"]');
    await waitFor(() => expect(path()?.textContent).toContain("Nested Reusable"));
    expect(path()?.textContent).toContain("Faulted · Retryable");
    expect(container.querySelector('[aria-label="Activity boundary path"]')?.textContent).not.toContain("nested");
  });

  it("links to the exact inspected Activity Definition version only after authorized source resolution", async () => {
    vi.mocked(getActivityExecutionLayout).mockResolvedValue(layout("outer", []));
    vi.mocked(getActivityExecutionDescendants).mockResolvedValue(page(1, []));
    vi.mocked(getActivityDefinitionVersion).mockResolvedValue({
      definition: { definitionId: "definition-outer" },
      versionId: "definition-version-outer"
    } as Awaited<ReturnType<typeof getActivityDefinitionVersion>>);
    const container = render(<ReusableBoundaryInspector context={context} inspection={boundaryInspection("outer")} />);

    await waitFor(() => {
      const link = [...container.querySelectorAll<HTMLAnchorElement>("a")]
        .find(candidate => candidate.textContent === "View activity definition");
      expect(link?.getAttribute("href")).toBe("/workflows/activity-definitions?definition=definition-outer&section=versions&version=definition-version-outer");
    });
    expect(getActivityDefinitionVersion).toHaveBeenCalledWith(context, "definition-version-outer", expect.any(AbortSignal));
  });

  it("does not link when the resolved version belongs to a different definition", async () => {
    vi.mocked(getActivityExecutionLayout).mockResolvedValue(layout("outer", []));
    vi.mocked(getActivityExecutionDescendants).mockResolvedValue(page(1, []));
    vi.mocked(getActivityDefinitionVersion).mockResolvedValue({
      definition: { definitionId: "definition-outer" },
      versionId: "other-version"
    } as Awaited<ReturnType<typeof getActivityDefinitionVersion>>);
    const container = render(<ReusableBoundaryInspector context={context} inspection={boundaryInspection("outer")} />);

    await waitFor(() => expect(container.textContent).toContain("Activity definition link unavailable."));
    expect([...container.querySelectorAll<HTMLAnchorElement>("a")]
      .some(candidate => candidate.textContent === "View activity definition")).toBe(false);
  });

  it("loads pinned structure independently, virtualizes descendants, and does not preload a nested boundary", async () => {
    const layoutDeferred = deferred<ActivityExecutionLayout>();
    const descendantsDeferred = deferred<ActivityExecutionHierarchyPage>();
    vi.mocked(getActivityExecutionLayout).mockReturnValue(layoutDeferred.promise);
    vi.mocked(getActivityExecutionDescendants).mockReturnValue(descendantsDeferred.promise);

    const container = render(<ReusableBoundaryInspector context={context} inspection={boundaryInspection("outer")} />);
    expect(container.querySelector('[role="tab"][aria-label="Steps"][aria-selected="true"]')).not.toBeNull();
    click(button(container, "Definition"));

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

    await waitFor(() => expect(container.textContent).toContain("Saved activity layout"));
    expect(container.textContent).toContain("Historical activity descriptor unavailable");
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
    click(button(container, "Steps"));

    await expectSnapshotSequence(container, 20);
    expect(container.querySelectorAll("[role=treeitem]").length).toBeLessThan(20);
    expect(getActivityExecutionInspection).not.toHaveBeenCalled();
    expect(button(container, "View steps")).not.toBeNull();
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
            hierarchyItem("selected-child", 1, { activityType: "Example.SelectedChild" }),
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
    await waitFor(() => expect(treeItem(container, "Selected Child")).toBeDefined());

    click(treeItem(container, "Selected Child"));
    await waitFor(() => expect(container.querySelector('[aria-label="Selected activity step"]')?.textContent).toContain("Activity"));
    click(button(container, "Load more steps"));
    await waitFor(() => expect(container.textContent).toContain("Loading more steps"));
    click(button(container, "View steps"));

    await waitFor(() => expect(container.querySelector('[aria-label="Activity boundary path"]')?.textContent).toContain("Nested Reusable"));
    expect(container.querySelector('[aria-label="Activity boundary path"]')?.textContent).not.toContain("nested");
    await expectSnapshotSequence(container, 12);
    expect(getActivityExecutionDescendants).toHaveBeenCalledTimes(3);
    expect(getActivityExecutionLayout).toHaveBeenCalledTimes(2);

    click(button(container, "Outer Reusable"));
    await waitFor(() => expect(container.querySelector('[aria-label="Selected activity step"]')?.textContent).toContain("Activity"));
    await expectSnapshotSequence(container, 10);
    expect(container.querySelector('[role="tab"][aria-selected="true"]')?.getAttribute("aria-label")).toBe("Steps");
    expect(button(container, "Load more steps").hasAttribute("disabled")).toBe(false);
  });

  it("preserves loaded and selected nested frames when the activity label arrives later", async () => {
    const outerInspection = boundaryInspection("outer");
    const nestedInspection = boundaryInspection("nested");
    vi.mocked(getActivityExecutionLayout).mockImplementation(async (_context, _run, activityExecutionId) =>
      layout(activityExecutionId, []));
    vi.mocked(getActivityExecutionDescendants).mockImplementation(async (_context, _run, activityExecutionId) =>
      activityExecutionId === "outer"
        ? page(10, [
            hierarchyItem("selected-child", 1, { activityType: "Example.SelectedChild" }),
            hierarchyItem("nested", 2, { activityType: "Example.NestedReusable", boundary: nestedInspection.boundary })
          ])
        : page(12, [hierarchyItem("nested-child", 3, { activityType: "Example.NestedChild" })]));
    vi.mocked(getActivityExecutionInspection).mockImplementation(async (_context, _run, id) =>
      id === "nested" ? nestedInspection : { ...canonicalInspection(id), activityType: "Example.SelectedChild" });

    const container = render(<ReusableBoundaryInspector context={context} inspection={outerInspection} />);
    await waitFor(() => expect(treeItem(container, "Selected Child")).toBeDefined());
    click(treeItem(container, "Selected Child"));
    await waitFor(() => expect(container.querySelector('[aria-label="Selected activity step"]')?.textContent).toContain("Selected Child"));
    click(button(container, "View steps"));
    await waitFor(() => expect(container.querySelector('[aria-label="Activity boundary path"]')?.textContent).toContain("Nested Reusable"));
    await waitFor(() => expect(treeItem(container, "Nested Child")).toBeDefined());

    const requestCounts = {
      layouts: vi.mocked(getActivityExecutionLayout).mock.calls.length,
      pages: vi.mocked(getActivityExecutionDescendants).mock.calls.length,
      inspections: vi.mocked(getActivityExecutionInspection).mock.calls.length
    };
    rerender(<ReusableBoundaryInspector context={context} inspection={outerInspection} activityLabel="Published invoice activity" />);

    await waitFor(() => expect(container.querySelector('[aria-label="Activity boundary path"]')?.textContent).toContain("Published invoice activity"));
    expect(container.querySelector('[aria-label="Activity boundary path"]')?.textContent).toContain("Nested Reusable");
    expect(treeItem(container, "Nested Child")).toBeDefined();
    expect(getActivityExecutionLayout).toHaveBeenCalledTimes(requestCounts.layouts);
    expect(getActivityExecutionDescendants).toHaveBeenCalledTimes(requestCounts.pages);
    expect(getActivityExecutionInspection).toHaveBeenCalledTimes(requestCounts.inspections);

    click(button(container, "Published invoice activity"));
    await waitFor(() => expect(container.querySelector('[aria-label="Selected activity step"]')?.textContent).toContain("Selected Child"));
    expect(treeItem(container, "Selected Child").getAttribute("aria-selected")).toBe("true");
    expect(getActivityExecutionLayout).toHaveBeenCalledTimes(requestCounts.layouts);
    expect(getActivityExecutionDescendants).toHaveBeenCalledTimes(requestCounts.pages);
    expect(getActivityExecutionInspection).toHaveBeenCalledTimes(requestCounts.inspections);
  });

  it("returns from Definition and Technical details to Steps when following a related execution", async () => {
    vi.mocked(getActivityExecutionLayout).mockResolvedValue(layout("outer", []));
    vi.mocked(getActivityExecutionDescendants).mockResolvedValue(page(4, [hierarchyItem("attempt-2", 4, {
      activityType: "Example.RetryAttempt"
    })]));
    vi.mocked(getActivityExecutionInspection).mockImplementation(async (_context, _run, id) => id === "attempt-2"
      ? {
          ...canonicalInspection(id),
          activityType: "Example.RetryAttempt",
          attempt: {
            attemptNumber: 2,
            firstAttemptActivityExecutionId: "attempt-1",
            previousAttemptActivityExecutionId: "attempt-1",
            totalAttempts: 2
          }
        }
      : { ...canonicalInspection(id), activityType: "Example.PreviousAttempt", status: "Faulted" });

    const container = render(<ReusableBoundaryInspector context={context} inspection={boundaryInspection("outer")} />);
    await waitFor(() => expect(treeItem(container, "Retry Attempt")).toBeDefined());
    click(treeItem(container, "Retry Attempt"));
    await waitFor(() => expect(container.querySelector('[aria-label="Selected activity step"]')?.textContent).toContain("Retry Attempt"));

    click(button(container, "Definition"));
    await waitFor(() => expect(container.textContent).toContain("Activity structure"));
    const technical = container.querySelector<HTMLDetailsElement>("details.wf-boundary-technical")!;
    click(technical.querySelector("summary")!);
    click(button(technical, "Previous attempt"));

    await waitFor(() => expect(container.querySelector('[role="tab"][aria-label="Steps"]')?.getAttribute("aria-selected")).toBe("true"));
    await waitFor(() => expect(container.querySelector('[aria-label="Selected activity step"]')?.textContent).toContain("Previous Attempt"));
    const tabpanel = container.querySelector<HTMLElement>('[role="tabpanel"]');
    await waitFor(() => expect(document.activeElement).toBe(tabpanel));
    expect(vi.mocked(getActivityExecutionInspection)).toHaveBeenLastCalledWith(
      context, "run", "attempt-1", expect.any(AbortSignal));
  });

  it("preserves stale evidence on cursor failure and replaces it only after an explicit snapshot restart", async () => {
    vi.mocked(getActivityExecutionLayout).mockResolvedValue(layout("outer", []));
    vi.mocked(getActivityExecutionDescendants)
      .mockResolvedValueOnce(page(40, [hierarchyItem("old-evidence", 1, { activityType: "Example.OldEvidence" })], "next-cursor"))
      .mockRejectedValueOnce({
        message: "The hierarchy cursor no longer matches.",
        payload: {
          errorCode: "activity.cursor.binding-mismatch",
          detail: "The hierarchy cursor no longer matches the current request."
        }
      })
      .mockResolvedValueOnce(page(50, [hierarchyItem("new-evidence", 2, { activityType: "Example.NewEvidence" })]));

    const container = render(<ReusableBoundaryInspector context={context} inspection={boundaryInspection("outer")} />);
    await waitFor(() => expect(treeItem(container, "Old Evidence")).toBeDefined());

    click(button(container, "Load more steps"));
    await waitFor(() => expect(container.textContent).toContain("out of date"));
    expect(treeItem(container, "Old Evidence")).toBeDefined();
    expect(() => treeItem(container, "New Evidence")).toThrow();

    click(button(container, "Refresh steps"));
    await waitFor(() => expect(treeItem(container, "New Evidence")).toBeDefined());
    expect(() => treeItem(container, "Old Evidence")).toThrow();
    await expectSnapshotSequence(container, 50);
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
        activityType: "Example.RetryAttempt",
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
        resolutionOutcome: { actionKind: "Retry", metadata: {} },
        failureType: "Fault",
        message: "The first fault remains visible.",
        createdAt: "2026-07-19T00:00:02Z",
        isBlocking: true,
        metadata: {}
      }],
      valueSnapshots: [
        value("Captured value", { state: "captured", type: { kind: "alias", id: "Int32" }, snapshot: { kind: "string", preview: "visible" } }),
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
    await waitFor(() => expect(treeItem(container, "Retry Attempt")).toBeDefined());
    click(treeItem(container, "Retry Attempt"));

    await waitFor(() => expect(container.querySelector('[aria-label="Selected activity step"]')?.textContent).toContain("Activity"));
    const technical = container.querySelector<HTMLDetailsElement>("details.wf-boundary-technical")!;
    expect(technical.open).toBe(false);
    click(technical.querySelector("summary")!);
    expect(technical.textContent).toContain("attempt-2");
    expect(container.textContent).toContain("Input · Int32");
    expect(container.textContent).toContain("bookmark-attempt-2");
    expect(container.textContent).toContain("incident-attempt-2");
    expect(container.querySelector('[aria-label="Selected activity step"]')?.textContent).toContain("The first fault remains visible.");
    expect(container.textContent).toContain("Previous attempt");
    expect(container.textContent).toContain("Parent execution");
    expect(container.textContent).toContain("Cancellation is still finishing.");
    expect([...container.querySelectorAll(".wf-boundary-value-card > header > span:last-child")].map(node => node.textContent))
      .toEqual(["Captured", "Redacted", "Not captured", "Capture failed", "Payload reference"]);
    expect(container.textContent).not.toContain("Reveal");
    expect(container.textContent).not.toContain("Download");
  });

  it("resolves metadata-only captured output using the selected canonical execution IDs", async () => {
    vi.mocked(getActivityExecutionValuePayload).mockResolvedValue({
      evidenceId: "output-evidence", captureMode: "DiagnosticSnapshot", payload: { kind: "number", value: 200 }
    });
    const container = await selectCapturedExecution();

    await waitFor(() => expect(container.querySelector(".wf-runtime-input-value")?.textContent).toBe("200"));
    expect(getActivityExecutionValuePayload).toHaveBeenCalledWith(
      context, "run", "first", "output-evidence", "DiagnosticSnapshot", expect.any(AbortSignal));
  });

  it("requires a fresh sensitive-output action after changing the selected descendant", async () => {
    vi.mocked(getActivityExecutionValuePayload).mockResolvedValue({
      evidenceId: "output-evidence", captureMode: "DiagnosticSnapshot", payload: { kind: "string", preview: "PRIVATE_OUTPUT" }
    });
    const container = await selectCapturedExecution({ isSensitive: true });
    expect(getActivityExecutionValuePayload).not.toHaveBeenCalled();
    click(button(container, "Show captured value"));
    await waitFor(() => expect(container.textContent).toContain("PRIVATE_OUTPUT"));

    click(treeItem(container, "Second Step"));
    await waitFor(() => expect(container.querySelector('[aria-label="Selected activity step"]')?.textContent).toContain("Activity"));
    expect(container.textContent).not.toContain("PRIVATE_OUTPUT");
    expect(getActivityExecutionValuePayload).toHaveBeenCalledTimes(1);
    click(button(container, "Show captured value"));
    await waitFor(() => expect(getActivityExecutionValuePayload).toHaveBeenCalledTimes(2));
    expect(getActivityExecutionValuePayload).toHaveBeenLastCalledWith(
      context, "run", "second", "output-evidence", "DiagnosticSnapshot", expect.any(AbortSignal));
  });

  it.each([
    ["allowed", true],
    ["resolutionPermissionRequired", false],
    ["unavailable", false]
  ])("hides inline values with %s access and sensitive=%s", async (accessState, isSensitive) => {
    const container = await selectCapturedExecution({
      accessState, isSensitive,
      payload: "PRIVATE_INLINE_OUTPUT",
      snapshot: { kind: "string", preview: "PRIVATE_INLINE_OUTPUT" }
    });

    expect(container.textContent).not.toContain("PRIVATE_INLINE_OUTPUT");
    expect(getActivityExecutionValuePayload).not.toHaveBeenCalled();
  });

  it("cancels stale selected-output resolution and ignores its late result", async () => {
    const firstPayload = deferred<Awaited<ReturnType<typeof getActivityExecutionValuePayload>>>();
    vi.mocked(getActivityExecutionValuePayload)
      .mockReturnValueOnce(firstPayload.promise)
      .mockResolvedValue({
        evidenceId: "output-evidence", captureMode: "DiagnosticSnapshot", payload: { kind: "string", preview: "CURRENT_OUTPUT" }
      });
    const container = await selectCapturedExecution();
    await waitFor(() => expect(getActivityExecutionValuePayload).toHaveBeenCalledTimes(1));
    const firstSignal = vi.mocked(getActivityExecutionValuePayload).mock.calls[0]![5]!;
    click(treeItem(container, "Second Step"));
    await waitFor(() => expect(container.textContent).toContain("CURRENT_OUTPUT"));
    expect(firstSignal.aborted).toBe(true);
    firstPayload.resolve({
      evidenceId: "output-evidence", captureMode: "DiagnosticSnapshot", payload: { kind: "string", preview: "STALE_OUTPUT" }
    });
    await new Promise(resolve => setTimeout(resolve, 0));
    expect(container.textContent).not.toContain("STALE_OUTPUT");
    expect(container.textContent).toContain("CURRENT_OUTPUT");
  });

  it("supports roving tree focus and opens canonical evidence with the keyboard", async () => {
    vi.mocked(getActivityExecutionLayout).mockResolvedValue(layout("outer", []));
    vi.mocked(getActivityExecutionDescendants).mockResolvedValue(page(2, [
      hierarchyItem("first", 1, { activityType: "Example.FirstStep" }),
      hierarchyItem("second", 2, { activityType: "Example.SecondStep" })
    ]));
    vi.mocked(getActivityExecutionInspection).mockImplementation(async (_context, _run, id) =>
      canonicalInspection(id));

    const container = render(<ReusableBoundaryInspector context={context} inspection={boundaryInspection("outer")} />);
    click(button(container, "Steps"));
    await waitFor(() => expect(treeItem(container, "Second Step")).toBeDefined());

    const first = treeItem(container, "First Step");
    first.focus();
    first.dispatchEvent(new KeyboardEvent("keydown", { key: "ArrowDown", bubbles: true }));
    await waitFor(() => expect(document.activeElement?.textContent).toContain("Second Step"));
    document.activeElement?.dispatchEvent(new KeyboardEvent("keydown", { key: "Enter", bubbles: true }));

    await waitFor(() => expect(container.querySelector('[aria-label="Selected activity step"]')?.textContent).toContain("Activity"));
  });
});

async function selectCapturedExecution(overrides: Partial<ActivityExecutionInspection["valueSnapshots"][number]> = {}) {
  vi.mocked(getActivityExecutionLayout).mockResolvedValue(layout("outer", []));
  vi.mocked(getActivityExecutionDescendants).mockResolvedValue(page(2, [
    hierarchyItem("first", 1, { activityType: "Example.FirstStep" }),
    hierarchyItem("second", 2, { activityType: "Example.SecondStep" })
  ]));
  vi.mocked(getActivityExecutionInspection).mockImplementation(async (_context, _run, id) => ({
    ...canonicalInspection(id),
    valueSnapshots: [value("Result", {
      subject: "ActivityOutput", evidenceId: "output-evidence", captureMode: "DiagnosticSnapshot",
      captureState: "diagnosticSnapshotCaptured", accessState: "resolutionAvailable",
      ...overrides
    })]
  }));
  const container = render(<ReusableBoundaryInspector context={context} inspection={boundaryInspection("outer")} />);
  click(button(container, "Steps"));
  await waitFor(() => expect(treeItem(container, "First Step")).toBeDefined());
  click(treeItem(container, "First Step"));
  await waitFor(() => expect(container.querySelector('[aria-label="Selected activity step"]')?.textContent).toContain("Activity"));
  return container;
}

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

async function expectSnapshotSequence(container: HTMLElement, sequence: number) {
  const technical = container.querySelector<HTMLDetailsElement>("details.wf-boundary-technical");
  if (!technical) throw new Error("Technical details disclosure was not rendered.");
  if (!technical.open) click(technical.querySelector("summary")!);
  await waitFor(() => {
    const label = [...technical.querySelectorAll("dt")].find(item => item.textContent === "Snapshot through sequence");
    expect(label?.nextElementSibling?.textContent).toBe(String(sequence));
  });
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

function treeItem(container: HTMLElement, activityLabel: string) {
  const match = [...container.querySelectorAll<HTMLElement>("[role=treeitem]")]
    .find(candidate => candidate.textContent?.includes(activityLabel));
  if (!match) throw new Error(`Tree item containing "${activityLabel}" was not found.`);
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

function activitySummary(activityExecutionId: string): ActivityExecutionStateSummary {
  return {
    activityExecutionId,
    workflowExecutionId: "run",
    executableNodeId: `node-${activityExecutionId}`,
    authoredActivityId: `authored-${activityExecutionId}`,
    activityType: "Example.OuterReusable",
    activityTypeVersion: "2.0.0",
    status: "Completed",
    subStatus: null,
    scheduledAt: "2026-07-19T00:00:00Z",
    startedAt: "2026-07-19T00:00:01Z",
    completedAt: "2026-07-19T00:00:02Z",
    schedulingActivityExecutionId: null,
    parentActivityExecutionId: null,
    branchId: null,
    iterationId: null,
    callStackDepth: null,
    bookmarkIds: [],
    incidentIds: [],
    metadata: {}
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
