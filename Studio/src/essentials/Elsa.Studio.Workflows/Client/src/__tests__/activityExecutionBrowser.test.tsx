import React from "react";
import { createRoot, type Root } from "react-dom/client";
import { flushSync } from "react-dom";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { ActivityExecutionInspectionPanel, ActivityExecutionNavigator, ChildExecutionBrowser, useScopedCanvasActivities } from "../workflow-editor/ActivityExecutionBrowser";
import { browseActivityExecutions, supportsActivityExecutionBrowsing, type ActivityExecutionBrowseEntry, type ActivityExecutionBrowsePage } from "../api/runtime";
import { latestActivityExecution } from "../runtimeActivityOrder";
import type { ActivityExecutionStateSummary } from "../workflowTypes";
vi.mock("../api/runtime", () => ({ browseActivityExecutions: vi.fn(), supportsActivityExecutionBrowsing: vi.fn() }));
let root: Root;
let container: HTMLDivElement;
const context = { baseUrl: "http://backend", http: {} } as StudioEndpointContext;
const execution = (id: number, node = "ordinary-node"): ActivityExecutionStateSummary => ({
  activityExecutionId: `execution-${id}`, workflowExecutionId: "run", executableNodeId: node, authoredActivityId: node,
  activityType: "Any.CustomActivity", activityTypeVersion: "1", status: "Completed", executionSequence: id, metadata: {},
  scheduledAt: "2026-10-25T10:00:00Z", startedAt: "2026-10-25T10:00:00Z", completedAt: "2026-10-25T10:00:00.005Z"
});
const entry = (id: number): ActivityExecutionBrowseEntry => ({ execution: { ...execution(id), executionSequence: id }, position: id, iterationPosition: id,
  previousActivityExecutionId: id > 1 ? `execution-${id - 1}` : null, nextActivityExecutionId: id < 10 ? `execution-${id + 1}` : null });
const page = (items = [entry(9), entry(10)]): ActivityExecutionBrowsePage => ({ items, totalCount: 10, previousCursor: "earlier", nextCursor: null });
const onResolve = vi.fn(), onSelect = vi.fn();
function button(label: string) {
  const found = [...container.querySelectorAll("button")].find(item => item.getAttribute("aria-label") === label || item.textContent?.includes(label));
  expect(found, `button ${label}`).toBeTruthy(); return found!;
}
function click(label: string) { flushSync(() => button(label).click()); }
function navigator(props: Partial<React.ComponentProps<typeof ActivityExecutionNavigator>> = {}) {
  flushSync(() => root.render(<ActivityExecutionNavigator context={context} workflowExecutionId="run" executableNodeId="ordinary-node"
    selectedActivityExecutionId="execution-10" refreshKey={0} onResolve={onResolve} onSelect={onSelect} onIncident={vi.fn()} {...props} />));
}
beforeEach(() => {
  vi.resetAllMocks(); vi.mocked(supportsActivityExecutionBrowsing).mockResolvedValue(true);
  vi.mocked(browseActivityExecutions).mockResolvedValue(page());
  container = document.createElement("div"); document.body.appendChild(container); root = createRoot(container);
});
afterEach(() => { flushSync(() => root.unmount()); container.remove(); });

