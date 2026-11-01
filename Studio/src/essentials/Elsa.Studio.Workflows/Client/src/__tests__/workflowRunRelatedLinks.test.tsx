import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { beforeEach, expect, it, vi } from "vitest";
import type { StudioBackendSession, StudioEndpointContext, StudioWorkflowRunRelatedLinkContribution, StudioWorkflowRunRelatedLinkContext } from "@elsa-workflows/studio-sdk";
import { getActivityExecutionInspection, getActivityStateView, supportsActivityStateView } from "../api/runtime";
import { WorkflowActivityExecutionDetails } from "../workflow-editor/WorkflowActivityExecutionDetails";
import type { ActivityExecutionStateSummary, ActivityStateView } from "../workflowTypes";

vi.mock("../api/runtime", async original => ({ ...(await original<typeof import("../api/runtime")>()),
  getActivityExecutionInspection: vi.fn(), getActivityStateView: vi.fn(), supportsActivityStateView: vi.fn() }));
const activityType = "Example.NativeWork";
function projection(): ActivityStateView {
  return { workflowExecutionId: "native-run", activityExecutionId: "activity-1", executableNodeId: "node-1", activityStatus: "Running", available: true,
    canInspectValues: false, viewKind: "example.run-reference", viewVersion: 1, view: { runId: "related-run" } };
}
beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(getActivityExecutionInspection).mockRejectedValue(new Error("Additional value evidence unavailable"));
  vi.mocked(supportsActivityStateView).mockResolvedValue(true);
  vi.mocked(getActivityStateView).mockResolvedValue(projection());
});
function fixture() {
  let context = { baseUrl: "https://backend.example", http: {} } as StudioEndpointContext;
  let session = { key: "connection", profile: { id: "backend-1", baseUrl: context.baseUrl }, context,
    identity: { status: "authenticated", subject: "author", tenantId: "tenant-1" }, capabilities: { capabilities: [] }, disposed: false } as unknown as StudioBackendSession;
  let activity = { workflowExecutionId: "native-run", activityExecutionId: "activity-1", executableNodeId: "node-1", authoredActivityId: "node-1", activityType,
    activityTypeVersion: "1.0.0", status: "Running", executionSequence: 1, scheduledAt: "2026-01-01T00:00:00Z", metadata: {} } as ActivityExecutionStateSummary;
  const getLinks = vi.fn((value: StudioWorkflowRunRelatedLinkContext) => [{ id: "related", label: "Open related run", path: `/example/runs?runId=related-run&returnTo=${encodeURIComponent(value.returnTo)}` }]);
  let contributions: StudioWorkflowRunRelatedLinkContribution[] = [{ id: "example", activityTypes: [activityType], viewKind: "example.run-reference", viewVersion: 1, getLinks }];
  let refreshKey = 0;
  const container = document.createElement("div"); document.body.appendChild(container); const root = createRoot(container);
  const render = (options: { session?: StudioBackendSession; context?: StudioEndpointContext; activity?: ActivityExecutionStateSummary; contributions?: StudioWorkflowRunRelatedLinkContribution[]; refreshKey?: number } = {}) => {
    session = options.session ?? session; context = options.context ?? context; activity = options.activity ?? activity; contributions = options.contributions ?? contributions;
    refreshKey = options.refreshKey ?? refreshKey;
    const props = { context, activity, activityCatalog: [], backendSession: session, runRelatedLinks: contributions, refreshKey };
    flushSync(() => root.render(<WorkflowActivityExecutionDetails {...props} />));
  };
  render();
  return { container, getLinks, render, get session() { return session; }, get context() { return context; }, get activity() { return activity; },
    dispose: () => { flushSync(() => root.unmount()); container.remove(); } };
}
it("shows a contextual link from the selected activity's authorized projection", async () => {
  const view = fixture();
  try {
    await vi.waitFor(() => expect(view.container.querySelector("a")?.textContent).toBe("Open related run"));
    expect(getActivityStateView).toHaveBeenCalledWith(view.context, "native-run", "activity-1", expect.any(AbortSignal));
    const target = new URL(view.container.querySelector<HTMLAnchorElement>("a")!.href);
    expect(target.searchParams.get("backend")).toBe("backend-1");
    const back = new URL(target.searchParams.get("returnTo")!, window.location.origin);
    expect(back.pathname).toBe("/workflows/instances/native-run"); expect(back.searchParams.get("selected")).toBe("activity-1");
    expect(back.searchParams.get("backend")).toBe("backend-1");
    expect(view.getLinks.mock.calls[0][0].stateView.data).toEqual({ runId: "related-run" });
  } finally { view.dispose(); }
});

