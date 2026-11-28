import { describe, expect, it } from "vitest";
import { canonicalizeStateForWire, expandStateFromWire } from "../activityInputWire";
import { readBpmnDocument } from "../bpmn/bpmnDocument";
import type { ActivityNode, WorkflowDefinitionState } from "../workflowTypes";

function source(): WorkflowDefinitionState {
  const header = { scopeElementId: "sub", nodeId: "scope-header", activityVersionId: "bpmn-version", inputs: [], outputs: [], variables: [] };
  const native = { nodeId: "native", activityVersionId: "inline-version", inputs: [], outputs: [], structure: null,
    Expression: { expression: { type: "JavaScript", value: '"native"' } } };
  return { rootActivity: { nodeId: "root", activityVersionId: "bpmn-version", inputs: [], outputs: [], structure: {
    kind: "elsa.bpmn.structure", schemaVersion: "2.0.0", payload: {
      documentXml: '<definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="defs" targetNamespace="urn:wire"><process id="process"><subProcess id="sub"><task id="task"/></subProcess></process></definitions>',
      processId: "process", activities: [native], bindings: [{ elementId: "task", childNodeId: "native" }], scopeHeaders: [header], variables: [],
      vendor: [{ nodeId: "opaque", activityVersionId: "opaque-version", inputs: [], outputs: [], Expression: { expression: { type: "Literal", value: "opaque" } } }]
    }
  } } } as unknown as WorkflowDefinitionState;
}

describe("complete-source native wire ownership", () => {
  for (const [name, transform] of [["canonicalize", canonicalizeStateForWire], ["expand", expandStateFromWire]] as const) {
    it(`${name} keeps compact scope headers and opaque metadata outside the native activity traversal`, () => {
      const before = source(), payload = before.rootActivity!.structure!.payload;
      const after = transform(before), next = after.rootActivity!.structure!.payload;
      expect(next.scopeHeaders).toStrictEqual(payload.scopeHeaders);
      expect(next.vendor).toStrictEqual(payload.vendor);
      expect(Object.hasOwn((next.scopeHeaders as Record<string, unknown>[])[0], "structure")).toBe(false);
      expect(() => readBpmnDocument(after.rootActivity!)).not.toThrow();
    });
  }

  it("still folds native forest arguments and round-trips actual source and header bytes", () => {
    const before = source(), original = before.rootActivity!.structure!.payload;
    const wire = canonicalizeStateForWire(before), payload = wire.rootActivity!.structure!.payload;
    expect((payload.activities as ActivityNode[])[0].inputs).toEqual([{ referenceKey: "Expression", value: { expressionType: "JavaScript", value: '"native"' } }]);
    const roundTrip = canonicalizeStateForWire(expandStateFromWire(wire));
    expect(roundTrip).toEqual(wire);
    expect(roundTrip.rootActivity!.structure!.payload.documentXml).toBe(original.documentXml);
    expect(roundTrip.rootActivity!.structure!.payload.scopeHeaders).toEqual(original.scopeHeaders);
  });
});
