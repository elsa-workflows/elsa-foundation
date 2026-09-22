import { describe, expect, it } from "vitest";
import type { StudioActivityDefinitionImplementationState } from "@elsa-workflows/studio-sdk";
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
