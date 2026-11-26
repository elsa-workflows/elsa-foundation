import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { describe, expect, it, vi } from "vitest";
import { BpmnElementConfiguration, BpmnElementInspector } from "../bpmn/BpmnElementInspector";
import type { BpmnElement, BpmnSequenceFlow } from "../bpmn/bpmnTypes";

// A retained defaultFlowId must disable its condition even when an imported flow omits isDefault.
describe("BPMN routing inspector", () => {
  it.each([false, true])("renders pure-gateway controls with readOnly=%s", readOnly => {
    const container = document.createElement("div");
    const root = createRoot(container);
    const add = vi.fn();
    flushSync(() => root.render(<BpmnElementInspector
      element={{ elementId: "gateway", elementType: "exclusiveGateway", defaultFlowId: "fallback" }}
      outboundFlows={[
        { flowId: "high", sourceRef: "gateway", targetRef: "high-end", conditionOutcome: "High" },
        { flowId: "fallback", sourceRef: "gateway", targetRef: "fallback-end" }
      ]}
      onChangeElement={vi.fn()} onChangeFlow={vi.fn()} onSetDefaultFlow={vi.fn()}
      targetLabelFor={flow => flow.targetRef} onAddDecision={add} readOnly={readOnly}
    />));
    try {
      expect(container.querySelector(".wf-inspector-context h3")?.textContent).toBe("Exclusive gateway");
      expect(container.querySelector(".wf-inspector-tab-panel .wf-form-field")).not.toBeNull();
      const condition = container.querySelector<HTMLInputElement>("[aria-label='Match outcome for flow to high-end']")!;
      expect(condition.disabled).toBe(readOnly);
      expect(container.querySelector<HTMLInputElement>("[aria-label='Match outcome for flow to fallback-end']")?.disabled).toBe(true);
      expect(container.querySelector<HTMLInputElement>("[aria-label='Default flow to fallback-end']")?.checked).toBe(true);
      expect(container.querySelector<HTMLSelectElement>("select")?.disabled).toBe(readOnly);
      expect(container.querySelector<HTMLButtonElement>("button")?.disabled).toBe(readOnly);
      if (!readOnly) {
        flushSync(() => container.querySelector("button")!.click());
        expect(add).toHaveBeenCalledOnce();
      }
    } finally {
      flushSync(() => root.unmount());
    }
  });

  it("keeps the Outgoing paths section and connection guidance visible when there are no paths", () => {
    const rendered = renderConfiguration({ elementId: "task", elementType: "task" }, []);
    try {
      const heading = rendered.container.querySelector("h4");
      const section = rendered.container.querySelector("section.wf-bpmn-flow-list");
      expect(heading?.textContent).toBe("Outgoing paths");
      expect(section?.getAttribute("aria-labelledby")).toBe(heading?.id);
      expect(rendered.container.textContent).toContain("Connect outgoing paths on the canvas");
    } finally {
      rendered.unmount();
    }
  });

  it("keeps standalone names in routing and can omit the name field for a bound element", () => {
    const standalone = renderConfiguration({ elementId: "task", elementType: "task", name: "Standalone task" }, []);
    const bound = renderConfiguration({ elementId: "bound-task", elementType: "task", childNodeId: "activity", name: "Bound task" }, [], { showElementName: false });
    try {
      expect(standalone.container.querySelector("input[aria-label='Name']")).not.toBeNull();
      expect(bound.container.querySelector("input[aria-label='Name']")).toBeNull();
    } finally {
      standalone.unmount();
      bound.unmount();
    }
  });

  it("summarizes a single unconditional task path and keeps its controls mounted", () => {
    const rendered = renderConfiguration({ elementId: "task", elementType: "task" }, [flow()]);
    try {
      const disclosure = rendered.container.querySelector<HTMLButtonElement>(".wf-bpmn-flow-summary")!;
      const outcome = rendered.container.querySelector<HTMLInputElement>("[aria-label='Match outcome for flow to End event']")!;
      expect(disclosure.textContent).toContain("End event");
      expect(disclosure.textContent).toContain("Any outcome");
      expect(disclosure.getAttribute("aria-expanded")).toBe("false");
      expect(outcome).not.toBeNull();
      expect(outcome.closest("[hidden]")).not.toBeNull();

      flushSync(() => disclosure.click());

      expect(disclosure.getAttribute("aria-expanded")).toBe("true");
      expect(outcome.closest("[hidden]")).toBeNull();
    } finally {
      rendered.unmount();
    }
  });

  it("keeps compact paths inspectable but not editable in read-only mode", () => {
    const rendered = renderConfiguration({ elementId: "task", elementType: "task" }, [flow()], { readOnly: true });
    try {
      const disclosure = rendered.container.querySelector<HTMLButtonElement>(".wf-bpmn-flow-summary")!;
      expect(disclosure.disabled).toBe(false);
      flushSync(() => disclosure.click());

      expect(rendered.container.querySelector<HTMLInputElement>("[aria-label='Match outcome for flow to End event']")?.disabled).toBe(true);
      expect(rendered.container.querySelector<HTMLInputElement>("[aria-label='Default flow to End event']")?.disabled).toBe(true);
    } finally {
      rendered.unmount();
    }
  });

  it.each(["exclusiveGateway", "parallelGateway", "inclusiveGateway"])("keeps %s routes visible", elementType => {
    const rendered = renderConfiguration({ elementId: "gateway", elementType }, [flow()]);
    try {
      expect(rendered.container.querySelector(".wf-bpmn-flow-summary")).toBeNull();
      expect(rendered.container.querySelector<HTMLInputElement>("[aria-label='Match outcome for flow to End event']")?.closest("[hidden]")).toBeNull();
    } finally {
      rendered.unmount();
    }
  });

  it.each([
    ["a Decision", { isDecision: true }, [flow()]],
    ["a conditional path", {}, [flow({ conditionOutcome: "Approved" })]],
    ["a flow-level default", {}, [flow({ isDefault: true })]],
    ["multiple paths", {}, [flow(), flow({ flowId: "other", targetRef: "other-end" })]]
  ])("keeps %s routes visible", (_description, options, flows) => {
    const rendered = renderConfiguration({ elementId: "task", elementType: "task" }, flows, options);
    try {
      expect(rendered.container.querySelector(".wf-bpmn-flow-summary")).toBeNull();
      expect(rendered.container.querySelector<HTMLInputElement>(".wf-bpmn-flow-details input")?.closest("[hidden]")).toBeNull();
    } finally {
      rendered.unmount();
    }
  });

  it("keeps an edited outcome mounted and focused as it becomes blank and compactable", () => {
    const rendered = renderConfiguration({ elementId: "task", elementType: "task" }, [flow()]);
    try {
      flushSync(() => rendered.container.querySelector<HTMLButtonElement>(".wf-bpmn-flow-summary")!.click());
      const outcome = rendered.container.querySelector<HTMLInputElement>("[aria-label='Match outcome for flow to End event']")!;
      flushSync(() => outcome.focus());

      changeInput(outcome, "Approved");
      expect(rendered.onChangeFlow).toHaveBeenLastCalledWith("flow", { conditionOutcome: "Approved" });
      rendered.render({ elementId: "task", elementType: "task" }, [flow({ conditionOutcome: "Approved" })]);
      expect(rendered.container.querySelector("[aria-label='Match outcome for flow to End event']")).toBe(outcome);
      expect(document.activeElement).toBe(outcome);

      changeInput(outcome, "");
      expect(rendered.onChangeFlow).toHaveBeenLastCalledWith("flow", { conditionOutcome: null });
      rendered.render({ elementId: "task", elementType: "task" }, [flow()]);
      expect(rendered.container.querySelector("[aria-label='Match outcome for flow to End event']")).toBe(outcome);
      expect(outcome.closest("[hidden]")).toBeNull();
      expect(document.activeElement).toBe(outcome);
    } finally {
      rendered.unmount();
    }
  });

  it("resets disclosure state when selection changes to another element", () => {
    const rendered = renderConfiguration({ elementId: "task", elementType: "task" }, [flow()]);
    try {
      flushSync(() => rendered.container.querySelector<HTMLButtonElement>(".wf-bpmn-flow-summary")!.click());
      rendered.render({ elementId: "other-task", elementType: "task" }, [flow()]);
      expect(rendered.container.querySelector<HTMLButtonElement>(".wf-bpmn-flow-summary")?.getAttribute("aria-expanded")).toBe("false");
      expect(rendered.container.querySelector<HTMLInputElement>(".wf-bpmn-flow-details input")?.closest("[hidden]")).not.toBeNull();
    } finally {
      rendered.unmount();
    }
  });
});

