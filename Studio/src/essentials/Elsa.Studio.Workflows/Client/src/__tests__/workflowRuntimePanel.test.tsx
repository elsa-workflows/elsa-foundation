import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { WorkflowRuntimePanel } from "../workflow-editor/editorPanels";
import type { WorkflowInstanceSummary, WorkflowTestRunView } from "../workflowTypes";
import type { TestRunObservation } from "../workflow-editor/useTestRunObservation";

let container: HTMLDivElement;
let root: Root;
const receipt: WorkflowTestRunView = {
  testRunId: "test-run-exact", definitionId: "definition-1", definitionVersionId: "draft:1",
  artifactId: "artifact/exact value", workflowExecutionId: "execution-exact",
  status: "DispatchAccepted", commandDispatchStatus: "Accepted"
};

function observation(status: string, extra: Partial<TestRunObservation> = {}, instance: Partial<WorkflowInstanceSummary> = {}): TestRunObservation {
  return {
    details: { instance: { status, workflowExecutionId: receipt.workflowExecutionId, artifactId: receipt.artifactId, ...instance } },
    status: "connected", waits: [], waitsLoading: false, waitsError: "", error: "",
    ...extra
  } as TestRunObservation;
}

beforeEach(() => {
  container = document.createElement("div"); document.body.appendChild(container);
  root = createRoot(container);
});
afterEach(() => { flushSync(() => root.unmount()); container.remove(); vi.restoreAllMocks(); });

