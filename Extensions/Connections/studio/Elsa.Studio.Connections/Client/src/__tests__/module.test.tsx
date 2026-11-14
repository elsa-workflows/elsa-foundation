import { describe, expect, it } from "vitest";
import {
  createWeaverRegistry,
  createContributionRegistry,
  createDashboardWidgetRegistry,
  createDialogController,
  createEndpointContext,
  studioSlots,
  type ElsaStudioModuleApi
} from "@elsa-workflows/studio-sdk";
import { register } from "../module";

describe("connections module", () => {
  it("registers a Connections page and Connection expression editor", () => {
    const api = stubApi();

    register(api);

    expect(api.featureAreas.list()).toEqual([
      expect.objectContaining({ id: "connections", title: "Connections", navGroup: "Security", ownedPaths: ["/security/connections"] })
    ]);
    expect(api.propertyEditors.list()).toEqual([]);
    expect(api.expressionEditors.list()).toEqual([
      expect.objectContaining({
        id: "elsa.connection-reference-editor",
        presentsField: true,
        surfaces: expect.objectContaining({ inline: expect.any(Function) }),
        createDefaultValue: expect.any(Function)
      })
    ]);
    const contribution = api.expressionEditors.list()[0];
    const context = {
      syntax: "Connection",
      surface: "inline" as const,
      descriptor: { name: "Connection", typeName: "ConnectionReference", uiHint: "connection-picker" },
      activity: {},
      expressionDescriptors: [{ type: "Connection", displayName: "Connection", editingMode: "reference" as const }]
    };
    expect(contribution.supports(context)).toBe(true);
    expect(contribution.createDefaultValue?.(context)).toBeNull();
  });
});

function stubApi(): ElsaStudioModuleApi {
  const dashboardWidgets = createDashboardWidgetRegistry();
  const weaver = createWeaverRegistry();
  return {
    host: {
      ...createEndpointContext("https://studio.example/"),
      hostVersion: "1.0.0",
      sdkVersion: "1.0.0"
    },
    backend: createEndpointContext("https://server.example/"),
    runtime: {},
    featureAreas: createContributionRegistry({ slot: studioSlots.featureAreas }),
    navigation: createContributionRegistry({ slot: studioSlots.navigation }),
    routes: createContributionRegistry({ slot: studioSlots.routes }),
    dashboardWidgets,
    homeWidgets: dashboardWidgets,
    diagnosticsWidgets: createContributionRegistry({ slot: studioSlots.diagnosticsWidgets }),
    panels: createContributionRegistry({ slot: studioSlots.panels }),
    toolbarActions: createContributionRegistry({ slot: studioSlots.toolbarActions }),
    activityEditors: createContributionRegistry({ slot: studioSlots.activityEditors }),
    propertyEditors: createContributionRegistry({ slot: studioSlots.propertyEditors }),
    expressionEditors: createContributionRegistry({ slot: studioSlots.expressionEditors }),
    workflowRunInputEditors: createContributionRegistry({ slot: studioSlots.workflowRunInputEditors }),
    workflowTestStimulusEditors: createContributionRegistry({ slot: studioSlots.workflowTestStimulusEditors }),
    settingEditors: createContributionRegistry({ slot: studioSlots.settingEditors }),
    weaver,
    workflowDesigner: {
      nodeRenderers: createContributionRegistry({ slot: studioSlots.workflowDesignerNodeRenderers }),
      toolboxItems: createContributionRegistry({ slot: studioSlots.workflowDesignerToolboxItems }),
      panels: createContributionRegistry({ slot: studioSlots.workflowDesignerPanels })
    },
    ai: weaver,
    dialogs: createDialogController().api,
    diagnostics: createContributionRegistry({ slot: studioSlots.diagnostics })
  };
}