function renderConfiguration(element: BpmnElement, outboundFlows: BpmnSequenceFlow[], options: { isDecision?: boolean; readOnly?: boolean; showElementName?: boolean } = {}) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  const onChangeFlow = vi.fn();
  const render = (nextElement: BpmnElement, nextFlows: BpmnSequenceFlow[]) => flushSync(() => root.render(
    <BpmnElementConfiguration
      element={nextElement}
      outboundFlows={nextFlows}
      onChangeElement={vi.fn()}
      onChangeFlow={onChangeFlow}
      onSetDefaultFlow={vi.fn()}
      targetLabelFor={sequenceFlow => sequenceFlow.targetRef === "end" ? "End event" : sequenceFlow.targetRef}
      {...options}
    />
  ));
  render(element, outboundFlows);
  return {
    container,
    onChangeFlow,
    render,
    unmount: () => {
      flushSync(() => root.unmount());
      container.remove();
    }
  };
}

function flow(patch: Partial<BpmnSequenceFlow> = {}): BpmnSequenceFlow {
  return { flowId: "flow", sourceRef: "task", targetRef: "end", ...patch };
}

function changeInput(input: HTMLInputElement, value: string) {
  flushSync(() => {
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")?.set?.call(input, value);
    input.dispatchEvent(new Event("input", { bubbles: true }));
    input.dispatchEvent(new Event("change", { bubbles: true }));
  });
}
