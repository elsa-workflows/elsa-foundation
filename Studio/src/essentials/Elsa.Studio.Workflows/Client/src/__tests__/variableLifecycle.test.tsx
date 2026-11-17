import { StrictMode } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { clearApiCapabilityCache } from "../api/capabilities";
import {
  acceptRecordedCompletionActionModel,
  operatorActionPlan,
  operatorRefusalMessage
} from "../operatorActions";
import {
  countLostUpdates,
  inputRefreshReason,
  isBackgroundPlacement,
  presentValueState,
  readDisableBackgroundInputRefresh,
  readLostUpdateMode,
  variableWriteDiagnostics,
  writeDisableBackgroundInputRefresh,
  writeLostUpdateMode,
  writerLabel
} from "../variableLifecycle";
import { InputRefreshHistory, LostUpdateBadge, VariableWriteDiagnosticsSection } from "../workflow-editor/VariableLifecycleEvidence";
import { VariableLifecycleNodeSettings } from "../workflow-editor/VariableLifecycleNodeSettings";
import { useOperatorActions, type CompensationJournals, type OperatorActionsApi } from "../workflow-editor/useOperatorActions";
import type {
  ActivityExecutionInspection,
  ActivityExecutionStateSummary,
  ActivityInputRefreshView,
  ActivityNode,
  IncidentStateSummary,
  RecordedCompletionView,
  VariableWriteRecordView,
  WorkflowInstanceDetails
} from "../workflowTypes";

// #4735: Studio shows the variable lifecycle of spec 1221 (S-10).

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  sessionStorage.clear();
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(() => {
  flushSync(() => root.unmount());
  container.remove();
  clearApiCapabilityCache();
});

const labels: Record<string, string> = { "node-a": "Add bonus", "node-b": "Increment" };
const labelOf = (id: string) => labels[id];

function lostUpdateWarning(): VariableWriteRecordView {
  return {
    scopeId: "root", frameId: "frame-1", variableKey: "count",
    writer: { source: "Capture", executableNodeId: "node-b", invocationId: "inv-b" },
    outcome: "LostUpdateWarning",
    baseVersion: 0, currentVersion: 1, newVersion: 2, commitOrder: 4, commitSequence: 9,
    occurredAt: "2026-11-18T10:00:00Z",
    overwrittenWriter: { source: "Capture", executableNodeId: "node-a", invocationId: "inv-a" },
    writtenValue: { state: "omitted", digest: "sha256:abcdef0123456789" },
    overwrittenValue: { state: "captured", isOperational: true }
  };
}

function refusal(): VariableWriteRecordView {
  return {
    ...lostUpdateWarning(),
    outcome: "LostUpdateFault", newVersion: 1, writtenValue: null, overwrittenValue: null,
    rejectedValue: { state: "captured", isOperational: true }
  };
}

function recordedCompletion(): RecordedCompletionView {
  return {
    selectionState: "Named", outcomeNames: ["Done"], attemptId: "att-b", attemptOrdinal: 1,
    variableWrites: [{ variableKey: "count", value: { state: "captured", isOperational: true } }]
  };
}

function refresh(): ActivityInputRefreshView {
  return {
    supersededEvaluation: 1, replacedByEvaluation: 2,
    supersededMaterializedAt: "2026-11-18T09:59:00Z", refreshedAt: "2026-11-18T10:00:00Z",
    changes: [{ scopeId: "root", variableKey: "count", readVersion: 0, currentVersion: 1 }],
    supersededInputs: [{ inputKey: "Amount", value: { state: "redacted" } }]
  };
}

function inspection(overrides: Partial<ActivityExecutionInspection> = {}) {
  return { subStatus: null, valueSnapshots: [], variableWrites: null, recordedCompletion: null, ...overrides };
}