it("selects latest and exact neighbors for an arbitrary activity", async () => {
  navigator({ selectedActivityExecutionId: undefined });
  await vi.waitFor(() => expect(container.textContent).toContain("Execution 10 of 10"));
  await vi.waitFor(() => expect(onResolve).toHaveBeenLastCalledWith(expect.objectContaining({ activityExecutionId: "execution-10" })));
  click("Previous activity execution");
  expect(onSelect).toHaveBeenLastCalledWith(expect.objectContaining({ activityExecutionId: "execution-9" }));
  expect(button("Next activity execution").disabled).toBe(true);
});
it("expands an accessible history with timing and pages without selecting another execution", async () => {
  navigator(); await vi.waitFor(() => expect(container.textContent).toContain("Execution 10 of 10"));
  click("Execution 10 of 10");
  expect(container.querySelector('[aria-current="true"]')?.textContent).toContain("Execution 10");
  expect(container.querySelector("time")).toBeTruthy();
  vi.mocked(browseActivityExecutions).mockResolvedValue({ ...page([entry(1)]), previousCursor: null, nextCursor: "later" });
  click("Earlier");
  await vi.waitFor(() => expect(browseActivityExecutions).toHaveBeenLastCalledWith(context, "run", expect.objectContaining({ cursor: "earlier" }), expect.any(AbortSignal)));
  expect(onSelect).not.toHaveBeenCalled();
});
it("anchors a neighbor beyond the loaded page", async () => {
  vi.mocked(browseActivityExecutions).mockImplementation(async (_context, _run, request) => page([entry(request?.anchorActivityExecutionId === "execution-9" ? 9 : 10)]));
  navigator(); await vi.waitFor(() => expect(container.textContent).toContain("Execution 10 of 10")); click("Previous activity execution");
  await vi.waitFor(() => expect(onSelect).toHaveBeenCalledWith(expect.objectContaining({ activityExecutionId: "execution-9" })));
  expect(browseActivityExecutions).toHaveBeenCalledWith(context, "run", expect.objectContaining({ anchorActivityExecutionId: "execution-9", limit: 1 }), expect.any(AbortSignal));
});
it("does not substitute latest for a missing explicit execution", async () => {
  vi.mocked(browseActivityExecutions).mockResolvedValue(page([])); navigator({ selectedActivityExecutionId: "missing" });
  await vi.waitFor(() => expect(container.textContent).toContain("This execution is not available in this scope"));
  await vi.waitFor(() => expect(onResolve).toHaveBeenLastCalledWith(null)); expect(onSelect).not.toHaveBeenCalled();
});
it("aborts an old scope and ignores its late result", async () => {
  let release!: (value: ActivityExecutionBrowsePage) => void;
  vi.mocked(browseActivityExecutions).mockReturnValueOnce(new Promise(resolve => { release = resolve; })); navigator();
  await vi.waitFor(() => expect(browseActivityExecutions).toHaveBeenCalledTimes(1));
  const oldSignal = vi.mocked(browseActivityExecutions).mock.calls[0][3];
  vi.mocked(browseActivityExecutions).mockResolvedValue(page([entry(4)]));
  navigator({ executableNodeId: "other", selectedActivityExecutionId: "execution-4", ancestorActivityExecutionId: "body-4" });
  await vi.waitFor(() => expect(container.textContent).toContain("Execution 4 of 10")); release(page());
  expect(oldSignal?.aborted).toBe(true);
  await vi.waitFor(() => expect(onResolve).toHaveBeenLastCalledWith(expect.objectContaining({ activityExecutionId: "execution-4" })));
});
it("finishes slow same-query history reads before reconciling the newest live revision", async () => {
  const releases: Array<(value: ActivityExecutionBrowsePage) => void> = [];
  vi.mocked(browseActivityExecutions).mockImplementation(() => new Promise(resolve => releases.push(resolve)));
  navigator(); await vi.waitFor(() => expect(releases).toHaveLength(1));
  for (let revision = 1; revision <= 5; revision++) navigator({ refreshKey: revision });
  await new Promise(resolve => setTimeout(resolve, 0));
  expect(browseActivityExecutions).toHaveBeenCalledTimes(1);
  expect(vi.mocked(browseActivityExecutions).mock.calls[0][3]?.aborted).toBe(false);
  releases[0](page());
  await vi.waitFor(() => expect(container.textContent).toContain("Execution 10 of 10"));
  await vi.waitFor(() => expect(releases).toHaveLength(2));
  releases[1]({ ...page(), totalCount: 11 });
  await vi.waitFor(() => expect(container.textContent).toContain("Execution 10 of 11"));
  expect(browseActivityExecutions).toHaveBeenCalledTimes(2);
});
it("distinguishes unsupported, denied and failed contexts without retained rows", async () => {
  vi.mocked(supportsActivityExecutionBrowsing).mockResolvedValue(false); navigator();
  await vi.waitFor(() => expect(container.textContent).toContain("unavailable on this host")); expect(browseActivityExecutions).not.toHaveBeenCalled();
  vi.mocked(supportsActivityExecutionBrowsing).mockResolvedValue(true); vi.mocked(browseActivityExecutions).mockRejectedValue({ status: 403 }); navigator({ refreshKey: 1 });
  await vi.waitFor(() => expect(container.textContent).toContain("unavailable for this context")); expect(container.querySelector("li")).toBeNull();
  vi.mocked(browseActivityExecutions).mockRejectedValue(new Error("offline")); navigator({ refreshKey: 2 });
  await vi.waitFor(() => expect(container.textContent).toContain("could not be loaded")); expect(button("Retry")).toBeTruthy();
});
it("groups generic child executions and keeps retries distinct from iterations", async () => {
  const children = [entry(4), { ...entry(5), attempt: { attemptNumber: 2 } }].map(item => ({ ...item, execution: { ...item.execution, iterationId: `iteration-${item.position}` } }));
  vi.mocked(browseActivityExecutions).mockResolvedValue(page(children)); const enter = vi.fn();
  flushSync(() => root.render(<ChildExecutionBrowser context={context} execution={execution(1, "any-composite")} refreshKey={0} labelFor={() => "Pinned child"} onEnter={enter} onIncident={vi.fn()} />));
  click("Child executions"); await vi.waitFor(() => expect(container.textContent).toContain("Iteration 4"));
  expect(container.textContent).toContain("Execution 5"); expect(container.textContent).toContain("Attempt 2"); click("Iteration 4");
  expect(enter).toHaveBeenCalledWith(expect.objectContaining({ position: 4 }));
  expect(browseActivityExecutions).toHaveBeenCalledWith(context, "run", expect.objectContaining({ parentActivityExecutionId: "execution-1", executableNodeId: "ordinary-node" }), expect.any(AbortSignal));
});
it("queries only visible nodes inside the exact canvas scope, including unexecuted nodes", async () => {
  vi.mocked(browseActivityExecutions).mockResolvedValue(page([]));
  function Canvas() { const state = useScopedCanvasActivities(context, "run", "body-4", ["write", "unexecuted"], 0); return <div>{state.loading ? "Loading" : `${state.activities.length} recorded`}</div>; }
  flushSync(() => root.render(<Canvas />)); await vi.waitFor(() => expect(container.textContent).toBe("0 recorded"));
  expect(browseActivityExecutions).toHaveBeenCalledTimes(2);
  for (const call of vi.mocked(browseActivityExecutions).mock.calls) expect(call[2]).toEqual(expect.objectContaining({ ancestorActivityExecutionId: "body-4", limit: 1 }));
});

