import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { describe, expect, it, vi } from "vitest";
import { BpmnElementInspector } from "../bpmn/BpmnElementInspector";

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
      const condition = container.querySelector<HTMLInputElement>("[aria-label='Condition outcome for flow to high-end']")!;
      expect(condition.disabled).toBe(readOnly);
      expect(container.querySelector<HTMLInputElement>("[aria-label='Condition outcome for flow to fallback-end']")?.disabled).toBe(true);
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
});