describe("variable lifecycle model", () => {
  it("reads and writes the two node settings as the design API carries them, omitting defaults", () => {
    const node = { nodeId: "n", activityVersionId: "v", inputs: [], outputs: [] } as ActivityNode;

    expect(readLostUpdateMode(node)).toBe("Default");
    const fault = writeLostUpdateMode(node, "Fault");
    expect(fault.lostUpdateMode).toBe(1);
    expect(readLostUpdateMode(fault)).toBe("Fault");
    expect(readLostUpdateMode({ ...node, lostUpdateMode: "Fault" })).toBe("Fault");
    expect("lostUpdateMode" in writeLostUpdateMode(fault, "Default")).toBe(false);

    const disabled = writeDisableBackgroundInputRefresh(node, true);
    expect(readDisableBackgroundInputRefresh(disabled)).toBe(true);
    expect("disableBackgroundInputRefresh" in writeDisableBackgroundInputRefresh(disabled, false)).toBe(false);

    expect(isBackgroundPlacement({ ...node, executionPlacement: 1 })).toBe(true);
    expect(isBackgroundPlacement({ ...node, executionPlacement: "Background" })).toBe(true);
    expect(isBackgroundPlacement(node)).toBe(false);
  });

  it("counts lost updates and keeps only the records that need attention", () => {
    const applied = { ...lostUpdateWarning(), outcome: "Current" };
    const accepted = { ...lostUpdateWarning(), outcome: "AcceptedOverride" };

    expect(countLostUpdates([lostUpdateWarning(), refusal(), applied, accepted])).toBe(2);
    expect(variableWriteDiagnostics({ variableWrites: [applied, lostUpdateWarning(), accepted] }).map(record => record.outcome))
      .toEqual(["LostUpdateWarning", "AcceptedOverride"]);
    expect(variableWriteDiagnostics(null)).toEqual([]);
  });

  it("names writers by label, node id, or kind of write", () => {
    expect(writerLabel({ source: "Capture", executableNodeId: "node-a" }, labelOf)).toBe("Add bonus");
    expect(writerLabel({ source: "Intrinsic", executableNodeId: "node-x" }, labelOf)).toBe("node-x (Set Variable)");
    expect(writerLabel({ source: "Alteration" })).toBe("an operator alteration");
    expect(writerLabel(null)).toBe("an earlier writer");
  });

  it("keeps value states distinct and never treats an operational value as readable here", () => {
    expect(presentValueState({ state: "captured", isOperational: true }).label).toBe("Kept for recovery");
    expect(presentValueState({ state: "captured", isOperational: true }).detail).toContain("write history view");
    expect(presentValueState({ state: "captured", evidenceId: "ev-1" }).label).toBe("Captured");
    expect(presentValueState({ state: "omitted" }).label).toBe("Not captured");
    expect(presentValueState({ state: "redacted" }).label).toBe("Redacted");
    expect(presentValueState({ state: "expired" }).label).toBe("Expired");
  });

  it("gives the reason of a background input refresh", () => {
    expect(inputRefreshReason({ changes: [
      { scopeId: "root", variableKey: "count", readVersion: 0, currentVersion: 1 },
      { scopeId: "root", variableKey: "total", readVersion: 2, currentVersion: 3 }
    ] })).toBe("count v0 → v1, total v2 → v3");
  });
});

describe("Accept recorded completion", () => {
  const incident = { incidentId: "inc-5", workflowExecutionId: "wf-42", activityExecutionId: "ae-11" };

  it("explains itself against Retry and Re-execute and submits AcceptRecordedCompletion/1 with the retry payload", () => {
    const model = acceptRecordedCompletionActionModel(incident, "Increment", recordedCompletion());

    expect(model.kind).toBe("accept");
    expect(model.confirmLabel).toBe("Accept recorded completion");
    expect(model.reasonRequired).toBe(false);
    expect(model.effect.join(" ")).toContain("Its body does not run again");
    expect(model.effect.join(" ")).toContain("Retry would run the same invocation again on its committed inputs");
    expect(model.effect.join(" ")).toContain("Re-execute a new invocation on fresh inputs");
    expect(model.effect.join(" ")).toContain('"count"');
    expect(operatorActionPlan(model, { reason: " recorded result is right " })).toEqual({
      target: { executionIds: ["wf-42"] },
      alterations: [{ kind: "AcceptRecordedCompletion", schemaVersion: 1, payload: { sourceActivityExecutionId: "ae-11", reason: "recorded result is right" } }]
    });
    expect(operatorActionPlan(model, { reason: "" }).alterations[0].payload).toEqual({ sourceActivityExecutionId: "ae-11" });
  });

  it("names its refusals", () => {
    expect(operatorRefusalMessage("NoRecordedCompletion")).toContain("no recorded completion");
    expect(operatorRefusalMessage("RecordedCompletionContractChanged")).toContain("contract changed");
    expect(operatorRefusalMessage("RecordedCompletionScopeChanged")).toContain("no longer declared");
  });
});

