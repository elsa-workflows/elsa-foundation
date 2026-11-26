// @vitest-environment jsdom
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type { ActivityExecutionStateSummary } from "../workflowTypes";
import {
  buildActivityTimelineEntries,
  meaningfulActivityOutcomes,
  RunActivitiesTimeline
} from "../workflow-editor/run-history/RunActivitiesTimeline";

const runtimeApi = vi.hoisted(() => ({
  browseActivityExecutions: vi.fn(),
  supportsActivityExecutionBrowsing: vi.fn()
}));

vi.mock("../api/runtime", () => runtimeApi);

function execution(overrides: Partial<ActivityExecutionStateSummary> = {}): ActivityExecutionStateSummary {
  return {
    activityExecutionId: "exec-1",
    workflowExecutionId: "run-1",
    executableNodeId: "node-1",
    authoredActivityId: "activity-1",
    activityType: "Elsa.WriteLine",
    activityTypeVersion: "1",
    status: "Completed",
    executionSequence: 1,
    scheduledAt: "2026-11-23T10:00:00Z",
    startedAt: "2026-11-23T10:00:00.100Z",
    completedAt: "2026-11-23T10:00:00.150Z",
    parentActivityExecutionId: null,
    incidentCount: 0,
    incidentIds: [],
    outcomeNames: ["Done"],
    metadata: {},
    ...overrides
  };
}

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
  runtimeApi.supportsActivityExecutionBrowsing.mockResolvedValue(true);
  runtimeApi.browseActivityExecutions.mockResolvedValue({
    items: [],
    totalCount: 0,
    previousCursor: null,
    nextCursor: null
  });
});

afterEach(() => {
  flushSync(() => root.unmount());
  container.remove();
  vi.clearAllMocks();
});