it("does not let a delayed neighbor override a newer explicit selection", async () => {
  let release!: (value: ActivityExecutionBrowsePage) => void;
  const loaded = { ...page([entry(51), entry(80)]), totalCount: 100 };
  vi.mocked(browseActivityExecutions).mockImplementation(async (_context, _run, request) => request?.anchorActivityExecutionId === "execution-50"
    ? new Promise(resolve => { release = resolve; }) : loaded);
  navigator({ selectedActivityExecutionId: "execution-51" });
  await vi.waitFor(() => expect(container.textContent).toContain("Execution 51 of 100")); click("Previous activity execution");
  await vi.waitFor(() => expect(release).toBeTypeOf("function"));
  navigator({ selectedActivityExecutionId: "execution-80" });
  await vi.waitFor(() => expect(container.textContent).toContain("Execution 80 of 100"));
  release(page([entry(50)])); await new Promise(resolve => setTimeout(resolve, 0));
  expect(onSelect).not.toHaveBeenCalledWith(expect.objectContaining({ activityExecutionId: "execution-50" }));
});
it("keeps a requested exact neighbor pending across live revisions", async () => {
  let release!: (value: ActivityExecutionBrowsePage) => void;
  vi.mocked(browseActivityExecutions).mockImplementation(async (_context, _run, request) => request?.anchorActivityExecutionId === "execution-50"
    ? new Promise(resolve => { release = resolve; }) : { ...page([entry(51)]), totalCount: 100 });
  navigator({ selectedActivityExecutionId: "execution-51" });
  await vi.waitFor(() => expect(container.textContent).toContain("Execution 51 of 100"));
  click("Previous activity execution");
  await vi.waitFor(() => expect(release).toBeTypeOf("function"));
  navigator({ selectedActivityExecutionId: "execution-51", refreshKey: 1 });
  release(page([entry(50)]));
  await vi.waitFor(() => expect(onSelect).toHaveBeenCalledWith(expect.objectContaining({ activityExecutionId: "execution-50" })));
});
it("removes inspected values when a subsequent history page confirms denial", async () => {
  flushSync(() => root.render(<ActivityExecutionInspectionPanel context={context} workflowExecutionId="run" nodeId="ordinary-node" nodeLabel="Ordinary"
    exactId="execution-10" seed={execution(10)} refreshKey={0} labelFor={() => "Ordinary"} onSelect={onSelect} onEnter={vi.fn()} onIncident={vi.fn()}
    renderDetails={(_execution, navigation) => <>{navigation}<span>private inspected evidence</span></>} />));
  await vi.waitFor(() => expect(container.textContent).toContain("private inspected evidence"));
  click("Execution 10 of 10"); vi.mocked(browseActivityExecutions).mockRejectedValue({ status: 403 }); click("Earlier");
  await vi.waitFor(() => expect(container.textContent).toContain("unavailable for this context"));
  expect(container.textContent).not.toContain("private inspected evidence");
});