describe("run view", () => {
  it("shows a lost-update warning with both writers, the versions and the overwritten value", () => {
    flushSync(() => root.render(<VariableWriteDiagnosticsSection inspection={inspection({ variableWrites: [lostUpdateWarning()] })} labelOf={labelOf} />));

    const text = container.textContent!;
    expect(container.querySelector(".wf-lost-update-status .studio-status-chip[data-tone=warning]")!.textContent).toContain("1 lost update");
    expect(text).toContain("Lost update");
    expect(text).toContain("built on v0, current v1 → v2");
    expect(text).toContain("without seeing the value Add bonus wrote");
    expect(text).toContain("WriterIncrement");
    expect(text).toContain("Overwritten writerAdd bonus");
    expect(text).toContain("Overwritten valueKept for recovery");
    expect(text).toContain("Modify variable");
    expect(text).toContain("commit 9");
  });

  it("shows a Fault refusal with what was refused and why, and the recorded completion", () => {
    flushSync(() => root.render(<VariableWriteDiagnosticsSection
      inspection={inspection({ subStatus: "VariableLostUpdate", variableWrites: [refusal()], recordedCompletion: recordedCompletion() })} labelOf={labelOf} />));

    const text = container.textContent!;
    expect(text).toContain("Write refused");
    expect(text).toContain("faulted with VariableLostUpdate");
    expect(text).toContain("Changed byAdd bonus");
    expect(text).toContain("Refused valueKept for recovery");
    expect(text).not.toContain("Written value");
    expect(text).toContain("Recorded completion kept");
    expect(text).toContain("outcome Done and would write count");
  });

  it("shows an accepted override with the value it overwrote and where its written value came from", () => {
    const accepted: VariableWriteRecordView = {
      ...lostUpdateWarning(), outcome: "AcceptedOverride", baseVersion: null,
      writer: { source: "AcceptedCompletion", executableNodeId: "node-b", invocationId: "inv-b2" },
      writtenValue: { state: "omitted" }, overwrittenValue: { state: "captured", isOperational: true }
    };
    flushSync(() => root.render(<VariableWriteDiagnosticsSection inspection={inspection({ variableWrites: [accepted] })} labelOf={labelOf} />));

    const text = container.textContent!;
    expect(text).toContain("Accepted override");
    expect(text).toContain("Overwritten valueKept for recovery");
    expect(text).toContain("The value the accepted completion recorded");
    expect(text).not.toContain("Variable writes is off");
    expect(container.querySelector(".wf-lost-update-status")).toBeNull();
  });

  it("renders nothing for a step whose writes all applied", () => {
    flushSync(() => root.render(<VariableWriteDiagnosticsSection inspection={inspection({ variableWrites: [{ ...lostUpdateWarning(), outcome: "Current" }] })} />));
    expect(container.innerHTML).toBe("");
  });

  it("shows superseded background input evaluations with the reason", () => {
    flushSync(() => root.render(<InputRefreshHistory refreshes={[refresh()]} />));

    const text = container.textContent!;
    expect(text).toContain("Evaluation 1");
    expect(text).toContain("replaced by evaluation 2");
    expect(text).toContain("because count v0 → v1");
    expect(text).toContain("Amount (evaluation 1)Redacted");
  });

  it("keeps the lost-update badge reusable: it reports, or opens a view when given one", () => {
    const onSelect = vi.fn();
    flushSync(() => root.render(<><LostUpdateBadge count={0} /><LostUpdateBadge count={3} onSelect={onSelect} /></>));

    const badge = container.querySelector<HTMLButtonElement>("button.wf-lost-update-badge")!;
    expect(container.querySelectorAll(".wf-lost-update-badge, .wf-lost-update-status")).toHaveLength(1);
    expect(badge.textContent).toContain("3 lost updates");
    badge.click();
    expect(onSelect).toHaveBeenCalledTimes(1);
  });
});

