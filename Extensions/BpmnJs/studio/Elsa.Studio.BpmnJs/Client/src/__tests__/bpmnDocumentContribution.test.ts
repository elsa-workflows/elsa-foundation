import { describe, expect, it } from "vitest";
import type { ElsaStudioModuleApi, StudioWorkflowDesignerCanvasContribution } from "@elsa-workflows/studio-sdk";
import { register } from "../module";
import { matchingCanvasEditors } from "../../../../../../../Studio/src/essentials/Elsa.Studio.Workflows/Client/src/graph-authoring/CanvasEditorHost";

describe("optional native document contribution", () => {
  it("registers only the exact document protocol and does not offer source2 through the old synchronous canvas bridge", () => {
    const added: StudioWorkflowDesignerCanvasContribution[] = [];
    register({ workflowDesigner: { canvasEditors: { add: (value: StudioWorkflowDesignerCanvasContribution) => added.push(value) } } } as unknown as ElsaStudioModuleApi);
    expect(added).toHaveLength(1);
    const editor = added[0], protocol = { id: "elsa.workflows.bpmn-document-canvas", version: 1 };
    expect(editor.id).toBe("bpmn-js");
    expect(matchingCanvasEditors(added, "workflow-definition", { kind: "elsa.bpmn.structure", schemaVersion: "2.0.0" }, protocol)).toEqual(added);
    expect(matchingCanvasEditors(added, "activity-definition-graph", { kind: "elsa.bpmn.structure", schemaVersion: "2.0.0" }, protocol)).toEqual(added);
    expect(matchingCanvasEditors(added, "workflow-definition", { kind: "elsa.bpmn.structure", schemaVersion: "1.0.0" }, protocol)).toEqual([]);
    expect(matchingCanvasEditors(added, "workflow-definition", { kind: "elsa.bpmn.structure", schemaVersion: "2.0.0" })).toEqual([]);
    expect(matchingCanvasEditors(added, "workflow-definition", { kind: "elsa.bpmn.structure", schemaVersion: "2.0.0" }, { ...protocol, version: 2 })).toEqual([]);
  });

  it("fails explicitly on a consumer without the optional canvas registry", () => {
    const api = { workflowDesigner: {} } as unknown as ElsaStudioModuleApi;
    expect(() => register(api)).toThrow("canvas editor registry");
  });
});