it("preserves recorded sub-millisecond scheduling chronology before hash sequences", () => {
  const earlier = { ...execution(900), scheduledAt: "2026-10-25T10:00:00.0000001Z" };
  const later = { ...execution(100), scheduledAt: "2026-10-25T10:00:00.0000002Z" };
  expect(latestActivityExecution([earlier, later])?.activityExecutionId).toBe("execution-100");
});


it("requires a deliberate occurrence choice for repeated editor activities", async () => {
  navigator({ selectedActivityExecutionId: undefined, requireExplicitSelection: true });
  await vi.waitFor(() => expect(container.textContent).toContain("10 recorded executions"));
  expect(onResolve).toHaveBeenLastCalledWith(null);
  expect(container.textContent).toContain("Choose a recorded execution");
  click("10 recorded executions"); click("Execution 9");
  expect(onSelect).toHaveBeenCalledWith(expect.objectContaining({ activityExecutionId: "execution-9" }));
});

it("resolves an unambiguous editor occurrence without widening its scope", async () => {
  vi.mocked(browseActivityExecutions).mockResolvedValue({ items: [entry(4)], totalCount: 1, previousCursor: null, nextCursor: null });
  navigator({ selectedActivityExecutionId: undefined, requireExplicitSelection: true, ancestorActivityExecutionId: "branch-4" });
  await vi.waitFor(() => expect(onResolve).toHaveBeenCalledWith(expect.objectContaining({ activityExecutionId: "execution-4" })));
  expect(browseActivityExecutions).toHaveBeenCalledWith(context, "run", expect.objectContaining({ ancestorActivityExecutionId: "branch-4" }), expect.any(AbortSignal));
});

it("keeps the inspected occurrence mounted during refresh and clears it when the exact read becomes unavailable", async () => {
  const panel = (refreshKey: number) => flushSync(() => root.render(<ActivityExecutionInspectionPanel context={context} workflowExecutionId="run"
    nodeId="ordinary-node" nodeLabel="Activity" exactId="execution-10" seed={null} refreshKey={refreshKey}
    labelFor={value => value.activityExecutionId} onSelect={onSelect} onEnter={vi.fn()} onIncident={vi.fn()}
    renderDetails={(value, navigation) => <section><p data-evidence="">{value.activityExecutionId}</p>{navigation}</section>} />));
  panel(0); await vi.waitFor(() => expect(container.querySelector("[data-evidence]")?.textContent).toBe("execution-10"));
  let release!: (value: ActivityExecutionBrowsePage) => void;
  vi.mocked(browseActivityExecutions).mockImplementation(() => new Promise(resolve => { release = resolve; }));
  const evidence = container.querySelector("[data-evidence]");
  panel(1);
  expect(container.querySelector("[data-evidence]")).toBe(evidence);
  await vi.waitFor(() => expect(release).toBeTypeOf("function"));
  release(page([]));
  await vi.waitFor(() => expect(container.querySelector("[data-evidence]")).toBeNull());
  expect(onSelect).not.toHaveBeenCalled();
});

