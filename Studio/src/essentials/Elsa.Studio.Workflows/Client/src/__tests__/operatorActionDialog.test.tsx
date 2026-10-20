import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import type { Node } from "@xyflow/react";
import { OperatorActionDialog } from "../workflow-editor/OperatorActionDialog";
import { CompensationScopeDesigner, type CompensationOperatorControls } from "../graph-authoring/CompensationScopeDesigner";
import { compensationScopeStructureKind, type CompensationStep } from "../compensationScope";
import type { CompensationRowRuntime, CompensationScopeRuntime } from "../compensationRuntime";
import {
  abandonActionModel,
  operatorActionPlan,
  resolveActionModel,
  retryActionModel,
  validateOperatorReason,
  type OperatorActionModel,
  type OperatorActionScopeContext
} from "../operatorActions";
import type { WorkflowNodeData } from "../workflowAdapter";
import type { ActivityNode } from "../workflowTypes";

// Spec 260 T421 (the confirmation dialog) and T422 (where the actions show).

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(() => {
  flushSync(() => root.unmount());
  container.remove();
});

const decisions = ["compensation.resolve.effect-absent", "compensation.resolve.effect-accepted", "compensation.abandon"];
const labels: Record<string, string> = { reserve: "Reserve inventory", release: "Release inventory", deliver: "Reserve delivery", cancel: "Cancel delivery", confirm: "Confirm order" };

describe("operator action models", () => {
  it("names the retry's target and consequence, with the handler's operation key and what recovery does next", () => {
    const model = retryActionModel(waitingRow(), scopeContext());

    expect(model.title).toBe('Retry the compensation of "Reserve delivery"?');
    expect(model.target).toEqual([
      'Scope "Order" › step "Reserve delivery" › compensation "Cancel delivery"',
      "Attempt 4 (limit 3 reached) · incident inc-5 · run wf-42"
    ]);
    expect(model.effect).toEqual([
      'Runs "Cancel delivery" again with the same operation key (ae-scope/step/1/compensate).',
      'If it succeeds, recovery continues with "Release inventory". If it fails, the scope waits again.'
    ]);
    expect(model.reasonRequired).toBe(false);
    expect(operatorActionPlan(model, { reason: "" })).toEqual({
      target: { executionIds: ["wf-42"] },
      alterations: [{ kind: "RetryFaultedActivity", schemaVersion: 1, payload: { sourceActivityExecutionId: "ae-11" } }]
    });
  });

  it("retries an uncertain step with the step's own key", () => {
    const model = retryActionModel(stepRow(), scopeContext());

    expect(model.title).toBe('Retry "Notify carrier"?');
    expect(model.target).toEqual(['Scope "Order" › step "Notify carrier"', "incident inc-2 · run wf-42"]);
    expect(model.effect[0]).toBe('Runs "Notify carrier" again with the same operation key (ae-scope/step/2).');
  });

  it("shows only the resolve effect lines that apply to the row", () => {
    const handler = resolveActionModel(waitingRow(), scopeContext());
    expect(handler.effect).toEqual(['Recovery continues with "Release inventory".', "The entry is never undone afterwards."]);
    expect(handler.outcomes?.map(outcome => outcome.label)).toEqual(["Effect absent or undone", "Effect present, keep it"]);
    expect(handler.reasonRequired).toBe(true);

    const step = resolveActionModel(stepRow(), scopeContext(), "compensation.resolve.effect-accepted");
    expect(step.effect).toEqual([
      "Effect absent: the completed steps are undone, newest first.",
      "Effect present: the scope carries on with the next step.",
      "The entry is never undone afterwards."
    ]);
    expect(step.initialDecision).toBe("compensation.resolve.effect-accepted");
    expect(operatorActionPlan(step, { reason: "Carrier confirmed", decision: "compensation.resolve.effect-accepted" }).alterations[0])
      .toEqual({ kind: "DecideIntervention", schemaVersion: 1, payload: { incidentId: "inc-2", decision: "compensation.resolve.effect-accepted", reason: "Carrier confirmed" } });
  });

  it("lists what an abandon leaves not undone and who decides next", () => {
    const top = abandonActionModel(scopeContext());
    expect(top.title).toBe('Abandon recovery of scope "Order"?');
    expect(top.tone).toBe("danger");
    expect(top.notUndone).toEqual(["Reserve inventory (waiting to undo)", "Reserve delivery (undo failed)"]);
    expect(top.effectAfter).toEqual(["The scope fails with compensation.scope.abandoned.", "The run's incident strategy decides next; by default the run fails."]);

    const nested = abandonActionModel(scopeContext({ enclosingScope: { scopeNodeId: "outer", label: "Fulfilment", activityExecutionId: "ae-outer" } }));
    expect(nested.effectAfter?.[1]).toBe('Inside "Fulfilment", that scope decides next.');
    expect(operatorActionPlan(nested, { reason: "Written off" }).alterations[0].payload).toEqual({ incidentId: "inc-5", decision: "compensation.abandon", reason: "Written off" });
  });

  it.each([
    ["", true, "Enter a reason."],
    ["   ", true, "Enter a reason."],
    ["", false, null],
    ["x".repeat(500), true, null],
    ["x".repeat(501), true, "Use at most 500 characters (now 501)."],
    ["line one\nline two", true, "Remove line breaks and other control characters."],
    ["tab\there", false, "Remove line breaks and other control characters."],
    ["Refunded manually, ticket FIN-221", true, null]
  ])("checks the reason %j (required %s)", (reason, required, expected) => {
    expect(validateOperatorReason(reason, required)).toBe(expected);
  });
});

