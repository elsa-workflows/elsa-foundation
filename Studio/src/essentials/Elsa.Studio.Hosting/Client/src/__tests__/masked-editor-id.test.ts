// @vitest-environment node

import { describe, expect, it } from "vitest";
import { registerBuiltInPropertyEditors } from "../app/propertyEditors";
import { createStudioRegistry } from "../app/registry";
import { createEndpointContext, type StudioActivityInputDescriptor } from "../sdk";
import { read } from "./themeTestUtils";

// The Workflows properties panel names the masked editor by id across the module boundary and gives a masked
// literal to no other editor, so a rename on either side would hide every masked literal there.
describe("masked property editor id", () => {
  it("is the id of the editor the registry resolves for a password-hinted text input", () => {
    const descriptor: StudioActivityInputDescriptor = { name: "ApiToken", typeName: "System.String", uiHint: "password" };
    const panel = read("../../../../../essentials/Elsa.Studio.Workflows/Client/src/ActivityPropertiesPanel.tsx");
    const panelEditorId = /const maskedPropertyEditorId = "([^"]+)";/.exec(panel)?.[1];
    const api = createStudioRegistry({ hostVersion: "1.0.0", sdkVersion: "1.0.0", ...createEndpointContext("https://studio.example/") });
    registerBuiltInPropertyEditors(api);
    // The registry lists contributions in resolution order; the first that supports the input is the one used.
    const resolvedEditorId = api.propertyEditors.list()
      .find(editor => editor.supports(descriptor, { activity: {}, expressionDescriptors: [], scope: "element" }))?.id;

    expect(panelEditorId, "maskedPropertyEditorId in ActivityPropertiesPanel.tsx").toBeDefined();
    expect(resolvedEditorId, "the registry's editor for a password-hinted text input").toBeDefined();
    expect(panelEditorId).toBe(resolvedEditorId);
  });
});
