import { describe, expect, it, vi } from "vitest";
import { createStudioRegistry } from "../app/registry";
import { createEndpointContext, type StudioBackendSession, type StudioWorkflowDesignerCanvasContribution } from "../sdk";

describe("optional canvas contribution registry", () => {
  it("keeps discovery inert, ordered and governed by backend and host policy", () => {
    const api = createStudioRegistry({ hostVersion: "1", sdkVersion: "1", ...createEndpointContext("https://studio.example") }, {
      backendSession: { capabilities: { capabilities: [] } } as unknown as StudioBackendSession
    });
    const load = vi.fn();
    const editor = (id: string, order?: number): StudioWorkflowDesignerCanvasContribution => ({ id, title: id, order,
      protocol: { id: "example.document", version: 2 }, supportedResourceKinds: ["workflow-definition"],
      supportedStructures: [{ kind: "example.structure", schemaVersions: ["2.0.0"] }], load });
    const registry = api.workflowDesigner.canvasEditors!;
    expect(registry.slot).toMatchObject({ id: "workflow.designer.canvas-editors", owner: { kind: "module", moduleId: "Elsa.Studio.Workflows" } });
    registry.add(editor("z", 10)); registry.add(editor("a", 10)); registry.add(editor("first", 1));
    registry.add({ ...editor("blocked", 0), requiresBackend: [{ id: "missing", contractVersion: "1" }] });
    expect(registry.list().map(item => item.id)).toEqual(["first", "a", "z"]);
    expect(registry.list({ hostPolicy: ({ contribution }) => contribution.id !== "a" }).map(item => item.id)).toEqual(["first", "z"]);
    expect(registry.compose({ includeUnavailable: true }).find(item => item.contribution.id === "blocked")?.availability)
      .toMatchObject({ state: "unavailable", source: "runtime" });
    expect(load).not.toHaveBeenCalled();
  });
});