describe("OperatorActionDialog", () => {
  it("starts on the reason for a retry, confirms with an optional reason, and cancels on Escape", () => {
    const onConfirm = vi.fn();
    const onCancel = vi.fn();
    render(retryActionModel(waitingRow(), scopeContext()), { onConfirm, onCancel });

    const dialog = container.querySelector<HTMLElement>("[role=dialog]")!;
    expect(dialog.getAttribute("aria-labelledby")).toBe(dialog.querySelector("h3")!.id);
    expect(dialog.querySelector("h3")!.textContent).toBe('Retry the compensation of "Reserve delivery"?');
    expect(document.activeElement).toBe(reason());
    expect(confirmButton().disabled).toBe(false);
    expect(dialog.textContent).toContain("Visible to everyone who can view this run. Do not include secrets.");

    flushSync(() => confirmButton().click());
    expect(onConfirm).toHaveBeenCalledWith({ reason: "", decision: null });

    flushSync(() => window.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape" })));
    expect(onCancel).toHaveBeenCalledTimes(1);
  });

  it("keeps the label's name to its visible text and links the hint and error by description", () => {
    render(abandonActionModel(scopeContext()));

    const field = reason();
    const label = container.querySelector<HTMLLabelElement>(`label[for="${field.id}"]`)!;
    expect(label.textContent).toBe("Reason (required)");
    expect(field.getAttribute("aria-describedby")!.split(" ")).toHaveLength(1);

    type(field, "line\nbreak");
    expect(field.getAttribute("aria-invalid")).toBe("true");
    const [, errorId] = field.getAttribute("aria-describedby")!.split(" ");
    expect(document.getElementById(errorId)!.textContent).toBe("Remove line breaks and other control characters.");
    expect(confirmButton().disabled).toBe(true);
  });

  it("asks for an outcome and a reason before a resolve can be confirmed, starting on the first outcome", () => {
    const onConfirm = vi.fn();
    render(resolveActionModel(waitingRow(), scopeContext()), { onConfirm });

    const radios = [...container.querySelectorAll<HTMLInputElement>("input[type=radio]")];
    expect(container.querySelector("fieldset legend")!.textContent).toBe("Outcome");
    expect(document.activeElement).toBe(radios[0]);
    expect(confirmButton().disabled).toBe(true);

    type(reason(), "Refunded manually, ticket FIN-221");
    expect(confirmButton().disabled).toBe(true);

    flushSync(() => radios[0].click());
    expect(confirmButton().disabled).toBe(false);
    flushSync(() => confirmButton().click());
    expect(onConfirm).toHaveBeenCalledWith({ reason: "Refunded manually, ticket FIN-221", decision: "compensation.resolve.effect-absent" });
  });

  it("shows the abandon's not-undone list and a danger confirm, and locks while submitting", () => {
    render(abandonActionModel(scopeContext()), { busy: true, error: "The runtime is busy. Try again in 7 seconds. Nothing was changed." });

    expect([...container.querySelectorAll("li")].map(item => item.textContent)).toEqual(["Reserve inventory (waiting to undo)", "Reserve delivery (undo failed)"]);
    expect(confirmButton().dataset.tone).toBe("danger");
    expect(confirmButton().textContent).toBe("Submitting…");
    expect(confirmButton().disabled).toBe(true);
    expect(reason().disabled).toBe(true);
    expect(container.querySelector("[role=alert]")!.textContent).toContain("Try again in 7 seconds");
  });
});

describe("operator actions in the scope view", () => {
  it("offers Retry and Resolve on the waiting handler row and Abandon on the banner", () => {
    const onAction = vi.fn();
    mountDesigner(waitingRuntime(), { onAction });

    expect(button("Retry the compensation of Reserve delivery")).not.toBeNull();
    expect(button("Resolve Reserve delivery")).not.toBeNull();
    expect(button("Abandon recovery of Order")).not.toBeNull();
    expect(button("Retry the compensation of Reserve inventory")).toBeNull();

    flushSync(() => button("Resolve Reserve delivery")!.click());
    expect(onAction).toHaveBeenCalledWith(expect.objectContaining({ kind: "resolve", incidentId: "inc-5" }), expect.any(Function));
    // The fallback focuses the row's state chip when the button is gone.
    (onAction.mock.calls[0][1] as () => void)();
    expect(document.activeElement?.getAttribute("aria-label")).toMatch(/^Recovery: Needs action/);
  });

  it("puts the actions of an uncertain step without a handler next to its forward state", () => {
    const runtime = waitingRuntime();
    runtime.rows = [runtime.rows[0], { ...stepRow(), step: "Reserve delivery" }, runtime.rows[2]];
    mountDesigner(runtime);

    const row = container.querySelector('[data-step-index="1"]')!;
    expect(row.querySelector(".wf-compensation-step")!.querySelector('[aria-label="Retry Reserve delivery"]')).not.toBeNull();
    expect(row.querySelector(".wf-compensation-handler")!.querySelector(".wf-compensation-operator-action")).toBeNull();
  });

  it.each([
    ["read-only", (runtime: CompensationScopeRuntime) => runtime, { canAct: false }],
    ["cancelled", (runtime: CompensationScopeRuntime) => ({ ...runtime, phase: "Cancelled" as const }), {}],
    ["following another action", (runtime: CompensationScopeRuntime) => runtime, { pending: { incidentId: "inc-9", label: "Retry submitted" } }]
  ])("hides every action when %s", (_, change, controls) => {
    mountDesigner(change(waitingRuntime()), controls);

    expect(container.querySelectorAll(".wf-compensation-operator-action")).toHaveLength(0);
  });

  it("hides every action when the view has no operator controls (no links advertised)", () => {
    mountDesigner(waitingRuntime(), undefined, false);
    expect(container.querySelectorAll(".wf-compensation-operator-action")).toHaveLength(0);
  });

  it("offers only Retry for a wait that offers no decisions", () => {
    const runtime = waitingRuntime();
    runtime.rows[1] = { ...runtime.rows[1], actions: { ...runtime.rows[1].actions!, decisions: [] } };
    mountDesigner(runtime);

    expect(button("Retry the compensation of Reserve delivery")).not.toBeNull();
    expect(button("Resolve Reserve delivery")).toBeNull();
    expect(button("Abandon recovery of Order")).toBeNull();
  });

  it("shows a submitted action on its row as text and icon", () => {
    mountDesigner(waitingRuntime(), { pending: { incidentId: "inc-5", label: "Applying…" } });

    const chip = container.querySelector('[data-state="Submitted"]')!;
    expect(chip.getAttribute("aria-label")).toBe("Recovery: Applying…");
    expect(chip.querySelector("svg")).not.toBeNull();
  });

  it("shows the source, the new states, the abandoned phase and the operator history", () => {
    const runtime: CompensationScopeRuntime = {
      ...waitingRuntime(),
      phase: "Abandoned",
      source: "journal",
      rows: [
        { step: "Reserve inventory", forward: { state: "Completed", activityExecutionId: "ae-1" }, recovery: { state: "NotUndone" }, handlerNodeId: "release" },
        { step: "Reserve delivery", forward: { state: "Completed", activityExecutionId: "ae-2" }, recovery: { state: "ResolvedAbsent", activityExecutionId: "ae-11" }, handlerNodeId: "cancel" },
        { step: "Confirm order", forward: { state: "Faulted", activityExecutionId: "ae-3" }, recovery: { state: "NotApplicable" } }
      ],
      history: [
        { incidentId: "inc-7", kind: "Abandoned", decision: "compensation.abandon", actor: "carol", appliedAt: "2026-10-20T11:30:00Z", reason: "Written off" },
        { incidentId: "inc-5", kind: "Retry", actor: "alice", appliedAt: "2026-10-20T10:42:00Z", step: "Reserve delivery" }
      ]
    };
    const onOpenIncident = vi.fn();
    mountDesigner(runtime, {}, true, { onOpenIncident });

    expect(container.querySelector(".wf-compensation-source")!.textContent).toBe("From the scope's journal.");
    expect(container.querySelector(".wf-compensation-phase")!.textContent).toContain("Abandoned");
    expect(container.querySelector('[aria-label="Recovery: Not undone (recovery abandoned)"]')).not.toBeNull();
    expect(container.querySelector('[aria-label^="Recovery: Resolved: effect absent"]')).not.toBeNull();
    const items = [...container.querySelectorAll(".wf-compensation-history li")];
    expect(items.map(item => item.querySelector(".wf-compensation-history-kind")!.textContent!.trim())).toEqual(["Abandoned", "Retry (Reserve delivery)"]);
    expect(items[1].textContent).toContain("No reason given");
    flushSync(() => button("Show the incident for Retry on Reserve delivery")!.click());
    expect(onOpenIncident).toHaveBeenCalledWith("inc-5");
  });

  it("says a completed scope's rows are derived because its journal is no longer kept, and names a withheld cause message", () => {
    mountDesigner({
      ...waitingRuntime(),
      phase: "Compensated",
      source: "derived",
      journalUnavailable: "StateCleared",
      cause: { kind: "fault", step: "Confirm order", activityExecutionId: "ae-3", failureType: "order.rejected", messageWithheld: true }
    });

    expect(container.querySelector(".wf-compensation-source")!.textContent).toBe("Derived from run data: a scope keeps its journal only until it completes.");
    expect(container.querySelector(".wf-compensation-cause")!.textContent).toContain("(message withheld)");
  });
});

function render(model: OperatorActionModel, props: Partial<Parameters<typeof OperatorActionDialog>[0]> = {}) {
  flushSync(() => root.render(<OperatorActionDialog model={model} onConfirm={vi.fn()} onCancel={vi.fn()} {...props} />));
}

function reason() {
  return container.querySelector<HTMLTextAreaElement>("textarea")!;
}

function confirmButton() {
  return container.querySelector<HTMLButtonElement>("button[type=submit]")!;
}

function type(field: HTMLTextAreaElement, value: string) {
  const setter = Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, "value")!.set!;
  flushSync(() => {
    setter.call(field, value);
    field.dispatchEvent(new Event("input", { bubbles: true }));
  });
}