describe("Runtime panel information hierarchy", () => {
  it("keeps pending cancellation visible until Runtime confirms a terminal execution", () => {
    flushSync(() => root.render(<WorkflowRuntimePanel testRun={{ ...receipt, status: "StopRequested" }} observation={observation("Running")} onOpenRun={() => {}} />));
    expect(container.querySelector(".wf-status-badge")?.textContent).toBe("Stopping");
    expect(container.querySelector(".wf-runtime-outcome")?.textContent).toContain("Waiting for runtime confirmation");
    flushSync(() => root.render(<WorkflowRuntimePanel testRun={{ ...receipt, status: "StopRequested" }} observation={observation("Cancelled")} onOpenRun={() => {}} />));
    expect(container.querySelector(".wf-status-badge")?.textContent).toBe("Cancelled");
  });

  it("reports a stopped prepared test without a root as stopped", () => {
    flushSync(() => root.render(<WorkflowRuntimePanel testRun={{ ...receipt, status: "Stopped", workflowExecutionId: null }} onOpenRun={() => {}} />));
    expect(container.querySelector(".wf-status-badge")?.textContent).toBe("Stopped");
    expect(container.querySelector(".wf-runtime-outcome")?.textContent).not.toContain("Waiting");
  });

  it.each([["DispatchDeferred", "Deferred"], ["DispatchRejected", "Rejected"], ["FutureState", "FutureState"]])("does not label %s as accepted", (status, expected) => {
    flushSync(() => root.render(<WorkflowRuntimePanel testRun={{ ...receipt, status, commandDispatchStatus: null }} onOpenRun={() => {}} />));
    expect(container.querySelector(".wf-status-badge")?.textContent).toBe(expected);
    expect(container.querySelector(".wf-runtime-outcome")?.textContent).not.toContain("Test accepted");
  });

  it("prefers observed runtime counts and incidents over an earlier dispatch receipt", () => {
    flushSync(() => root.render(<WorkflowRuntimePanel testRun={{ ...receipt, activityCount: 0, incidentCount: 0 }}
      observation={observation("Completed", {}, { activityCount: 9, incidentCount: 3 })} onOpenRun={() => {}} />));
    expect(container.querySelector(".wf-runtime-incident-warning")?.textContent).toContain("3 incidents");
    expect(container.textContent).toContain("9 activities");
    expect(container.textContent).not.toContain("0 incidents");
  });

  it("leads with confirmed execution and reveals exact copyable IDs on demand", async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, "clipboard", { configurable: true, value: { writeText } });
    flushSync(() => root.render(<WorkflowRuntimePanel testRun={receipt} observation={observation("Completed")} onOpenRun={() => {}} />));
    expect(container.querySelector(".wf-runtime-outcome")?.textContent).toContain("completed");
    expect(container.querySelector(".wf-runtime-card h3")?.textContent).toBe("Latest test run");
    const disclosure = container.querySelector<HTMLButtonElement>("button[aria-expanded]")!;
    expect(disclosure).not.toBeNull();
    expect(disclosure.getAttribute("aria-expanded")).toBe("false");
    const body = document.getElementById(disclosure.getAttribute("aria-controls")!)!;
    expect(body.hidden).toBe(true);
    flushSync(() => disclosure.click());
    expect(body.hidden).toBe(false);
    const copy = container.querySelector<HTMLButtonElement>('button[aria-label="Copy Artifact ID"]')!;
    flushSync(() => copy.click());
    await vi.waitFor(() => expect(writeText).toHaveBeenCalledWith(receipt.artifactId));
  });

  it("offers exact resource navigation without presenting unavailable counts as zero", () => {
    const onOpenRun = vi.fn();
    flushSync(() => root.render(<WorkflowRuntimePanel testRun={receipt} onOpenRun={onOpenRun} />));
    const run = [...container.querySelectorAll<HTMLButtonElement>("button")].find(button => button.textContent === "Open run")!;
    expect(run).toBeTruthy(); flushSync(() => run.click());
    expect(onOpenRun).toHaveBeenCalledWith("execution-exact");
    const artifact = container.querySelector<HTMLAnchorElement>('a[href*="/workflows/executables/"]')!;
    expect(artifact?.getAttribute("href")).toContain("/workflows/executables/artifact%2Fexact%20value");
    expect(container.textContent).not.toContain("Available on linked Run");
    expect(container.textContent).not.toMatch(/0 activities|0 incidents/);
    expect(container.querySelector(".wf-runtime-outcome")?.textContent).toContain("Waiting for execution status");
  });

  it.each(["Faulted", "Cancelled", "Suspended", "Running"])("uses confirmed %s execution rather than the dispatch receipt", status => {
    flushSync(() => root.render(<WorkflowRuntimePanel testRun={receipt} observation={observation(status)} onOpenRun={() => {}} />));
    expect(container.querySelector(".wf-runtime-card .wf-status-badge")?.textContent).toBe(status === "Suspended" ? "Waiting" : status);
    if (status === "Faulted") expect(container.querySelector(".wf-runtime-incident-warning")).not.toBeNull();
  });

  it("keeps an earlier draft warning outside the disclosure", () => {
    flushSync(() => root.render(<WorkflowRuntimePanel testRun={{ view: receipt, draftSignature: "old" }} currentDraftSignature="new" observation={observation("Completed")} onOpenRun={() => {}} />));
    const warning = container.querySelector(".wf-test-drive-stale");
    expect(warning?.textContent).toContain("earlier draft snapshot");
    expect(warning?.closest(".studio-technical-details")).toBeNull();
  });

  it("does not imply a missing incident count is zero when Runtime reports a fault", () => {
    flushSync(() => root.render(<WorkflowRuntimePanel testRun={receipt} observation={observation("Faulted")} onOpenRun={() => {}} />));
    expect(container.querySelector(".wf-runtime-incident-warning")?.textContent).toContain("runtime issue");
    expect(container.textContent).not.toContain("0 incidents");
  });

  it("qualifies last confirmed state when following is paused", () => {
    flushSync(() => root.render(<WorkflowRuntimePanel testRun={receipt} observation={observation("Running", { status: "paused" })} onOpenRun={() => {}} />));
    expect(container.querySelector(".wf-runtime-outcome")?.textContent).toContain("Last confirmed");
    expect(container.querySelector(".wf-runtime-outcome")?.textContent).toContain("paused");
  });

  it("shows expired evidence rather than a successful dispatch badge", () => {
    flushSync(() => root.render(<WorkflowRuntimePanel testRun={{ ...receipt, status: "Expired" }} onOpenRun={() => {}} />));
    expect(container.querySelector(".wf-runtime-card .wf-status-badge")?.textContent).toBe("Expired");
    expect(container.querySelector(".wf-runtime-outcome")?.textContent).toContain("expired");
  });
});