describe("designer node settings", () => {
  const base = { nodeId: "n", activityVersionId: "v", inputs: [], outputs: [] } as ActivityNode;

  it("edits the lost-update mode", () => {
    const onChange = vi.fn();
    flushSync(() => root.render(<VariableLifecycleNodeSettings node={base} onChange={onChange} />));

    const select = container.querySelector("select")!;
    expect(select.value).toBe("Default");
    flushSync(() => {
      select.value = "Fault";
      select.dispatchEvent(new Event("change", { bubbles: true }));
    });
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ lostUpdateMode: 1 }));
  });

  it("offers the background refresh switch only for background placement", () => {
    const onChange = vi.fn();
    flushSync(() => root.render(<VariableLifecycleNodeSettings node={base} onChange={onChange} />));
    expect(container.querySelector<HTMLInputElement>("input[type=checkbox]")!.disabled).toBe(true);
    expect(container.textContent).toContain("Applies to background placement only");

    flushSync(() => root.render(<VariableLifecycleNodeSettings node={{ ...base, executionPlacement: 1 }} onChange={onChange} />));
    const checkbox = container.querySelector<HTMLInputElement>("input[type=checkbox]")!;
    expect(checkbox.disabled).toBe(false);
    flushSync(() => checkbox.click());
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ disableBackgroundInputRefresh: true }));
  });

  it("is read-only when the draft is", () => {
    flushSync(() => root.render(<VariableLifecycleNodeSettings node={{ ...base, executionPlacement: 1 }} readOnly onChange={vi.fn()} />));
    expect(container.querySelector("select")!.disabled).toBe(true);
    expect(container.querySelector<HTMLInputElement>("input[type=checkbox]")!.disabled).toBe(true);
  });
});