it.each(["workflow", "activity", "node", "unavailable", "kind", "version"])("does not disclose mismatched or unavailable projected context: %s", mismatch => {
  const value = projection();
  if (mismatch === "workflow") value.workflowExecutionId = "other";
  if (mismatch === "activity") value.activityExecutionId = "other";
  if (mismatch === "node") value.executableNodeId = "other";
  if (mismatch === "unavailable") value.available = false;
  if (mismatch === "kind") value.viewKind = "other";
  if (mismatch === "version") value.viewVersion = 99;
  vi.mocked(getActivityStateView).mockResolvedValue(value);
  const view = fixture();
  return vi.waitFor(() => expect(view.container.textContent).toContain("Related run links are unavailable")).then(() => {
    expect(view.getLinks).not.toHaveBeenCalled(); expect(view.container.querySelector("a")).toBeNull();
  }).finally(view.dispose);
});

it.each(["backend", "subject", "tenant", "workflow", "activity", "node", "disposed", "transport"])("retires the outstanding projection before applying a changed %s", async change => {
  let finish!: (value: ActivityStateView) => void;
  vi.mocked(getActivityStateView).mockImplementationOnce(() => new Promise(resolve => { finish = resolve; }))
    .mockResolvedValue({ ...projection(), available: false });
  const view = fixture();
  try {
    await vi.waitFor(() => expect(getActivityStateView).toHaveBeenCalledOnce());
    if (change === "backend") {
      const context = { ...view.context };
      view.render({ context, session: { ...view.session, key: "other", context } });
    }
    if (change === "subject") view.render({ session: { ...view.session, identity: { ...view.session.identity, subject: "other" } } });
    if (change === "tenant") view.render({ session: { ...view.session, identity: { ...view.session.identity, tenantId: "other" } } });
    if (change === "workflow") view.render({ activity: { ...view.activity, workflowExecutionId: "other" } });
    if (change === "activity") view.render({ activity: { ...view.activity, activityExecutionId: "other" } });
    if (change === "node") view.render({ activity: { ...view.activity, executableNodeId: "other" } });
    if (change === "disposed") Object.defineProperty(view.session, "disposed", { value: true });
    if (change === "transport") Object.defineProperty(view.session, "context", { value: { ...view.context } });
    expect(view.container.querySelector("a")).toBeNull();
    finish(projection()); await new Promise(resolve => setTimeout(resolve, 0));
    expect(view.getLinks).not.toHaveBeenCalled(); expect(view.container.querySelector("a")).toBeNull();
  } finally { view.dispose(); }
});

it.each(["none", "other-activity", "missing-backend-capability"])("does not request a projection without an eligible contribution: %s", async missing => {
  const view = fixture();
  try {
    // First render starts a read; retire it before its asynchronous capability response is applied.
    if (missing === "none") view.render({ contributions: [] });
    if (missing === "other-activity") view.render({ activity: { ...view.activity, activityType: "Other.Activity" } });
    if (missing === "missing-backend-capability") view.render({ contributions: [{ id: "missing", activityTypes: [activityType], viewKind: "example.run-reference", viewVersion: 1,
      requiresBackend: [{ id: "absent.capability", contractVersion: "1" }], getLinks: view.getLinks }] });
    await new Promise(resolve => setTimeout(resolve, 0));
    expect(getActivityStateView).not.toHaveBeenCalled(); expect(view.getLinks).not.toHaveBeenCalled();
    expect(view.container.querySelector("[aria-label='Related runs']")).toBeNull();
    expect(view.container.querySelector(".wf-activity-overview")).not.toBeNull();
  } finally { view.dispose(); }
});

