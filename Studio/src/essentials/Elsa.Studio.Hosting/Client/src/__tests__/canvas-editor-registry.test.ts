import { describe, expect, it } from "vitest";
import { createStudioRegistry } from "../app/registry";
import { createEndpointContext } from "../sdk";

describe("Workflows-owned canvas registry", () => {
  it("is empty without composition and records the exact owning module identity", () => {
    const api = createStudioRegistry({ hostVersion: "1.0.0", sdkVersion: "1.0.0", ...createEndpointContext("https://studio.example/") });
    expect(api.workflowDesigner.canvasEditors!.list()).toEqual([]);
    expect(api.workflowDesigner.canvasEditors!.slot.owner).toEqual({ kind: "module", id: "Elsa.Studio.Workflows", moduleId: "Elsa.Studio.Workflows" });
  });
});