describe("buildActivityTimelineEntries", () => {
  it("groups children under parents in start-time order with 100ns tick precision regardless of hashed executionSequence", () => {
    const flowchart = execution({
      activityExecutionId: "exec-flow",
      executionSequence: 999999,
      parentActivityExecutionId: null,
      startedAt: "2026-11-23T14:33:36.0001000Z",
      completedAt: "2026-11-23T14:33:37.5000000Z"
    });
    const write1 = execution({
      activityExecutionId: "exec-w1",
      executionSequence: 100,
      parentActivityExecutionId: "exec-flow",
      startedAt: "2026-11-23T14:33:36.0001500Z",
      completedAt: "2026-11-23T14:33:36.0001800Z"
    });
    const ifAct = execution({
      activityExecutionId: "exec-if",
      executionSequence: 800,
      parentActivityExecutionId: "exec-flow",
      startedAt: "2026-11-23T14:33:36.0001900Z",
      completedAt: "2026-11-23T14:33:36.0002100Z"
    });
    const write3 = execution({
      activityExecutionId: "exec-w3",
      executionSequence: 200,
      parentActivityExecutionId: "exec-flow",
      startedAt: "2026-11-23T14:33:37.1000000Z",
      completedAt: "2026-11-23T14:33:37.2000000Z"
    });
    const write4 = execution({
      activityExecutionId: "exec-w4",
      executionSequence: 50,
      parentActivityExecutionId: "exec-flow",
      startedAt: "2026-11-23T14:33:37.3000000Z",
      completedAt: "2026-11-23T14:33:37.4000000Z"
    });

    const entries = buildActivityTimelineEntries([write4, write1, write3, ifAct, flowchart]);

    expect(entries.map(entry => [entry.execution.activityExecutionId, entry.depth, entry.hasChildren, entry.descendantCount])).toEqual([
      ["exec-flow", 0, true, 4],
      ["exec-w1", 1, false, 0],
      ["exec-if", 1, false, 0],
      ["exec-w3", 1, false, 0],
      ["exec-w4", 1, false, 0]
    ]);
    expect(entries[0]!.span).toEqual({ startPercent: 0, widthPercent: 100, open: false });
    expect(entries[3]!.span!.startPercent).toBeGreaterThan(70);
    expect(entries[3]!.span!.widthPercent).toBeGreaterThan(0);
  });

  it("caps visual nesting depth at 4 and handles missing or equal timestamps deterministically", () => {
    const seq = execution({ activityExecutionId: "exec-seq", executionSequence: 1, parentActivityExecutionId: null });
    const child = execution({ activityExecutionId: "exec-child", executionSequence: 2, parentActivityExecutionId: "exec-seq" });
    const grandchild = execution({ activityExecutionId: "exec-grandchild", executionSequence: 3, parentActivityExecutionId: "exec-child" });
    const deep1 = execution({ activityExecutionId: "exec-d1", executionSequence: 4, parentActivityExecutionId: "exec-grandchild" });
    const deep2 = execution({ activityExecutionId: "exec-d2", executionSequence: 5, parentActivityExecutionId: "exec-d1" });
    const deep3 = execution({ activityExecutionId: "exec-d3", executionSequence: 6, parentActivityExecutionId: "exec-d2" });

    const entries = buildActivityTimelineEntries([deep3, child, seq, deep1, grandchild, deep2]);

    expect(entries.map(entry => [entry.execution.activityExecutionId, entry.depth])).toEqual([
      ["exec-seq", 0],
      ["exec-child", 1],
      ["exec-grandchild", 2],
      ["exec-d1", 3],
      ["exec-d2", 4],
      ["exec-d3", 4]
    ]);
  });

  it("computes open spans for waiting/running activities and omits spans when all timestamps coincide", () => {
    const identical = buildActivityTimelineEntries([
      execution({ activityExecutionId: "a", startedAt: "2026-11-23T10:00:00Z", completedAt: "2026-11-23T10:00:00Z" }),
      execution({ activityExecutionId: "b", startedAt: "2026-11-23T10:00:00Z", completedAt: "2026-11-23T10:00:00Z" })
    ]);
    expect(identical.every(entry => entry.span === null)).toBe(true);

    const openRun = buildActivityTimelineEntries([
      execution({ activityExecutionId: "start", startedAt: "2026-11-23T10:00:00Z", completedAt: "2026-11-23T10:00:02Z" }),
      execution({ activityExecutionId: "wait", status: "Suspended", startedAt: "2026-11-23T10:00:01Z", completedAt: null })
    ]);
    expect(openRun[1]!.span).toEqual({ startPercent: 50, widthPercent: 50, open: true });
  });

  it("hides a lone default Done outcome while preserving branching or multi-outcome chips", () => {
    expect(meaningfulActivityOutcomes(execution({ outcomeNames: ["Done"] }))).toEqual([]);
    expect(meaningfulActivityOutcomes(execution({ outcomeNames: ["True"] }))).toEqual(["True"]);
    expect(meaningfulActivityOutcomes(execution({ outcomeNames: ["Done", "Approved"] }))).toEqual(["Done", "Approved"]);
  });
});