it.each(["absent", "denied"])("keeps native inspection useful when state-view reads are %s", async unavailable => {
  if (unavailable === "absent") vi.mocked(supportsActivityStateView).mockResolvedValue(false);
  else vi.mocked(getActivityStateView).mockRejectedValue(new Error("Not found or forbidden"));
  const view = fixture();
  try {
    await vi.waitFor(() => expect(view.container.textContent).toContain("Related run links are unavailable"));
    expect(view.getLinks).not.toHaveBeenCalled(); expect(view.container.querySelector(".wf-activity-overview")).not.toBeNull();
    if (unavailable === "absent") expect(getActivityStateView).not.toHaveBeenCalled();
  } finally { view.dispose(); }
});

it("rejects foreign destinations and isolates a broken contribution", async () => {
  const view = fixture();
  try {
    view.render({ contributions: [
      { id: "broken", activityTypes: [activityType], viewKind: "example.run-reference", viewVersion: 1, getLinks: () => { throw new Error("Broken module"); } },
      { id: "paths", activityTypes: [activityType], viewKind: "example.run-reference", viewVersion: 1, getLinks: () => [
        { id: "external", label: "External", path: "https://example.com" }, { id: "protocol", label: "External", path: "//example.com" },
        { id: "script", label: "Script", path: "javascript:alert(1)" }, { id: "backend", label: "Other backend", path: "/example/runs?backend=other" },
        { id: "valid", label: "Valid related run", path: "/example/runs?runId=exact" }
      ] }
    ] });
    await vi.waitFor(() => expect(view.container.querySelectorAll("a")).toHaveLength(1));
    expect(view.container.querySelector("a")?.textContent).toBe("Valid related run");
  } finally { view.dispose(); }
});

it("refuses a stale rendered link at activation time", async () => {
  const view = fixture();
  try {
    const link = await vi.waitFor(() => { const link = view.container.querySelector("a"); expect(link).not.toBeNull(); return link!; });
    Object.defineProperty(view.session, "disposed", { value: true });
    const click = new MouseEvent("click", { bubbles: true, cancelable: true });
    flushSync(() => link.dispatchEvent(click)); expect(click.defaultPrevented).toBe(true);
  } finally { view.dispose(); }
});

it("coalesces live revisions behind one exact read instead of aborting it repeatedly", async () => {
  let finish!: (value: ActivityStateView) => void;
  vi.mocked(getActivityStateView).mockImplementationOnce(() => new Promise(resolve => { finish = resolve; })).mockResolvedValue(projection());
  const view = fixture();
  try {
    await vi.waitFor(() => expect(getActivityStateView).toHaveBeenCalledOnce());
    const signal = vi.mocked(getActivityStateView).mock.calls[0][3]!;
    view.render({ refreshKey: 1 }); view.render({ refreshKey: 2 });
    expect(signal.aborted).toBe(false); expect(getActivityStateView).toHaveBeenCalledOnce();
    finish(projection());
    await vi.waitFor(() => expect(getActivityStateView).toHaveBeenCalledTimes(2));
    await vi.waitFor(() => expect(view.container.querySelector("a")?.textContent).toBe("Open related run"));
  } finally { view.dispose(); }
});

it("discovers a reference on a later live revision after the activity first reports unavailable state", async () => {
  vi.mocked(getActivityStateView).mockResolvedValueOnce({ ...projection(), available: false }).mockResolvedValue(projection());
  const view = fixture();
  try {
    await vi.waitFor(() => expect(view.container.textContent).toContain("Related run links are unavailable"));
    view.render({ refreshKey: 1 });
    await vi.waitFor(() => expect(view.container.querySelector("a")?.textContent).toBe("Open related run"));
    expect(getActivityStateView).toHaveBeenCalledTimes(2);
  } finally { view.dispose(); }
});