it("resolves a scoped singleton without presenting redundant navigation", async () => {
  const single = { ...entry(4), previousActivityExecutionId: null, nextActivityExecutionId: null };
  vi.mocked(browseActivityExecutions).mockResolvedValue({ items: [single], totalCount: 1, previousCursor: null, nextCursor: null });
  navigator({ selectedActivityExecutionId: undefined, ancestorActivityExecutionId: "body-4", requireExplicitSelection: true, hideSingleton: true });
  await vi.waitFor(() => expect(onResolve).toHaveBeenCalledWith(single.execution));
  expect(container.querySelector("button")).toBeNull();
  expect(browseActivityExecutions).toHaveBeenCalledWith(context, "run", expect.objectContaining({ ancestorActivityExecutionId: "body-4" }), expect.any(AbortSignal));
});

it("does not hide missing explicit selections or singleton read failures", async () => {
  vi.mocked(browseActivityExecutions).mockResolvedValue({ items: [entry(4)], totalCount: 1, previousCursor: null, nextCursor: null });
  navigator({ selectedActivityExecutionId: "missing", hideSingleton: true });
  await vi.waitFor(() => expect(container.textContent).toContain("This execution is not available in this scope"));
  expect(onResolve).toHaveBeenLastCalledWith(null);
  vi.mocked(browseActivityExecutions).mockRejectedValue(new Error("offline"));
  navigator({ selectedActivityExecutionId: "missing", hideSingleton: true, refreshKey: 1 });
  await vi.waitFor(() => expect(container.textContent).toContain("could not be loaded"));
  expect(button("Retry")).toBeTruthy();
});

it("keeps singleton evidence mounted on refresh and revokes it after denial", async () => {
  vi.mocked(browseActivityExecutions).mockResolvedValue({ items: [entry(4)], totalCount: 1, previousCursor: null, nextCursor: null });
  const panel = (refreshKey: number) => flushSync(() => root.render(<ActivityExecutionInspectionPanel context={context}
    workflowExecutionId="run" nodeId="ordinary-node" nodeLabel="Ordinary" exactId="execution-4" seed={null}
    ancestorId="body-4" refreshKey={refreshKey} labelFor={() => "Ordinary"} onSelect={onSelect} onEnter={vi.fn()} onIncident={vi.fn()}
    renderDetails={(_value, navigation) => <section><span data-single-evidence="">Exact scoped value</span>{navigation}</section>} />));
  panel(0); await vi.waitFor(() => expect(container.textContent).toContain("Exact scoped value"));
  expect(container.querySelector("button")).toBeNull();
  const evidence = container.querySelector("[data-single-evidence]");
  let refuse!: (error: unknown) => void;
  vi.mocked(browseActivityExecutions).mockImplementation(() => new Promise((_resolve, reject) => { refuse = reject; }));
  panel(1); await vi.waitFor(() => expect(refuse).toBeTypeOf("function"));
  expect(container.querySelector("[data-single-evidence]")).toBe(evidence);
  vi.mocked(browseActivityExecutions).mockRejectedValue({ status: 403 });
  refuse({ status: 403 });
  await vi.waitFor(() => expect(container.textContent).toContain("unavailable for this context"));
  expect(container.querySelector("[data-single-evidence]")).toBeNull();
  expect(onSelect).not.toHaveBeenCalled();
});

it("brings navigation back when a singleton gains another recorded occurrence", async () => {
  vi.mocked(browseActivityExecutions).mockResolvedValue({ items: [{ ...entry(4), position: 1 }], totalCount: 1, previousCursor: null, nextCursor: null });
  navigator({ selectedActivityExecutionId: "execution-4", hideSingleton: true });
  await vi.waitFor(() => expect(onResolve).toHaveBeenCalled());
  expect(container.querySelector("button")).toBeNull();
  vi.mocked(browseActivityExecutions).mockResolvedValue({ items: [{ ...entry(4), position: 1 }, { ...entry(5), position: 2 }], totalCount: 2, previousCursor: null, nextCursor: null });
  navigator({ selectedActivityExecutionId: "execution-4", hideSingleton: true, refreshKey: 1 });
  await vi.waitFor(() => expect(container.textContent).toContain("Execution 1 of 2"));
  expect(button("Next activity execution")).toBeTruthy();
});