function button(label: string) {
  return container.querySelector<HTMLButtonElement>(`button[aria-label="${label}"]`);
}

function waitingRow(): CompensationRowRuntime {
  return {
    step: "Reserve delivery",
    forward: { state: "Uncertain", activityExecutionId: "ae-2" },
    handlerNodeId: "cancel",
    entryId: "e2",
    operationKey: "ae-scope/step/1",
    recovery: { state: "NeedsAction", activityExecutionId: "ae-11", order: 1, attempts: { count: 3, max: 3 }, incidentId: "inc-5" },
    actions: { incidentId: "inc-5", sourceActivityExecutionId: "ae-11", decisions, target: "handler" }
  };
}

function stepRow(): CompensationRowRuntime {
  return {
    step: "Notify carrier",
    forward: { state: "Uncertain", activityExecutionId: "ae-8" },
    entryId: "e3",
    operationKey: "ae-scope/step/2",
    recovery: { state: "NeedsAction", incidentId: "inc-2" },
    actions: { incidentId: "inc-2", sourceActivityExecutionId: "ae-8", decisions, target: "step" }
  };
}

function waitingRuntime(): CompensationScopeRuntime {
  return {
    scopeActivityExecutionId: "ae-scope",
    phase: "AwaitingIntervention",
    scopeLabel: "Order",
    source: "journal",
    rows: [
      { step: "Reserve inventory", forward: { state: "Completed", activityExecutionId: "ae-1" }, recovery: { state: "Pending" }, handlerNodeId: "release" },
      waitingRow(),
      { step: "Confirm order", forward: { state: "Faulted", activityExecutionId: "ae-3" }, recovery: { state: "NotApplicable" } }
    ],
    remaining: [{ step: "Reserve delivery", handlerNodeId: "cancel" }, { step: "Reserve inventory", handlerNodeId: "release" }]
  };
}

