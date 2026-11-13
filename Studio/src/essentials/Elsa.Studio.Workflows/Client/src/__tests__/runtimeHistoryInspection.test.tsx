import { flushSync } from "react-dom";
import { useState } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { authSessionEndedEvent, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { ActivityBookmarkHistory, HistoryInspection } from "../workflow-editor/run-history/HistoryInspection";
import { HistoryCoverage, HistoryEventList, historyDate, historyAuthorityLabel } from "../workflow-editor/run-history/historyPresentation";
import { RunHistoryTimeline } from "../workflow-editor/run-history/RunHistoryTimeline";
import { defaultHistoryFilters } from "../workflow-editor/run-history/historyLocation";
import { RunHistorySummary } from "../workflow-editor/run-history/RunHistorySummary";
import { historyBookmark, historyCoverage, historyEvent, historyPage } from "./runtimeHistoryFixtures";
import type { RuntimeHistoryBookmarkPage } from "../runtimeHistoryTypes";
import type { ActivityExecutionStateSummary, WorkflowInstanceSummary } from "../workflowTypes";
const valueApi = vi.hoisted(() => ({ getActivityExecutionInspection: vi.fn(), getActivityExecutionValuePayload: vi.fn() }));
vi.mock("../api/runtime", async original => ({ ...await original<typeof import("../api/runtime")>(), getActivityExecutionInspection: valueApi.getActivityExecutionInspection }));
vi.mock("../api/activityExecutionValuePayload", () => ({ getActivityExecutionValuePayload: valueApi.getActivityExecutionValuePayload }));
const api = vi.hoisted(() => ({ getWorkflowHistoryBookmark: vi.fn(), getWorkflowHistoryEvent: vi.fn(), listWorkflowHistoryBookmarks: vi.fn(), getWorkflowHistoryPage: vi.fn(), supportsWorkflowHistory: vi.fn() }));
vi.mock("../api/runtimeHistory", async original => ({ ...await original<typeof import("../api/runtimeHistory")>(), ...api }));
const context = { baseUrl: "https://history.example" } as StudioEndpointContext;
let root: Root, container: HTMLDivElement;
const scrollIntoView = vi.fn();
const originalScrollIntoView = HTMLElement.prototype.scrollIntoView;
function render(ui: React.ReactNode) { flushSync(() => root.render(ui)); }
function click(text: string) { const target = [...container.querySelectorAll("button")].find(item => item.textContent?.includes(text)); expect(target).toBeTruthy(); flushSync(() => target!.click()); }
function bookmarkPage(overrides: Partial<RuntimeHistoryBookmarkPage> = {}): RuntimeHistoryBookmarkPage { return { coverage: historyCoverage, items: [historyBookmark()], totalCount: null, activeCount: null, scopeComplete: true, readAt: "2026-11-05T09:02:00Z", ...overrides }; }
beforeEach(() => {
  HTMLElement.prototype.scrollIntoView = scrollIntoView;
  vi.resetAllMocks(); api.supportsWorkflowHistory.mockResolvedValue(true); api.getWorkflowHistoryPage.mockResolvedValue(historyPage());
  api.listWorkflowHistoryBookmarks.mockResolvedValue(bookmarkPage());
  api.getWorkflowHistoryBookmark.mockResolvedValue({ coverage: historyCoverage, bookmark: historyBookmark(), readAt: "2026-11-05T09:02:00Z" });
  api.getWorkflowHistoryEvent.mockResolvedValue({ coverage: historyCoverage, event: historyEvent(), readAt: "2026-11-05T09:02:00Z" });
  container = document.createElement("div"); document.body.append(container); root = createRoot(container);
});
afterEach(() => { flushSync(() => root.unmount()); container.remove(); HTMLElement.prototype.scrollIntoView = originalScrollIntoView; vi.restoreAllMocks(); });
it("shows bookmark disposition separately from passive expiry, missing creation and current eligibility", async () => {
  api.getWorkflowHistoryBookmark.mockResolvedValue({ coverage: historyCoverage, bookmark: historyBookmark({ priorState: "Unknown", createdHistoryEventId: null }), readAt: "now" });
  render(<HistoryInspection context={context} workflowExecutionId="run" selection={{ kind: "bookmark", id: "bookmark-1" }} onSelect={vi.fn()} onBack={vi.fn()} />);
  await vi.waitFor(() => expect(container.textContent).toContain("Expiry makes this handle unmatchable"));
  expect(container.textContent).toContain("Active"); expect(container.textContent).toContain("Expired");
  expect(container.textContent).toContain("Creation event not captured"); expect(container.textContent).toContain("Handle lifetimeUnknown");
  expect(container.textContent).not.toContain("Inspect creation event");
  expect([...container.querySelectorAll("button")].some(button => button.textContent === "Resume")).toBe(false);
});
it("links a retired competing handle to its winner and its own retirement event", async () => {
  api.getWorkflowHistoryBookmark.mockResolvedValue({ coverage: historyCoverage, bookmark: historyBookmark({ status: "Removed", eligibility: "not-active", selectedBookmarkId: "winner", endedHistoryEventId: "retired", endedAt: "2026-11-05T09:02:00Z" }), readAt: "now" });
  const select = vi.fn(); render(<HistoryInspection context={context} workflowExecutionId="run" selection={{ kind: "bookmark", id: "bookmark-1" }} onSelect={select} onBack={vi.fn()} />);
  await vi.waitFor(() => expect(container.textContent).toContain("Inspect winning bookmark")); click("Inspect winning bookmark");
  expect(select).toHaveBeenLastCalledWith({ kind: "bookmark", id: "winner" }); click("Inspect removed event");
  expect(select).toHaveBeenLastCalledWith({ kind: "event", id: "retired" });
});
it("retains a known end transition without inventing its missing end time or lifetime", async () => {
  api.getWorkflowHistoryBookmark.mockResolvedValue({ coverage: historyCoverage,
    bookmark: historyBookmark({ status: "Consumed", endedHistoryEventId: "consumed", endedAt: null, eligibility: "not-active" }), readAt: "now" });
  render(<HistoryInspection context={context} workflowExecutionId="run" selection={{ kind: "bookmark", id: "bookmark-1" }} onSelect={vi.fn()} onBack={vi.fn()} />);
  await vi.waitFor(() => expect(container.textContent).toContain("End time not recorded"));
  expect(container.textContent).toContain("Handle lifetimeUnknown");
  expect(container.textContent).toContain("Inspect consumed event");
  expect(container.textContent).not.toContain("No end transition recorded");
});
it("renders unknown totals honestly and retains loaded bookmark rows during the next page read", async () => {
  api.listWorkflowHistoryBookmarks.mockResolvedValueOnce(bookmarkPage({ nextContinuationToken: "second" }));
  let complete!: (page: RuntimeHistoryBookmarkPage) => void;
  api.listWorkflowHistoryBookmarks.mockImplementationOnce(() => new Promise(resolve => { complete = resolve; }));
  render(<ActivityBookmarkHistory context={context} workflowExecutionId="run" activityExecutionId="activity-1" onSelect={vi.fn()} />);
  await vi.waitFor(() => expect(container.textContent).toContain("lifetime total unavailable")); click("Load more bookmarks");
  await vi.waitFor(() => expect([...container.querySelectorAll<HTMLButtonElement>("button")].find(button => button.textContent === "Load more bookmarks")?.disabled).toBe(true));
  expect(container.textContent).toContain("bookmark-1"); complete(bookmarkPage({ items: [] }));
  await vi.waitFor(() => expect(api.listWorkflowHistoryBookmarks.mock.calls[1][2].continuationToken).toBe("second"));
  flushSync(() => window.dispatchEvent(new Event(authSessionEndedEvent)));
  await vi.waitFor(() => expect(container.textContent).not.toContain("bookmark-1"));
});
it("shows safe unknown-schema event details and exact causal navigation without value requests", async () => {
  api.getWorkflowHistoryEvent.mockResolvedValue({ coverage: historyCoverage, event: historyEvent(1, { schemaVersion: 99, authority: "99", metadata: { "history.valueEvidenceIds": '["value-1"]' }, causation: { historyEventId: "cause" } }), readAt: "now" });
  const select = vi.fn(); render(<HistoryInspection context={context} workflowExecutionId="run" selection={{ kind: "event", id: "event-1" }} onSelect={select} onBack={vi.fn()} onActivity={vi.fn()} />);
  await vi.waitFor(() => expect(container.textContent).toContain("unrecognized schema version"));
  expect(container.textContent).toContain("Unrecognized recorded authority (99)"); expect(container.textContent).toContain("1 phase-specific value reference");
  expect(api.getWorkflowHistoryPage).not.toHaveBeenCalled(); click("Inspect causal event"); expect(select).toHaveBeenCalledWith({ kind: "event", id: "cause" });
});
it("keeps event meaning and exact navigation ahead of collapsed technical identities without losing copyable provenance", async () => {
  api.getWorkflowHistoryEvent.mockResolvedValue({ coverage: historyCoverage, event: historyEvent(2, { name: "BookmarkConsumed", bookmarkId: "bookmark-1", physicalCommitId: "commit-1" }), readAt: "now" });
  const select = vi.fn(), activity = vi.fn();
  render(<HistoryInspection context={context} workflowExecutionId="run" selection={{ kind: "event", id: "event-2" }} onSelect={select} onBack={vi.fn()} onActivity={activity} />);
  await vi.waitFor(() => expect(container.textContent).toContain("Continuation and its outcome are separate recorded facts"));
  const technical = container.querySelector<HTMLDetailsElement>(".wf-history-technical")!;
  expect(technical.open).toBe(false);
  expect(technical.textContent).toContain("Atomic commit groupcommit-1");
  expect(technical.querySelector('[aria-label="Copy event id"]')).toBeTruthy();
  const bookmark = [...container.querySelectorAll("button")].find(button => button.textContent === "Inspect bookmark →")!;
  expect(technical.contains(bookmark)).toBe(false); flushSync(() => bookmark.click());
  expect(select).toHaveBeenLastCalledWith({ kind: "bookmark", id: "bookmark-1" });
  click("Inspect owning execution"); expect(activity).toHaveBeenLastCalledWith("activity-1");
  expect(valueApi.getActivityExecutionInspection).not.toHaveBeenCalled();
});
it("discloses omitted outcome text while retaining completion provenance without fetching current values", async () => {
  const hash = "a".repeat(64);
  api.getWorkflowHistoryEvent.mockResolvedValue({ coverage: historyCoverage, event: historyEvent(2, {
    name: "ActivityContinuationCompleted", physicalCommitId: "completion-commit", metadata: {
      "history.outcomeCaptureState": "size-omitted", "history.outcomeCount": "1", "history.outcomeContentHash": hash
    }
  }), readAt: "now" });
  render(<HistoryInspection context={context} workflowExecutionId="run" selection={{ kind: "event", id: "event-2" }} onSelect={vi.fn()} onBack={vi.fn()} />);
  await vi.waitFor(() => expect(container.textContent).toContain("Full outcome detail was not captured within the metadata size policy"));
  expect(container.querySelector("h3")?.textContent).toBe("Continuation completed");
  expect(container.textContent).toContain("Atomic commit groupcompletion-commit");
  expect(container.textContent).toContain("Outcome count1");
  expect(container.textContent).toContain(`Outcome content hash${hash}`);
  expect(container.querySelector('[aria-label="Copy outcome content hash"]')).toBeTruthy();
  expect(container.textContent).not.toContain("Recorded outcomes");
  expect(valueApi.getActivityExecutionInspection).not.toHaveBeenCalled();
  expect(valueApi.getActivityExecutionValuePayload).not.toHaveBeenCalled();
  expect(api.getWorkflowHistoryPage).not.toHaveBeenCalled();
});
it("labels committed responsibilities, source observations, settlement and incident decisions as distinct recorded events", () => {
  const labels = [
    ["ChildWorkflowDispatchRequested", "Child run dispatch requested"], ["StimulusPublicationCommitted", "Stimulus publication committed"],
    ["ChildWorkflowSettled", "Child run settled"], ["ChildStartAttemptObserved", "Child start attempt observed"],
    ["ChildStartDispositionObserved", "Child start disposition observed"], ["StimulusRoutingAttemptObserved", "Stimulus routing attempt observed"],
    ["StimulusRoutingDispositionObserved", "Stimulus routing disposition observed"], ["IncidentStrategyEvaluated", "Incident strategy evaluated"],
    ["IncidentProjectionReconciled", "Incident projection reconciled"], ["IncidentSuppressed", "Incident suppressed"],
    ["IncidentDecisionApplied", "Incident decision applied"], ["IncidentResolutionBatchApplied", "Incident strategy decisions applied"],
    ["ActivityContinuationStarted", "Activity callback started"], ["ActivityContinuationReturned", "Activity callback returned a result"],
    ["ActivityContinuationSuspended", "Continuation waiting"], ["ActivityContinuationCompleted", "Continuation completed"],
    ["ActivityContinuationFaulted", "Continuation faulted"], ["CompensationAbandoned", "Compensation abandoned"]
  ];
  const events = labels.map(([name], index) => historyEvent(index + 1, { name, authority: name.endsWith("Observed") ? "DeliveryObservation" : "CommittedTransition" }));
  render(<HistoryEventList items={events} onSelect={vi.fn()} />);
  expect([...container.querySelectorAll(".wf-history-event strong")].map(item => item.textContent)).toEqual(labels.map(([, label]) => label));
  expect(container.querySelectorAll(".wf-history-event small").length).toBeGreaterThan(0);
  expect(container.textContent?.match(/Recorded source observation/g)).toHaveLength(4);
});
it("keeps callback return distinct from a later continuation fault and recognizes abandoned compensation", async () => {
  api.getWorkflowHistoryEvent.mockResolvedValue({ coverage: historyCoverage, event: historyEvent(1, { name: "ActivityContinuationReturned" }), readAt: "now" });
  const element = (id: string) => <HistoryInspection context={context} workflowExecutionId="run" selection={{ kind: "event", id }} onSelect={vi.fn()} onBack={vi.fn()} />;
  render(element("event-1"));
  await vi.waitFor(() => expect(container.textContent).toContain("Activity callback returned a result"));
  expect(container.textContent).toContain("validation or disposal can still fail after the callback returns");
  expect(container.textContent).toContain("committed continuation outcome is recorded separately");
  api.getWorkflowHistoryEvent.mockResolvedValue({ coverage: historyCoverage, event: historyEvent(2, { name: "ActivityContinuationFaulted" }), readAt: "now" });
  render(element("event-2"));
  await vi.waitFor(() => expect(container.textContent).toContain("Continuation faulted"));
  expect(container.textContent).not.toContain("Callback faulted");
  api.getWorkflowHistoryEvent.mockResolvedValue({ coverage: historyCoverage, event: historyEvent(3, { name: "CompensationAbandoned" }), readAt: "now" });
  render(element("event-3"));
  await vi.waitFor(() => expect(container.textContent).toContain("Compensation abandoned"));
  expect(container.textContent).not.toContain("event kind is unrecognized");
});
it.each([
  ["ChildWorkflowDispatchRequested", "Child admission, execution and outcome are separate recorded facts"],
  ["StimulusPublicationCommitted", "Routing, recipient admission and execution are separate recorded facts"],
  ["ChildWorkflowSettled", "Parent continuation and its result are recorded separately"],
  ["ChildStartAttemptObserved", "does not establish that a child run started or executed"],
  ["ChildStartDispositionObserved", "the related run's history for actual execution"],
  ["StimulusRoutingAttemptObserved", "does not establish recipient admission or execution"],
  ["StimulusRoutingDispositionObserved", "Recipient execution and continuation outcomes require their own recorded facts"],
  ["IncidentStrategyEvaluated", "any separate resolution event show whether it was resolved"],
  ["IncidentProjectionReconciled", "does not by itself resolve the incident"],
  ["IncidentDecisionApplied", "separate lifecycle events show whether it was resolved"],
  ["IncidentResolutionBatchApplied", "does not mean every incident was resolved"]
])("explains %s without inferring recipient execution or incident resolution", async (name, explanation) => {
  api.getWorkflowHistoryEvent.mockResolvedValue({ coverage: historyCoverage, event: historyEvent(1, { name }), readAt: "now" });
  render(<HistoryInspection context={context} workflowExecutionId="run" selection={{ kind: "event", id: "event-1" }} onSelect={vi.fn()} onBack={vi.fn()} />);
  await vi.waitFor(() => expect(container.textContent).toContain(explanation));
  expect(container.textContent).not.toContain("event kind is unrecognized");
  expect(valueApi.getActivityExecutionInspection).not.toHaveBeenCalled();
  expect(api.getWorkflowHistoryPage).not.toHaveBeenCalled();
});
it.each([
  [{ "history.relatedWorkflowExecutionId": "parent/run?exact", "history.childWorkflowId": "older-child" }, "parent/run?exact"],
  [{ "history.childWorkflowId": "legacy-child" }, "legacy-child"]
])("opens the exact generic related run through normal authorized navigation", async (metadata, target) => {
  api.getWorkflowHistoryEvent.mockResolvedValue({ coverage: historyCoverage, event: historyEvent(1, { name: "ChildWorkflowSettled", metadata }), readAt: "now" });
  const navigate = vi.fn();
  render(<HistoryInspection context={context} workflowExecutionId="run" selection={{ kind: "event", id: "event-1" }} onSelect={vi.fn()} onBack={vi.fn()} navigate={navigate} />);
  await vi.waitFor(() => expect(container.textContent).toContain(`Open related run ${target}`));
  expect(navigate).not.toHaveBeenCalled(); click(`Open related run ${target}`);
  expect(navigate).toHaveBeenCalledWith(`/workflows/instances/${encodeURIComponent(target)}?view=timeline`);
  expect(container.querySelector('[aria-label="Copy related run"]')).toBeTruthy();
  expect(api.getWorkflowHistoryEvent).toHaveBeenCalledTimes(1);
});
it("keeps recorded sequence when clocks regress and labels every date transition and unloaded middle range", () => {
  const items = [historyEvent(1, { occurredAt: "2026-11-05T09:00:00Z" }), historyEvent(4, { occurredAt: "2026-11-04T09:00:00Z", authority: "DeliveryObservation" })];
  render(<HistoryEventList items={items} unloadedAfterSequence={3} onSelect={vi.fn()} />);
  expect([...container.querySelectorAll("[data-history-sequence]")].map(row => row.getAttribute("data-history-sequence"))).toEqual(["1", "4"]);
  expect([...container.querySelectorAll(".wf-history-date")].map(row => row.textContent)).toEqual(items.map(item => historyDate(item.occurredAt)));
  expect(container.textContent).toContain("Earlier snapshot history is not fully loaded"); expect(container.textContent).toContain("Recorded source observation");
  expect(historyAuthorityLabel("Unspecified")).toBe("Authority unspecified");
});

it.each([null, "2026-11-05T09:10:00Z"])("keeps unknown occurrence out of clock labels and handle durations (%s)", occurredAt => {
  render(<HistoryEventList items={[historyEvent(1), historyEvent(2, { name: "BookmarkConsumed", occurredAt, occurredAtKnown: false })]} onSelect={vi.fn()} />);
  const unknown = container.querySelector("[data-history-event-id='event-2'] time");
  expect(unknown?.textContent).toBe("Time unknown");
  expect(unknown?.hasAttribute("datetime")).toBe(false);
  expect(container.textContent).toContain("Occurrence time unknown");
  expect(container.textContent).not.toContain("Handle lifetime");
});

it("keeps unknown occurrence distinct from the independently recorded event instant", async () => {
  api.getWorkflowHistoryEvent.mockResolvedValue({ coverage: historyCoverage,
    event: historyEvent(1, { occurredAt: "1999-01-01T00:00:00Z", occurredAtKnown: false, recordedAt: "2026-11-13T10:00:00Z" }), readAt: "now" });
  render(<HistoryInspection context={context} workflowExecutionId="run" selection={{ kind: "event", id: "event-1" }} onSelect={vi.fn()} onBack={vi.fn()} />);
  await vi.waitFor(() => expect(container.textContent).toContain("OccurredNot recorded"));
  expect(container.textContent).toContain("2026-11-13T10:00:00Z");
  expect(container.textContent).not.toContain("1999");
});
it("does not claim an empty search is globally complete while another scanned range remains", async () => {
  api.getWorkflowHistoryPage.mockResolvedValue(historyPage([], { nextContinuationToken: "next" }));
  render(<RunHistoryTimeline context={context} workflowExecutionId="run" filters={{ ...defaultHistoryFilters, search: "delivery-42" }} onFiltersChange={vi.fn()} onSelectEvent={vi.fn()} />);
  await vi.waitFor(() => expect(container.textContent).toContain("No matching events in this searched range"));
  expect(container.textContent).toContain("Continue searching recorded history"); expect(container.textContent).toContain("more history not loaded");
});
it("submits search explicitly, clears query filters and keeps live/follow toggles independent", async () => {
  const changed = vi.fn();
  function ControlledTimeline() {
    const [filters, setFilters] = useState(defaultHistoryFilters);
    return <RunHistoryTimeline context={context} workflowExecutionId="run" filters={filters}
      onFiltersChange={next => { changed(next); setFilters(next); }} onSelectEvent={vi.fn()} />;
  }
  render(<ControlledTimeline />);
  await vi.waitFor(() => expect(api.getWorkflowHistoryPage).toHaveBeenCalledTimes(1));
  const input = container.querySelector<HTMLInputElement>('[role="searchbox"][aria-label="Search recorded events"]')!;
  flushSync(() => {
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!.call(input, "  bookmark-42  ");
    input.dispatchEvent(new Event("input", { bubbles: true }));
  });
  expect(changed).not.toHaveBeenCalled(); expect(api.getWorkflowHistoryPage).toHaveBeenCalledTimes(1);
  click("Search");
  await vi.waitFor(() => expect(changed).toHaveBeenLastCalledWith({ ...defaultHistoryFilters, search: "bookmark-42" }));
  await vi.waitFor(() => expect(api.getWorkflowHistoryPage.mock.calls.at(-1)?.[2].search).toBe("bookmark-42"));
  click("Clear filters");
  await vi.waitFor(() => expect(changed).toHaveBeenLastCalledWith(defaultHistoryFilters));
  const category = container.querySelector<HTMLSelectElement>('[aria-label="History category"]')!;
  flushSync(() => { category.value = "BookmarkLifecycle"; category.dispatchEvent(new Event("change", { bubbles: true })); });
  await vi.waitFor(() => expect(api.getWorkflowHistoryPage.mock.calls.at(-1)?.[2].category).toBe("BookmarkLifecycle"));
  const changesBeforeToggles = changed.mock.calls.length;
  click("Pause live updates");
  expect([...container.querySelectorAll("button")].find(button => button.textContent === "Resume live updates")?.getAttribute("aria-pressed")).toBe("false");
  click("Follow latest");
  expect([...container.querySelectorAll("button")].find(button => button.textContent === "Following latest")?.getAttribute("aria-pressed")).toBe("true");
  expect(scrollIntoView).toHaveBeenCalledWith({ block: "nearest" });
  expect(changed).toHaveBeenCalledTimes(changesBeforeToggles);
  expect(valueApi.getActivityExecutionInspection).not.toHaveBeenCalled(); expect(valueApi.getActivityExecutionValuePayload).not.toHaveBeenCalled();
});
it("discloses long open handles as waits for a stimulus and separates elapsed from active execution", async () => {
  const item = historyBookmark({ eligibility: "eligible", bookmark: { ...historyBookmark().bookmark, createdAt: "2026-11-03T09:00:00Z" } });
  api.listWorkflowHistoryBookmarks.mockResolvedValue(bookmarkPage({ items: [item] }));
  render(<RunHistorySummary context={context} instance={{ workflowExecutionId: "run", status: "Suspended", startedAt: "2026-11-03T09:00:00Z" } as WorkflowInstanceSummary} name="Approval" activities={[{ activityExecutionId: "activity-1", status: "Suspended", activityType: "Elsa.CreateUserTask" } as ActivityExecutionStateSummary]} activitiesLimited={false} onBookmark={vi.fn()} enabled />);
  await vi.waitFor(() => expect(container.textContent).toContain("Waiting at last observation"));
  expect(container.textContent).toContain("User Task Completed"); expect(container.textContent).toContain("Handle open for"); expect(container.textContent).toContain("Elapsed includes waiting");
});


it("separates current capture/source availability from the historically captured prefix and keeps unknown settlement explicit", () => {
  render(<HistoryCoverage coverage={{ ...historyCoverage, captureAvailable: false, captureLimitation: "Current writer unavailable", sourceHistoryPending: null, sourceHistoryLimitation: "Timer responsibility cannot be inspected" }} readAt="2026-11-05T09:02:00Z" scopeComplete={false} scopeLimitation="Ancestor unavailable" />);
  expect(container.textContent).toContain("History available from run start");
  expect(container.textContent).toContain("Unavailable now; recorded history remains separate");
  expect(container.textContent).toContain("Cannot establish source settlement");
  expect(container.textContent).toContain("Ancestor unavailable"); expect(container.textContent).toContain("Timer responsibility cannot be inspected");
  expect(container.textContent).not.toContain("capture was disabled");
});


it("waits for the exact recorded value action and keeps an earlier phase distinct from a later input with the same name", async () => {
  api.getWorkflowHistoryEvent.mockResolvedValue({ coverage: historyCoverage, event: historyEvent(1, { metadata: { "history.valueEvidenceIds": '["early-value"]', "history.valuePhase": "invoke", "history.valueEvaluationId": "evaluation-1" } }), readAt: "now" });
  const snapshot = { name: "Response", subject: "ActivityInput", capturedAt: "2026-11-05T09:00:00Z", captureMode: "DiagnosticSnapshot", captureState: "diagnosticSnapshotCaptured", accessState: "visible", phase: "invoke", sequence: 1, metadata: {} };
  valueApi.getActivityExecutionInspection.mockResolvedValue({ workflowExecutionId: "run", activityExecutionId: "activity-1", valueSnapshots: [
    { ...snapshot, evidenceId: "early-value", snapshot: { kind: "string", preview: "Earlier captured response" } },
    { ...snapshot, evidenceId: "later-value", phase: "resume", sequence: 2, snapshot: { kind: "string", preview: "Later captured response" } }
  ] });
  render(<HistoryInspection context={context} workflowExecutionId="run" selection={{ kind: "event", id: "event-1" }} onSelect={vi.fn()} onBack={vi.fn()} />);
  await vi.waitFor(() => expect(container.textContent).toContain("Inspect recorded value"));
  expect(valueApi.getActivityExecutionInspection).not.toHaveBeenCalled(); expect(valueApi.getActivityExecutionValuePayload).not.toHaveBeenCalled();
  click("Inspect recorded value");
  await vi.waitFor(() => expect(container.textContent).toContain("Earlier captured response"));
  expect(container.textContent).not.toContain("Later captured response");
  expect(container.querySelector("[aria-label='Recorded value Data']")?.textContent).toContain("invoke");
  expect(valueApi.getActivityExecutionInspection).toHaveBeenCalledWith(context, "run", "activity-1", expect.any(AbortSignal));
});

it("reports an unavailable old value reference without substituting the current evaluation", async () => {
  api.getWorkflowHistoryEvent.mockResolvedValue({ coverage: historyCoverage, event: historyEvent(1, { metadata: { "history.valueEvidenceIds": '["purged-value"]' } }), readAt: "now" });
  valueApi.getActivityExecutionInspection.mockResolvedValue({ workflowExecutionId: "run", activityExecutionId: "activity-1", valueSnapshots: [
    { evidenceId: "latest-value", name: "Response", snapshot: { kind: "string", preview: "Current response must not substitute" } }
  ] });
  render(<HistoryInspection context={context} workflowExecutionId="run" selection={{ kind: "event", id: "event-1" }} onSelect={vi.fn()} onBack={vi.fn()} />);
  await vi.waitFor(() => expect(container.textContent).toContain("Inspect recorded value")); click("Inspect recorded value");
  await vi.waitFor(() => expect(container.textContent).toContain("This exact captured observation is unavailable"));
  expect(container.textContent).not.toContain("Current response must not substitute"); expect(valueApi.getActivityExecutionValuePayload).not.toHaveBeenCalled();
});


it("searches causal successors only on request and excludes matching text without an exact causal reference", async () => {
  api.getWorkflowHistoryPage.mockResolvedValue(historyPage([
    historyEvent(2, { causation: { historyEventId: "event-1" } }),
    historyEvent(3, { causation: { historyEventId: "other-event" }, metadata: { "history.status": "event-1" } })
  ], { nextContinuationToken: "more-causal-range" }));
  render(<HistoryInspection context={context} workflowExecutionId="run" selection={{ kind: "event", id: "event-1" }} onSelect={vi.fn()} onBack={vi.fn()} />);
  await vi.waitFor(() => expect(container.textContent).toContain("Find causal successors")); expect(api.getWorkflowHistoryPage).not.toHaveBeenCalled();
  click("Find causal successors");
  await vi.waitFor(() => expect(container.querySelector("[data-history-event-id='event-2']")).toBeTruthy());
  expect(container.querySelector("[data-history-event-id='event-3']")).toBeNull();
  expect(container.textContent).toContain("Continue searching causal history");
  expect(api.getWorkflowHistoryPage.mock.calls[0][2].search).toBe("event-1");
});

it("refreshes the retained exact bookmark on checkpoint signals and explicit request, coalescing in-flight reads without changing selection", async () => {
  const active = { coverage: historyCoverage, bookmark: historyBookmark({ eligibility: "eligible" }), readAt: "2026-11-05T09:02:00Z" };
  const consumed = { coverage: historyCoverage, bookmark: historyBookmark({ status: "Consumed", eligibility: "not-active", endedAt: "2026-11-05T09:03:00Z", eligibilityObservedAt: "2026-11-05T09:03:01Z" }), readAt: "2026-11-05T09:03:01Z" };
  let complete!: (value: typeof consumed) => void;
  api.getWorkflowHistoryBookmark.mockResolvedValueOnce(active).mockImplementationOnce(() => new Promise(resolve => { complete = resolve; })).mockResolvedValue(consumed);
  const select = vi.fn();
  const element = (refreshKey: number) => <HistoryInspection context={context} workflowExecutionId="run" selection={{ kind: "bookmark", id: "bookmark-1" }} refreshKey={refreshKey} onSelect={select} onBack={vi.fn()} />;
  render(element(0)); await vi.waitFor(() => expect(container.textContent).toContain("current eligibility: Eligible"));
  render(element(1)); await vi.waitFor(() => expect(api.getWorkflowHistoryBookmark).toHaveBeenCalledTimes(2));
  const inFlight = api.getWorkflowHistoryBookmark.mock.calls[1][3] as AbortSignal;
  render(element(2)); expect(inFlight.aborted).toBe(false); expect(api.getWorkflowHistoryBookmark).toHaveBeenCalledTimes(2);
  expect(container.textContent).toContain("previous recorded observation"); expect(container.textContent).toContain("current eligibility: Eligible");
  complete(consumed); await vi.waitFor(() => expect(api.getWorkflowHistoryBookmark).toHaveBeenCalledTimes(3));
  await vi.waitFor(() => expect(container.textContent).toContain("current eligibility: Not active"));
  await vi.waitFor(() => expect([...container.querySelectorAll<HTMLButtonElement>("button")].find(button => button.textContent === "Refresh bookmark observation")?.disabled).toBe(false));
  click("Refresh bookmark observation"); await vi.waitFor(() => expect(api.getWorkflowHistoryBookmark).toHaveBeenCalledTimes(4));
  expect(select).not.toHaveBeenCalled(); expect(api.getWorkflowHistoryBookmark.mock.calls.every(call => call[1] === "run" && call[2] === "bookmark-1")).toBe(true);
});

it("retains individually observed bookmark pages during a bounded first-page refresh and replaces old pages only on success", async () => {
  const second = historyBookmark({ bookmark: { ...historyBookmark().bookmark, identity: { ...historyBookmark().bookmark.identity, bookmarkId: "older-loaded-page" } }, firstRecordedSequence: 2, eligibilityObservedAt: "2026-11-05T09:04:00Z" });
  let complete!: (value: RuntimeHistoryBookmarkPage) => void;
  api.listWorkflowHistoryBookmarks.mockResolvedValueOnce(bookmarkPage({ nextContinuationToken: "second" }))
    .mockResolvedValueOnce(bookmarkPage({ items: [second], readAt: "2026-11-05T09:04:00Z" }))
    .mockImplementationOnce(() => new Promise(resolve => { complete = resolve; }));
  render(<ActivityBookmarkHistory context={context} workflowExecutionId="run" activityExecutionId="activity-1" onSelect={vi.fn()} />);
  await vi.waitFor(() => expect(container.textContent).toContain("bookmark-1")); click("Load more bookmarks");
  await vi.waitFor(() => expect(container.textContent).toContain("older-loaded-page")); click("Refresh bookmarks");
  await vi.waitFor(() => expect(api.listWorkflowHistoryBookmarks).toHaveBeenCalledTimes(3));
  expect(api.listWorkflowHistoryBookmarks.mock.calls[2][2].continuationToken).toBeNull();
  expect(container.textContent).toContain("older-loaded-page"); await vi.waitFor(() => expect(container.textContent).toContain("Previously loaded observations remain visible"));
  expect(container.textContent).not.toContain("No bookmarks recorded");
  complete(bookmarkPage({ items: [historyBookmark({ status: "Consumed", eligibility: "not-active" })] }));
  await vi.waitFor(() => expect(container.textContent).not.toContain("older-loaded-page"));
  expect(container.textContent).toContain("Consumed"); expect(container.textContent).toContain("loaded handles retain their individual observation times");
});

it("keeps503 bookmark observations but stops a rejected continuation until explicit first-page refresh", async () => {
  vi.useFakeTimers();
  try {
    const second = historyBookmark({ bookmark: { ...historyBookmark().bookmark,
      identity: { ...historyBookmark().bookmark.identity, bookmarkId: "second-page-handle" } }, firstRecordedSequence: 2 });
    api.listWorkflowHistoryBookmarks.mockResolvedValueOnce(bookmarkPage({ nextContinuationToken: "second" }))
      .mockResolvedValueOnce(bookmarkPage({ items: [second] }))
      .mockRejectedValueOnce(Object.assign(new Error("temporary"), { status: 503 }))
      .mockRejectedValueOnce(Object.assign(new Error("cursor changed"), { status: 400 }))
      .mockResolvedValue(bookmarkPage({ items: [historyBookmark({ status: "Consumed", eligibility: "not-active" })] }));
    render(<ActivityBookmarkHistory context={context} workflowExecutionId="run" activityExecutionId="activity-1" onSelect={vi.fn()} />);
    await vi.waitFor(() => expect(container.textContent).toContain("Load more bookmarks")); click("Load more bookmarks");
    await vi.waitFor(() => expect(container.textContent).toContain("second-page-handle"));
    await vi.advanceTimersByTimeAsync(5000);
    await vi.waitFor(() => expect(container.textContent).toContain("temporary"));
    expect(container.textContent).toContain("second-page-handle");
    await vi.advanceTimersByTimeAsync(5000);
    await vi.waitFor(() => expect(container.textContent).toContain("continuation is invalid"));
    expect(container.textContent).not.toContain("second-page-handle");
    await vi.advanceTimersByTimeAsync(10000); expect(api.listWorkflowHistoryBookmarks).toHaveBeenCalledTimes(4);
    click("Refresh bookmarks"); await vi.waitFor(() => expect(container.textContent).toContain("Consumed"));
    expect(api.listWorkflowHistoryBookmarks.mock.calls[4][2].continuationToken).toBeNull();
  } finally { vi.useRealTimers(); }
});

it("shows one pending batch without enabling continuous Follow latest or moving for the next arrival", async () => {
  vi.useFakeTimers();
  try {
    api.getWorkflowHistoryPage.mockResolvedValueOnce(historyPage())
      .mockResolvedValueOnce(historyPage([historyEvent(4)], { upperSequence: 4, scannedThroughSequence: 4, catchUpToken: "after-4" }))
      .mockResolvedValueOnce(historyPage([], { upperSequence: 4, scannedThroughSequence: 4, catchUpToken: "after-4" }))
      .mockResolvedValueOnce(historyPage([historyEvent(5)], { upperSequence: 5, scannedThroughSequence: 5, catchUpToken: "after-5" }))
      .mockResolvedValue(historyPage([]));
    render(<RunHistoryTimeline context={context} workflowExecutionId="run" filters={defaultHistoryFilters} onFiltersChange={vi.fn()} onSelectEvent={vi.fn()} />);
    await vi.waitFor(() => expect(container.querySelector("[data-history-event-id='event-1']")).toBeTruthy());
    await vi.advanceTimersByTimeAsync(2000);
    await vi.waitFor(() => expect(container.textContent).toContain("1 new recorded events · Show"));
    click("1 new recorded events · Show");
    await vi.advanceTimersByTimeAsync(20);
    await vi.waitFor(() => expect(container.querySelector("[data-history-event-id='event-4']")).toBeTruthy());
    expect([...container.querySelectorAll("button")].find(item => item.textContent === "Follow latest")?.getAttribute("aria-pressed")).toBe("false");
    await vi.waitFor(() => expect(api.getWorkflowHistoryPage).toHaveBeenCalledTimes(3));
    scrollIntoView.mockClear();
    await vi.advanceTimersByTimeAsync(2000);
    await vi.waitFor(() => expect(container.textContent).toContain("1 new recorded events · Show"));
    expect(container.querySelector("[data-history-event-id='event-5']")).toBeNull();
    expect(scrollIntoView).not.toHaveBeenCalled();
  } finally { vi.useRealTimers(); }
});