describe("RunActivitiesTimeline", () => {
  it("renders a clean chronological list with no status filter clutter on healthy runs and selects activities directly", () => {
    const onSelect = vi.fn();
    const onRefresh = vi.fn();
    const items = [
      execution({ activityExecutionId: "exec-1", executionSequence: 1, activityType: "Elsa.Sequence", outcomeNames: ["Done"] }),
      execution({ activityExecutionId: "exec-2", executionSequence: 2, parentActivityExecutionId: "exec-1", activityType: "Elsa.If", outcomeNames: ["True"] })
    ];
    const labels: Record<string, string> = { "exec-1": "Order sequence", "exec-2": "Check credit" };

    flushSync(() => root.render(
      <RunActivitiesTimeline
        context={{} as StudioEndpointContext}
        workflowExecutionId="run-1"
        activities={items}
        selectedActivityExecutionId="exec-2"
        labelForActivity={item => labels[item.activityExecutionId] ?? item.activityType}
        onSelectActivity={onSelect}
        onRefresh={onRefresh}
      />
    ));

    expect(container.querySelector("[aria-label='Filter activities by status']")).toBeNull();
    const rows = [...container.querySelectorAll<HTMLButtonElement>(".wf-activity-timeline-row")];
    expect(rows).toHaveLength(2);
    expect(rows[0]!.textContent).toContain("Order sequence");
    expect(rows[0]!.textContent).not.toContain("Done");
    expect(rows[1]!.textContent).toContain("Check credit");
    expect(rows[1]!.textContent).toContain("True");
    expect(rows[1]!.getAttribute("aria-pressed")).toBe("true");

    flushSync(() => rows[0]!.click());
    expect(onSelect).toHaveBeenCalledWith(items[0]);
  });

  it("surfaces Faulted and Waiting quick filters when relevant and filters by search text", () => {
    const items = [
      execution({ activityExecutionId: "exec-ok", executionSequence: 1, status: "Completed" }),
      execution({ activityExecutionId: "exec-bad", executionSequence: 2, status: "Faulted", incidentCount: 2 }),
      execution({ activityExecutionId: "exec-wait", executionSequence: 3, status: "Suspended" })
    ];
    const labels: Record<string, string> = {
      "exec-ok": "Send greeting",
      "exec-bad": "Charge card",
      "exec-wait": "Wait for approval"
    };

    flushSync(() => root.render(
      <RunActivitiesTimeline
        context={{} as StudioEndpointContext}
        workflowExecutionId="run-1"
        activities={items}
        labelForActivity={item => labels[item.activityExecutionId] ?? item.activityType}
        onSelectActivity={() => {}}
        onRefresh={() => {}}
      />
    ));

    const filterBar = container.querySelector<HTMLElement>("[aria-label='Filter activities by status']")!;
    expect(filterBar).toBeTruthy();
    const faultedPill = [...filterBar.querySelectorAll<HTMLButtonElement>("button")].find(button => button.textContent?.includes("Faulted"))!;
    flushSync(() => faultedPill.click());

    const faultedRows = [...container.querySelectorAll<HTMLButtonElement>(".wf-activity-timeline-row")];
    expect(faultedRows).toHaveLength(1);
    expect(faultedRows[0]!.textContent).toContain("Charge card");
    expect(faultedRows[0]!.textContent).toContain("2 incidents");

    flushSync(() => faultedPill.click());
    const searchInput = container.querySelector<HTMLInputElement>("input[aria-label='Search activities']")!;
    flushSync(() => {
      const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!;
      setter.call(searchInput, "approval");
      searchInput.dispatchEvent(new Event("input", { bubbles: true }));
    });
    const searchedRows = [...container.querySelectorAll<HTMLButtonElement>(".wf-activity-timeline-row")];
    expect(searchedRows).toHaveLength(1);
    expect(searchedRows[0]!.textContent).toContain("Wait for approval");
    expect(searchedRows[0]!.textContent).toContain("Waiting");
  });

  it("loads additional activity pages when hasMoreFromSummary is true", async () => {
    const first = execution({ activityExecutionId: "exec-1", executionSequence: 1 });
    const extra = execution({ activityExecutionId: "exec-2", executionSequence: 2 });
    const onLoaded = vi.fn();
    runtimeApi.browseActivityExecutions.mockResolvedValue({
      items: [{ execution: extra, position: 2, previousActivityExecutionId: "exec-1", nextActivityExecutionId: null }],
      totalCount: 2,
      previousCursor: null,
      nextCursor: null
    });

    flushSync(() => root.render(
      <RunActivitiesTimeline
        context={{} as StudioEndpointContext}
        workflowExecutionId="run-1"
        activities={[first]}
        hasMoreFromSummary
        labelForActivity={item => item.activityExecutionId}
        onSelectActivity={() => {}}
        onRefresh={() => {}}
        onExecutionsLoaded={onLoaded}
      />
    ));

    const loadMore = [...container.querySelectorAll<HTMLButtonElement>("button")].find(button => button.textContent?.includes("Load more activities"))!;
    expect(loadMore).toBeTruthy();
    flushSync(() => loadMore.click());

    await vi.waitFor(() => expect(container.querySelectorAll(".wf-activity-timeline-row")).toHaveLength(2));
    expect(onLoaded).toHaveBeenCalledWith([extra]);
  });

  it("collapses and expands container subtrees, flags faulted hidden steps, renders span bars, and auto-expands external selection", async () => {
    const items = [
      execution({
        activityExecutionId: "exec-flow",
        activityType: "Elsa.Flowchart",
        startedAt: "2026-11-23T14:00:00.000Z",
        completedAt: "2026-11-23T14:00:04.000Z"
      }),
      execution({
        activityExecutionId: "exec-step1",
        parentActivityExecutionId: "exec-flow",
        startedAt: "2026-11-23T14:00:00.500Z",
        completedAt: "2026-11-23T14:00:01.500Z"
      }),
      execution({
        activityExecutionId: "exec-step2",
        parentActivityExecutionId: "exec-flow",
        status: "Faulted",
        incidentCount: 1,
        startedAt: "2026-11-23T14:00:02.000Z",
        completedAt: "2026-11-23T14:00:03.500Z"
      })
    ];
    const labels: Record<string, string> = {
      "exec-flow": "Flowchart",
      "exec-step1": "Validate order",
      "exec-step2": "Charge card"
    };
    const renderTree = (selectedId: string | null = null) => flushSync(() => root.render(
      <RunActivitiesTimeline
        context={{} as StudioEndpointContext}
        workflowExecutionId="run-1"
        activities={items}
        selectedActivityExecutionId={selectedId}
        labelForActivity={item => labels[item.activityExecutionId] ?? item.activityType}
        onSelectActivity={() => {}}
        onRefresh={() => {}}
      />
    ));

    renderTree(null);
    expect(container.querySelectorAll(".wf-activity-timeline-row")).toHaveLength(3);
    expect(container.querySelectorAll(".wf-activity-span-fill")).toHaveLength(3);

    const collapseFlow = container.querySelector<HTMLButtonElement>("button[aria-label='Collapse Flowchart']")!;
    expect(collapseFlow.getAttribute("aria-expanded")).toBe("true");
    flushSync(() => collapseFlow.click());

    expect(container.querySelectorAll(".wf-activity-timeline-row")).toHaveLength(1);
    expect(container.querySelector(".wf-activity-timeline-row")?.textContent).toContain("2 steps · 1 faulted");

    // Searching still finds hidden children inside collapsed containers.
    const searchInput = container.querySelector<HTMLInputElement>("input[aria-label='Search activities']")!;
    flushSync(() => {
      const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!;
      setter.call(searchInput, "Charge");
      searchInput.dispatchEvent(new Event("input", { bubbles: true }));
    });
    expect(container.querySelectorAll(".wf-activity-timeline-row")).toHaveLength(1);
    expect(container.querySelector(".wf-activity-timeline-row")?.textContent).toContain("Charge card");

    // Clearing search returns to collapsed state; external selection auto-expands ancestors.
    flushSync(() => {
      const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!;
      setter.call(searchInput, "");
      searchInput.dispatchEvent(new Event("input", { bubbles: true }));
    });
    expect(container.querySelectorAll(".wf-activity-timeline-row")).toHaveLength(1);

    renderTree("exec-step2");
    await vi.waitFor(() => expect(container.querySelectorAll(".wf-activity-timeline-row")).toHaveLength(3));
    expect(container.querySelector("button[aria-label='Collapse Flowchart']")?.getAttribute("aria-expanded")).toBe("true");
  });
});
