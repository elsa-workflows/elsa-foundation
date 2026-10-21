import { describe, expect, it } from "vitest";
import {
  createAiContributionApi,
  createContributionRegistry,
  createDashboardWidgetRegistry,
  createDialogController,
  createEndpointContext,
  studioSlots,
  type ElsaStudioModuleApi
} from "@elsa-workflows/studio-sdk";
import { register } from "../module";

describe("secrets module", () => {
  it("registers a Secrets-owned Secret Reference Contribution", () => {
    const api = stubApi();

    register(api);

    expect(api.featureAreas.list()).toEqual([
      expect.objectContaining({ id: "secrets", title: "Secrets", navGroup: "Security", ownedPaths: ["/security/secrets", "/secrets"] })
    ]);
    expect(api.propertyEditors.list()).toEqual([]);
    expect(api.expressionEditors.list()).toEqual([
      expect.objectContaining({
        id: "elsa.secret-reference-editor",
        surfaces: expect.objectContaining({ inline: expect.any(Function) }),
        createDefaultValue: expect.any(Function)
      })
    ]);
    const contribution = api.expressionEditors.list()[0];
    const context = {
      syntax: "Secret",
      surface: "inline" as const,
      descriptor: { name: "token", typeName: "System.String" },
      activity: {},
      expressionDescriptors: [{ type: "Secret", displayName: "Secret", editingMode: "reference" as const }]
    };
    expect(contribution.supports(context)).toBe(true);
    expect(contribution.createDefaultValue?.(context)).toBeNull();
  });
});

function stubApi(): ElsaStudioModuleApi {
  const dashboardWidgets = createDashboardWidgetRegistry();
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
    agent: {
      contextProviders: createContributionRegistry({ slot: studioSlots.agentContextProviders }),
      promptStarters: createContributionRegistry({ slot: studioSlots.agentPromptStarters }),
      capabilities: createContributionRegistry({ slot: studioSlots.agentCapabilities }),
      actions: createContributionRegistry({ slot: studioSlots.agentActions }),
      toolSlots: createContributionRegistry({ slot: studioSlots.agentToolSlots }),
      toolContracts: createContributionRegistry({ slot: studioSlots.agentToolContracts }),
      resultRenderers: createContributionRegistry({ slot: studioSlots.agentResultRenderers })
    },
    workflowDesigner: {
      nodeRenderers: createContributionRegistry({ slot: studioSlots.workflowDesignerNodeRenderers }),
      toolboxItems: createContributionRegistry({ slot: studioSlots.workflowDesignerToolboxItems }),
      panels: createContributionRegistry({ slot: studioSlots.workflowDesignerPanels })
    },
    ai: createAiContributionApi(),
    dialogs: createDialogController().api,
    diagnostics: createContributionRegistry({ slot: studioSlots.diagnostics })
  };
}
