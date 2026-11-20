import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, expect, it } from "vitest";
import type { StudioActivityDescriptor, StudioExpressionDescriptor } from "@elsa-workflows/studio-sdk";
import { ActivityPropertiesPanel } from "../ActivityPropertiesPanel";
import type { ActivityNode } from "../workflowTypes";

// Its own file on purpose: every module starts unloaded here, as it does when an author reopens a workflow in a
// fresh page. A row that loaded its bindings on demand rendered nothing for them in that first commit (#4743).

const expressionDescriptors: StudioExpressionDescriptor[] = [
  { type: "Literal", displayName: "Literal", editingMode: "literal" },
  { type: "JavaScript", displayName: "JavaScript", editingMode: "text" }
];

const descriptor: StudioActivityDescriptor = {
  typeName: "WriteLine",
  inputs: [{ name: "Text", displayName: "Text", referenceKey: "text", typeName: "System.String", isWrapped: true }],
  outputs: [],
  ports: []
};

// The stored value after inserting an activity output, a workflow input and a variable into a JavaScript expression.
const reopened: ActivityNode = {
  nodeId: "consumer",
  activityVersionId: "write-line",
  inputs: [],
  outputs: [],
  text: {
    typeName: "System.String",
    expression: {
      type: "JavaScript",
      value: {
        language: "JavaScript",
        source: 'args.data["customerId"].toUpperCase() + "-" + args.data2 + "-" + args.data3["a.b"][""]',
        resultType: { alias: "String", collectionKind: "Single" },
        parameters: {
          data: { kind: "ActivityResult", producerNodeId: "producer", projectionKey: "Customer" },
          data2: { kind: "WorkflowRequest", memberKey: "suffix", path: null },
          data3: { kind: "Variable", declaringScopeNodeId: "workflow", variableKey: "escaped-payload" }
        },
        options: {},
        capabilityProfile: "binding-pure-v1"
      }
    }
  }
};

let unmount: (() => void) | undefined;
afterEach(() => unmount?.());

it("keeps reopened workflow input bindings collapsed and intact until requested", () => {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  unmount = () => { flushSync(() => root.unmount()); container.remove(); };

  flushSync(() => root.render(
    <ActivityPropertiesPanel
      readOnly={false}
      draftId="draft-1"
      activity={reopened}
      descriptor={descriptor}
      editors={[]}
      expressionEditors={[]}
      workflowState={{ inputs: [{ referenceKey: "suffix-key", name: "suffix", displayName: "Suffix" }] }}
      expressionDescriptors={expressionDescriptors}
      expressionDescriptorStatus="ready"
      descriptorStatus="ready"
      visibleVariables={[]}
      scopeStatus="ready"
      onChange={() => {}}
    />
  ));

  // No waiting: the section must be part of the same commit as the expression it belongs to.
  const bindings = container.querySelector<HTMLElement>(".wf-js-input-bindings");
  expect(bindings, "Workflow input bindings section").not.toBeNull();
  const disclosure = bindings!.querySelector<HTMLButtonElement>("button[aria-expanded]")!;
  expect(disclosure.getAttribute("aria-expanded")).toBe("false");
  expect(bindings!.querySelector("[aria-label='Bound workflow inputs']")).toBeNull();
  flushSync(() => disclosure.click());
  expect(disclosure.getAttribute("aria-expanded")).toBe("true");
  expect(bindings!.querySelector("[aria-label='Bound workflow inputs']")?.textContent).toContain("args.data2");
  expect(bindings!.querySelector("button[aria-label='Remove data2 binding']")).not.toBeNull();
});
