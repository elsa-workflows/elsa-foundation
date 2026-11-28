import { describe, expect, it } from "vitest";
import type { StudioActivityDefinitionImplementationState } from "@elsa-workflows/studio-sdk";
import type { ActivityNode } from "../workflowTypes";
import {
  activityGraphDocumentAdapter,
  activityGraphLayoutFromDesign,
  activityGraphLayoutToDesign
} from "../activityGraphDocumentAdapter";

describe("Activity Graph document adapter", () => {
  it("round-trips the root and layout while preserving provider and layout extension fields", () => {
    const document: StudioActivityDefinitionImplementationState = {
      payload: {
        rootActivity: root("root"),
        variables: [{ id: "local" }],
        outputMappings: [],
        outcomeMappings: [],
        providerExtension: { retained: true }
      },
      layout: [
        { nodeId: "child", data: { x: 12, y: 34, width: 220, plugin: { retained: true } } }
      ]
    };

    const nextRoot = root("root", "changed-v2");
    const design = activityGraphDocumentAdapter.readLayout(document);
    const updated = activityGraphDocumentAdapter.replaceGraph(document, nextRoot, [
      { ...design[0], x: 40, y: 60 }
    ]);

    expect(activityGraphDocumentAdapter.readRoot(updated)).toEqual(nextRoot);
    expect(updated.payload).toMatchObject({
      variables: [{ id: "local" }],
      providerExtension: { retained: true }
    });
    expect(updated.layout).toEqual([
      { nodeId: "child", data: { x: 40, y: 60, width: 220, plugin: { retained: true } } }
    ]);
  });

  it("normalizes a missing root without dropping unknown provider fields", () => {
    const document: StudioActivityDefinitionImplementationState = {
      payload: { providerExtension: "keep" },
      layout: []
    };

    expect(activityGraphDocumentAdapter.readRoot(document)).toEqual({
      nodeId: "root",
      activityVersionId: "",
      inputs: [],
      outputs: [],
      structure: null
    });
    expect(activityGraphDocumentAdapter.replaceRoot(document, root("root")).payload).toMatchObject({
      providerExtension: "keep"
    });
  });

  it("expands and canonicalizes nested input and output bindings without changing opaque reference keys", () => {
    const inputKey = "customer/id:opaque";
    const outputKey = "result/total:opaque";
    const childInputKey = "child/message:opaque";
    const childOutputKey = "child/result:opaque";
    const rootInput = {
      referenceKey: inputKey,
      value: { value: { referenceKey: "customer-id", declaringScopeId: "graph" }, expressionType: "Input" },
      conversion: { mode: "Exact", profileId: "input-profile" },
      autoEvaluate: true
    };
    const rootOutput = {
      referenceKey: outputKey,
      value: { value: { referenceKey: "running-total", declaringScopeId: "graph" }, expressionType: "Variable" },
      conversion: { mode: "Exact", profileId: "output-profile" },
      isSensitive: true
    };
    const foreignOutput = {
      referenceKey: "foreign-output",
      value: { value: "kept", expressionType: "Literal" },
      extension: { retained: true }
    };
    const childInput = {
      referenceKey: childInputKey,
      value: { value: "hello", expressionType: "Literal" },
      evaluatorType: "custom-evaluator"
    };
    const childOutput = {
      referenceKey: childOutputKey,
      value: { value: { referenceKey: "message-result" }, expressionType: "Variable" },
      extension: "child capture metadata"
    };
    const document: StudioActivityDefinitionImplementationState = {
      payload: {
        rootActivity: {
          nodeId: "root",
          activityVersionId: "sequence-v1",
          inputs: [rootInput],
          outputs: [rootOutput, foreignOutput],
          structure: {
            kind: "Sequence",
            schemaVersion: "1",
            payload: {
              activities: [{
                nodeId: "child",
                activityVersionId: "write-line-v1",
                inputs: [childInput],
                outputs: [childOutput],
                structure: null
              }]
            }
          }
        },
        variables: [],
        outputMappings: [],
        outcomeMappings: []
      },
      layout: []
    };

    const expanded = activityGraphDocumentAdapter.readRoot(document) as ActivityNode & Record<string, unknown>;
    const expandedChild = (expanded.structure as unknown as { payload: { activities: Array<Record<string, unknown>> } }).payload.activities[0];

    expect(expanded[inputKey]).toMatchObject({
      expression: { type: "Input", value: { referenceKey: "customer-id", declaringScopeId: "graph" } },
      conversion: rootInput.conversion,
      argumentExtras: { autoEvaluate: true }
    });
    expect(expanded[outputKey]).toMatchObject({
      target: { referenceKey: "running-total", declaringScopeId: "graph" },
      conversion: rootOutput.conversion,
      argumentExtras: { isSensitive: true }
    });
    expect(expanded.outputs).toEqual([foreignOutput]);
    expect(expandedChild[childInputKey]).toMatchObject({
      expression: { type: "Literal", value: "hello" },
      argumentExtras: { evaluatorType: "custom-evaluator" }
    });
    expect(expandedChild[childOutputKey]).toMatchObject({
      target: { referenceKey: "message-result" },
      argumentExtras: { extension: "child capture metadata" }
    });

    const editedRoot = {
      ...expanded,
      [inputKey]: {
        ...(expanded[inputKey] as Record<string, unknown>),
        expression: { type: "Literal", value: "edited customer id" }
      },
      structure: {
        ...expanded.structure!,
        payload: {
          ...(expanded.structure!.payload as Record<string, unknown>),
          activities: [{
            ...expandedChild,
            [childInputKey]: {
              ...(expandedChild[childInputKey] as Record<string, unknown>),
              expression: { type: "Literal", value: "edited nested message" }
            }
          }]
        }
      }
    };
    const saved = activityGraphDocumentAdapter.replaceGraph(document, editedRoot, []);
    const savedRoot = (saved.payload as { rootActivity: Record<string, unknown> }).rootActivity;
    const savedChild = (savedRoot.structure as { payload: { activities: Array<Record<string, unknown>> } }).payload.activities[0];

    expect(savedRoot.inputs).toEqual([{
      ...rootInput,
      value: { value: "edited customer id", expressionType: "Literal" }
    }]);
    expect(savedRoot.outputs).toHaveLength(2);
    expect(savedRoot.outputs).toEqual(expect.arrayContaining([rootOutput, foreignOutput]));
    expect(savedChild.inputs).toEqual([{
      ...childInput,
      value: { value: "edited nested message", expressionType: "Literal" }
    }]);
    expect(savedChild.outputs).toEqual([childOutput]);
    expect(savedRoot[inputKey]).toBeUndefined();
    expect(savedRoot[outputKey]).toBeUndefined();
    expect(savedChild[childInputKey]).toBeUndefined();
    expect(savedChild[childOutputKey]).toBeUndefined();
  });

  it("converts layout records without losing dimensions or unknown data", () => {
    const wire = [
      { nodeId: "node-1", data: { x: 10, y: 20, width: 200, height: 80, custom: "keep" } }
    ];
    const design = activityGraphLayoutToDesign(wire);

    expect(design).toEqual([{
      nodeId: "node-1",
      x: 10,
      y: 20,
      width: 200,
      height: 80,
      additionalProperties: { custom: "keep" }
    }]);
    expect(activityGraphLayoutFromDesign(design, wire)).toEqual(wire);
  });
});

function root(nodeId: string, activityVersionId = "sequence-v1") {
  return {
    nodeId,
    activityVersionId,
    inputs: [],
    outputs: [],
    structure: null
  };
}