describe("incident operator actions", () => {
  const journals: CompensationJournals = { get: () => undefined, request: () => undefined, reload: async () => "unavailable", reset: () => undefined, generation: 0 };

  function incident(activityExecutionId = "ae-11"): IncidentStateSummary {
    return {
      incidentId: activityExecutionId === "ae-11" ? "inc-5" : `inc-${activityExecutionId}`, workflowExecutionId: "wf-42", activityExecutionId,
      severity: "Error", status: "Blocking", isBlocking: true,
      failureType: "VariableLostUpdate", message: "Lost update on count", createdAt: "2026-11-18T10:00:00Z", metadata: {},
      resolutionOutcome: { actionKind: "WaitForIntervention", metadata: {} }
    };
  }

  function activities(): ActivityExecutionStateSummary[] {
    const common = { workflowExecutionId: "wf-42", activityTypeVersion: "1.0.0", scheduledAt: "2026-11-18T10:00:00Z", metadata: {} };
    const faulted = (id: string, node: string): ActivityExecutionStateSummary => ({ ...common, activityExecutionId: id, executableNodeId: node, authoredActivityId: node,
      activityType: "Increment", status: "Faulted", subStatus: "VariableLostUpdate", parentActivityExecutionId: "ae-parent", provenance: { parentActivityExecutionId: "ae-parent" } });
    return [
      { ...common, activityExecutionId: "ae-parent", executableNodeId: "flow", authoredActivityId: "flow", activityType: "Flowchart", status: "Running" },
      faulted("ae-11", "node-b"),
      faulted("ae-12", "node-c")
    ];
  }

  function backend(recorded: RecordedCompletionView | null, gate?: Promise<void>) {
    const submits: unknown[] = [];
    const getJson = vi.fn(async (url: string, options?: { signal?: AbortSignal }) => {
      if (url.endsWith("capabilities")) return {
        capabilities: [{
          id: "elsa.api.runtime", contractVersion: "1",
          links: [
            { rel: "workflow-alteration-plans", href: "runtime/workflows/alteration-plans" },
            { rel: "workflow-alteration-plan", href: "runtime/workflows/alteration-plans/{planId}", templated: true },
            { rel: "workflow-alteration-plan-jobs-page", href: "runtime/workflows/alteration-plans/{planId}/jobs/page", templated: true },
            { rel: "workflow-alteration-job", href: "runtime/workflows/alteration-plans/{planId}/jobs/{jobId}", templated: true },
            { rel: "activity-execution", href: "runtime/workflows/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}", templated: true }
          ]
        }]
      };
      const read = url.match(/\/activity-executions\/(ae-\d+)/)?.[1];
      if (read) {
        await (gate ?? new Promise(resolve => setTimeout(resolve, 0)));
        // As a real fetch does, a read whose signal is aborted before it answers rejects.
        if (options?.signal?.aborted) throw new DOMException("The read was aborted.", "AbortError");
        return { activityExecutionId: read, workflowExecutionId: "wf-42", recordedCompletion: read === "ae-11" ? recorded : null };
      }
      return { planId: "plan-1", status: "Completed" };
    });
    const requestResponse = vi.fn(async (_url: string, init: RequestInit) => {
      submits.push(JSON.parse(String(init.body)));
      return new Response(JSON.stringify({ planId: "plan-1", status: "CapturingTargets", submissionDisposition: "Accepted", createdAt: "" }), { status: 202, headers: { "Content-Type": "application/json" } });
    });
    return { context: { baseUrl: `test://accept-${Math.random()}`, http: { getJson, requestResponse } } as unknown as StudioEndpointContext, submits, getJson };
  }

  function Harness({ context, sink, second = false }: { context: StudioEndpointContext; sink: { api?: OperatorActionsApi }; second?: boolean }) {
    const details: WorkflowInstanceDetails = {
      instance: { workflowExecutionId: "wf-42", status: "Running" } as WorkflowInstanceDetails["instance"],
      activities: activities(), incidents: second ? [incident(), incident("ae-12")] : [incident()], permissions: { canManage: true, canInspectValues: false }
    };
    const api = useOperatorActions({
      context, workflowExecutionId: "wf-42", details, root: null, activities: activities(), journals,
      labelOf: () => undefined, refreshRun: async () => details
    });
    sink.api = api;
    return <>{api.dialog}{api.renderIncidentActions(incident(), "Increment", () => undefined, () => undefined)}</>;
  }

  async function settle() {
    for (let round = 0; round < 8; round++) await new Promise(resolve => setTimeout(resolve, 0));
  }

  const buttons = () => [...container.querySelectorAll(".wf-incident-operator-actions button")].map(button => button.textContent!.trim());

  it("offers Accept recorded completion next to Retry when the faulted step has a recorded completion, and submits it", async () => {
    const fake = backend(recordedCompletion());
    const sink: { api?: OperatorActionsApi } = {};
    flushSync(() => root.render(<Harness context={fake.context} sink={sink} />));
    await settle();
    flushSync(() => root.render(<Harness context={fake.context} sink={sink} />));
    await settle();

    expect(buttons()).toEqual(["Retry", "Accept recorded completion"]);
    expect(container.querySelector(".wf-incident-operator-explanation")!.textContent)
      .toContain("Retry runs the same invocation again on its committed inputs; Re-execute runs a new invocation on fresh inputs");

    flushSync(() => [...container.querySelectorAll<HTMLButtonElement>(".wf-incident-operator-actions button")]
      .find(button => button.textContent!.includes("Accept"))!.click());
    await settle();
    expect(container.querySelector("[role=dialog] h3")!.textContent).toBe('Accept the recorded completion of "Increment"?');
    flushSync(() => container.querySelector<HTMLButtonElement>("button[type=submit]")!.click());
    await settle();
    expect(fake.submits).toEqual([{
      target: { executionIds: ["wf-42"] },
      alterations: [{ kind: "AcceptRecordedCompletion", schemaVersion: 1, payload: { sourceActivityExecutionId: "ae-11" } }]
    }]);
  });

  it("reads a faulted step again when another incident arrives while its first read is in flight", async () => {
    let release!: () => void;
    const fake = backend(recordedCompletion(), new Promise<void>(resolve => { release = resolve; }));
    const sink: { api?: OperatorActionsApi } = {};
    const reads = (id: string) => fake.getJson.mock.calls.filter(([url]) => String(url).includes(`/activity-executions/${id}`)).length;
    flushSync(() => root.render(<StrictMode><Harness context={fake.context} sink={sink} /></StrictMode>));
    await settle();
    flushSync(() => root.render(<StrictMode><Harness context={fake.context} sink={sink} /></StrictMode>));
    await settle();
    expect(reads("ae-11")).toBeGreaterThan(0);

    // A refresh brings a second waiting incident: the candidates change and the first read of ae-11 is cancelled.
    const before = reads("ae-11");
    flushSync(() => root.render(<StrictMode><Harness context={fake.context} sink={sink} second /></StrictMode>));
    await settle();
    release();
    await settle();
    flushSync(() => root.render(<StrictMode><Harness context={fake.context} sink={sink} second /></StrictMode>));
    await settle();

    expect(reads("ae-11")).toBeGreaterThan(before);
    expect(buttons()).toEqual(["Retry", "Accept recorded completion"]);
  });

  it("offers only Retry when the faulted step has no recorded completion", async () => {
    const fake = backend(null);
    const sink: { api?: OperatorActionsApi } = {};
    flushSync(() => root.render(<Harness context={fake.context} sink={sink} />));
    await settle();
    flushSync(() => root.render(<Harness context={fake.context} sink={sink} />));
    await settle();

    expect(buttons()).toEqual(["Retry"]);
    expect(fake.getJson.mock.calls.filter(([url]) => String(url).includes("/activity-executions/ae-11"))).toHaveLength(1);
  });
});
