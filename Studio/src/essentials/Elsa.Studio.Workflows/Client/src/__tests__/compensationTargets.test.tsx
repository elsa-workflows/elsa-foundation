import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import type { StudioActivityInputDescriptor } from "@elsa-workflows/studio-sdk";
import { compensationScopeStructureKind } from "../compensationScope";
import { compensationTargetProblem, compensateTargetUiHint, confirmTargetUiHint, enclosingCompensationScopes } from "../compensationTargets";
import { CompensationTargetEditor, compensationTargetPropertyEditor } from "../CompensationTargetEditor";
import { WorkflowReferenceAuthoringProvider } from "../workflowReferenceAuthoring";
import type { ActivityNode } from "../workflowTypes";

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

describe("Confirm and Compensate targets (spec 258 T321)", () => {
  it("lists the enclosing scopes nearest first, through other containers, and knows a handler from a step", () => {
    const graph = workflow();

    expect(enclosingCompensationScopes(graph, "confirm-inner")).toEqual([
      { nodeId: "inner", label: "Book hotel", inHandler: false },
      { nodeId: "outer", label: "Travel booking", inHandler: false }
    ]);
    expect(enclosingCompensationScopes(graph, "confirm-in-handler")).toEqual([
      { nodeId: "outer", label: "Travel booking", inHandler: true }
    ]);
    expect(enclosingCompensationScopes(graph, "loose")).toEqual([]);
    expect(enclosingCompensationScopes(graph, "missing")).toBeNull();
  });

  it("explains a target the validator refuses, with its message", () => {
    const graph = workflow();
    const inner = enclosingCompensationScopes(graph, "confirm-inner")!;

    expect(compensationTargetProblem("Confirm", "confirm-inner", inner, "")).toBeNull();
    expect(compensationTargetProblem("Confirm", "confirm-inner", inner, "outer")).toBeNull();
    expect(compensationTargetProblem("Compensate", "confirm-inner", inner, "elsewhere"))
      .toBe("Compensate 'confirm-inner' names scope 'elsewhere', which does not enclose it.");
    expect(compensationTargetProblem("Confirm", "loose", [], ""))
      .toBe("Confirm 'loose' needs an enclosing compensation scope.");
    expect(compensationTargetProblem("Confirm", "confirm-in-handler", enclosingCompensationScopes(graph, "confirm-in-handler")!, ""))
      .toBe("Confirm 'confirm-in-handler' sits in a compensation handler of scope 'outer', which ignores it during recovery.");
  });

  it("offers only the nearest scope and the enclosing scopes, and writes the chosen node id", () => {
    const onChange = vi.fn();
    render("confirm-inner", "", onChange);

    const picker = container.querySelector("select")!;
    expect(picker.getAttribute("aria-label")).toBe("Scope");
    expect([...picker.options].map(option => [option.value, option.textContent])).toEqual([
      ["", "Nearest enclosing scope (Book hotel)"],
      ["inner", "Book hotel (inner)"],
      ["outer", "Travel booking (outer)"]
    ]);
    flushSync(() => {
      picker.value = "outer";
      picker.dispatchEvent(new Event("change", { bubbles: true }));
    });
    expect(onChange).toHaveBeenCalledWith("outer");
    flushSync(() => {
      picker.value = "";
      picker.dispatchEvent(new Event("change", { bubbles: true }));
    });
    expect(onChange).toHaveBeenLastCalledWith(null);
  });

  it("keeps an invalid stored target visible and describes why it is refused", () => {
    render("confirm-inner", "elsewhere", vi.fn(), compensateTargetUiHint);

    const picker = container.querySelector("select")!;
    expect(picker.value).toBe("elsewhere");
    expect(picker.getAttribute("aria-invalid")).toBe("true");
    expect(document.getElementById(picker.getAttribute("aria-describedby")!)?.textContent)
      .toContain("Compensate 'confirm-inner' names scope 'elsewhere', which does not enclose it.");
  });

  it("claims only the Confirm and Compensate target inputs", () => {
    const context = { activity: {}, expressionDescriptors: [] };
    expect(compensationTargetPropertyEditor.supports(descriptor(confirmTargetUiHint), context)).toBe(true);
    expect(compensationTargetPropertyEditor.supports(descriptor(compensateTargetUiHint), context)).toBe(true);
    expect(compensationTargetPropertyEditor.supports(descriptor("singleline"), context)).toBe(false);
    expect(compensationTargetPropertyEditor.supports(descriptor(confirmTargetUiHint), { ...context, scope: "collection" })).toBe(false);
  });
});

function render(activityId: string, value: string, onChange: (value: unknown) => void, uiHint = confirmTargetUiHint) {
  flushSync(() => root.render(
    <WorkflowReferenceAuthoringProvider workflowState={{ rootActivity: workflow() }} workflowInputs={[]} visibleVariables={[]} status="ready">
      <CompensationTargetEditor
        descriptor={descriptor(uiHint)}
        value={value}
        context={{ activity: {}, activityId, expressionDescriptors: [] }}
        onChange={onChange}
      />
    </WorkflowReferenceAuthoringProvider>
  ));
}

function descriptor(uiHint: string): StudioActivityInputDescriptor {
  return { name: "Scope", displayName: "Scope", typeName: "String", uiHint } as StudioActivityInputDescriptor;
}

function leaf(nodeId: string): ActivityNode {
  return { nodeId, activityVersionId: `${nodeId}-v1`, inputs: [], outputs: [] };
}

function scope(nodeId: string, displayName: string, steps: { name: string; activity: ActivityNode | null; compensation: ActivityNode | null }[]): ActivityNode {
  return {
    nodeId,
    displayName,
    activityVersionId: `${nodeId}-v1`,
    inputs: [],
    outputs: [],
    structure: { kind: compensationScopeStructureKind, schemaVersion: "1.0.0", payload: { steps } }
  };
}

// A flowchart holding an outer scope (whose first step is a sequence around an inner scope) and a loose Confirm.
function workflow(): ActivityNode {
  const sequence: ActivityNode = {
    ...leaf("sequence"),
    structure: { kind: "Elsa.Sequence", schemaVersion: "1.0.0", payload: { activities: [
      scope("inner", "Book hotel", [
        { name: "Reserve hotel", activity: leaf("hotel"), compensation: leaf("cancel-hotel") },
        { name: "Confirm", activity: leaf("confirm-inner"), compensation: null }
      ])
    ] } }
  };
  return {
    ...leaf("root"),
    structure: { kind: "Elsa.Flowchart", schemaVersion: "1.0.0", payload: { activities: [
      scope("outer", "Travel booking", [
        { name: "Book travel", activity: sequence, compensation: leaf("confirm-in-handler") }
      ]),
      leaf("loose")
    ] } }
  };
}