function scopeContext(overrides: Partial<CompensationScopeRuntime> = {}): OperatorActionScopeContext {
  return { workflowExecutionId: "wf-42", runtime: { ...waitingRuntime(), ...overrides }, labelOf: nodeId => (nodeId ? labels[nodeId] : undefined) };
}

function mountDesigner(
  runtime: CompensationScopeRuntime,
  controls: Partial<CompensationOperatorControls> = {},
  withOperator = true,
  extra: { onOpenIncident?: (incidentId: string) => void } = {}
) {
  const operator: CompensationOperatorControls = { canAct: true, workflowExecutionId: "wf-42", pending: null, onAction: vi.fn(), ...controls };
  flushSync(() => root.render(
    <CompensationScopeDesigner
      owner={scope([
        { name: "Reserve inventory", activity: leaf("reserve"), compensation: leaf("release") },
        { name: "Reserve delivery", activity: leaf("deliver"), compensation: leaf("cancel") },
        { name: "Confirm order", activity: leaf("confirm"), compensation: null }
      ])}
      nodes={Object.entries(labels).map(([id, label]) => node(id, label))}
      selectedNodeId={null}
      readOnly
      onSelect={vi.fn()}
      runtime={runtime}
      onSelectExecution={vi.fn()}
      onOpenIncident={extra.onOpenIncident ?? vi.fn()}
      {...(withOperator ? { operator } : {})}
    />));
  return operator;
}

function leaf(nodeId: string): ActivityNode {
  return { nodeId, activityVersionId: `${nodeId}-v1`, inputs: [], outputs: [] };
}

function scope(steps: CompensationStep[]): ActivityNode {
  return {
    nodeId: "scope",
    activityVersionId: "scope-v1",
    inputs: [],
    outputs: [],
    structure: { kind: compensationScopeStructureKind, schemaVersion: "1.0.0", payload: { steps } }
  };
}

function node(id: string, label: string): Node<WorkflowNodeData> {
  return {
    id,
    type: "workflowActivity",
    position: { x: 0, y: 0 },
    data: { label, activityVersionId: `${id}-v1`, activityTypeKey: `Elsa.${label.replaceAll(" ", "")}`, childSlots: [], acceptsInbound: true, sourcePorts: [] }
  };
}