it("shows an unavailable selection when a refresh replaces the singleton with another occurrence", async () => {
  vi.mocked(browseActivityExecutions).mockResolvedValue({ items: [entry(4)], totalCount: 1, previousCursor: null, nextCursor: null });
  navigator({ selectedActivityExecutionId: "execution-4", hideSingleton: true });
  await vi.waitFor(() => expect(onResolve).toHaveBeenLastCalledWith(execution(4)));
  expect(container.querySelector("button")).toBeNull();
  vi.mocked(browseActivityExecutions).mockResolvedValue({ items: [entry(5)], totalCount: 1, previousCursor: null, nextCursor: null });
  navigator({ selectedActivityExecutionId: "execution-4", hideSingleton: true, refreshKey: 1 });
  await vi.waitFor(() => expect(onResolve).toHaveBeenLastCalledWith(null));
  expect(container.textContent).toContain("This execution is not available in this scope.");
  expect(container.textContent).not.toContain("Execution 4 of 1");
  expect(onSelect).not.toHaveBeenCalled();
});

it("labels recorded iterations with scope context and keeps attempts as executions", async () => {
  const iteration = { ...entry(3), execution: { ...execution(3), executionSequence: 3, iterationId: "iteration-3" }, iterationPosition: 3 };
  const retry = { ...entry(4), execution: { ...execution(4), executionSequence: 4, iterationId: "iteration-3" }, iterationPosition: 3, attempt: { attemptNumber: 2 } };
  vi.mocked(browseActivityExecutions).mockResolvedValue({ items: [iteration, retry], totalCount: 4, previousCursor: null, nextCursor: null });
  navigator({ selectedActivityExecutionId: "execution-3", contextLabel: "Prepare one item", collapseOnSelect: true });
  await vi.waitFor(() => expect(container.textContent).toContain("Iteration 3"));
  expect(button("Previous occurrence of Prepare one item")).toBeTruthy();
  expect(button("Next occurrence of Prepare one item")).toBeTruthy();
  expect(button("Choose occurrence of Prepare one item: Iteration 3, recorded execution 3 of 4")).toBeTruthy();
  click("Iteration 3");
  expect(container.querySelector("ol")?.textContent).toContain("Execution 4");
  expect(container.querySelector("ol")?.textContent).toContain("Attempt 2");
  click("Execution 4");
  expect(onSelect).toHaveBeenLastCalledWith(retry.execution);
  expect(container.querySelector("ol")).toBeNull();
  expect(document.activeElement).toBe(button("Choose occurrence of Prepare one item: Iteration 3, recorded execution 3 of 4"));
});

it("does not steal focus after a delayed choice when user moved away", async () => {
  const focusRequest = { current: null as string | null };
  vi.mocked(browseActivityExecutions).mockResolvedValue({ items: [entry(4),entry(5)],totalCount:10,previousCursor:null,nextCursor:null });
  const panel = (id: string) => flushSync(() => root.render(<ActivityExecutionInspectionPanel context={context}
    workflowExecutionId="run" nodeId="ordinary-node" nodeLabel="Ordinary" exactId={id} seed={null}
    refreshKey={0} choiceFocus={focusRequest} labelFor={()=>"Ordinary"} onSelect={onSelect} onEnter={vi.fn()} onIncident={vi.fn()}
    renderDetails={(_value,navigation)=><section><span>evidence</span>{navigation}</section>}/>));
  panel("execution-4");
  await vi.waitFor(()=>expect(container.textContent).toContain("Execution 4 of 10"));
  click("Execution 4 of 10"); click("Execution 5");
  expect(focusRequest.current).toBe("execution-5");
  const outside = document.createElement("button"); outside.textContent="Refresh"; document.body.append(outside); outside.focus();
  expect(document.activeElement).toBe(outside);
  panel("execution-5");
  await vi.waitFor(()=>expect(container.textContent).toContain("Execution 5 of 10"));
  await new Promise(resolve=>setTimeout(resolve,10));
  const actual=document.activeElement;
  outside.remove();
  expect(actual).toBe(outside);
});
