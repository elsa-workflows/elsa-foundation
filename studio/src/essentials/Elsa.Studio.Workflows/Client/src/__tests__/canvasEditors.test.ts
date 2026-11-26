import { describe, expect, it, vi } from "vitest";
import type { ElsaStudioModuleApi, StudioWorkflowDesignerCanvasContribution } from "@elsa-workflows/studio-sdk";
import { matchingCanvasEditors } from "../graph-authoring/canvasEditors";

describe("canvas editor compatibility", () => {
  const protocol = { id: "example.complete-document", version: 2 };
  const structure = { kind: "example.structure", schemaVersion: "2.0.0" };
  const editor: StudioWorkflowDesignerCanvasContribution = { id: "example", title: "Example", protocol,
    supportedResourceKinds: ["workflow-definition", "activity-definition-graph"],
    supportedStructures: [{ kind: structure.kind, schemaVersions: [structure.schemaVersion] }], load: vi.fn() };

  it("requires exact protocol, resource and authored schema while preserving governed order", () => {
    const editors = [{ ...editor, id: "first" }, editor];
    expect(matchingCanvasEditors(editors, "activity-definition-graph", structure, protocol)).toEqual(editors);
    expect(matchingCanvasEditors(editors, "workflow-definition", structure, { ...protocol, id: "another" })).toEqual([]);
    expect(matchingCanvasEditors(editors, "workflow-definition", structure, { ...protocol, version: 1 })).toEqual([]);
    expect(matchingCanvasEditors(editors, "workflow-definition", { ...structure, kind: "another" }, protocol)).toEqual([]);
    expect(matchingCanvasEditors(editors, "workflow-definition", { ...structure, schemaVersion: "2.0.1" }, protocol)).toEqual([]);
    expect(matchingCanvasEditors([{ ...editor, supportedResourceKinds: ["workflow-definition"] }], "activity-definition-graph", structure, protocol)).toEqual([]);
    expect(matchingCanvasEditors(editors, "workflow-definition", null, protocol)).toEqual([]);
    expect(editor.load).not.toHaveBeenCalled();
  });

  it("allows an older host without a registry to remain on its existing editor", () => {
    const legacy: ElsaStudioModuleApi["workflowDesigner"] = { nodeRenderers: {} as never, toolboxItems: {} as never, panels: {} as never };
    expect(matchingCanvasEditors(legacy.canvasEditors?.list() ?? [], "workflow-definition", structure, protocol)).toEqual([]);
  });
});
