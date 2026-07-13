import {
  createAiContributionApi,
  createContributionRegistry,
  createDialogController,
  createEndpointContext,
  studioSlots,
  type ElsaStudioHostContext,
  type ElsaStudioModuleApi,
  type StudioContributionRegistry,
  type StudioFeatureAreaContribution,
  type StudioNavigationContribution,
  type StudioRouteContribution
} from "@elsa-workflows/studio-sdk";

/**
 * An honest, minimal implementation of `ElsaStudioModuleApi` built ENTIRELY from the
 * factories the SDK exports publicly (`createContributionRegistry`, `createAiContributionApi`,
 * `createDialogController`, `createEndpointContext`). Nothing here is faked or `as any`-cast:
 * every registry is a real registry, `dialogs` is a real dialog controller, and `backend` is
 * a real endpoint context.
 *
 * This mirrors what Elsa.Studio.Web's own host does in `Client/src/app/registry.ts`
 * (`createStudioRegistry`). That host factory is intentionally NOT part of the published SDK
 * surface — assembling the API is the HOST's job — so an external host builds it like this.
 *
 * `@elsa-workflows/studio-workflows`'s `register(api)` touches exactly:
 *   - `api.dialogs`               (passed to its internal `setDialogs`)
 *   - `api.featureAreas.add(...)` (registers one "Workflows" feature area with nav + routes)
 * and each registered route component reads `api.backend` / `api.ai` / the various editor
 * registries at RENDER time. So a working stub only needs those to be real and callable.
 */
export function createStubModuleApi(backendBaseUrl: string): ElsaStudioModuleApi {
  const host: ElsaStudioHostContext = {
    ...createEndpointContext(backendBaseUrl),
    hostVersion: "example-react-host",
    sdkVersion: "0.0.0-example"
  };

  const dialogController = createDialogController();
  const navigation = createContributionRegistry<StudioNavigationContribution>({ slot: studioSlots.navigation });
  const routes = createContributionRegistry<StudioRouteContribution>({ slot: studioSlots.routes });
  const featureAreas = createFeatureAreaRegistry(navigation, routes);

  return {
    host,
    backend: createEndpointContext(backendBaseUrl),
    runtime: {},
    featureAreas,
    navigation,
    routes,
    dashboardWidgets: createContributionRegistry({ slot: studioSlots.dashboardWidgets }),
    diagnosticsWidgets: createContributionRegistry({ slot: studioSlots.diagnosticsWidgets }),
    panels: createContributionRegistry({ slot: studioSlots.panels }),
    toolbarActions: createContributionRegistry({ slot: studioSlots.toolbarActions }),
    activityEditors: createContributionRegistry({ slot: studioSlots.activityEditors }),
    propertyEditors: createContributionRegistry({ slot: studioSlots.propertyEditors }),
    expressionEditors: createContributionRegistry({ slot: studioSlots.expressionEditors }),
    workflowRunInputEditors: createContributionRegistry({ slot: studioSlots.workflowRunInputEditors }),
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
    dialogs: dialogController.api,
    diagnostics: createContributionRegistry({ slot: studioSlots.diagnostics })
  };
}

/**
 * The host's featureAreas registry fans a single `featureAreas.add(area)` call out into the
 * navigation and routes registries (so nav + routing stay derived from one source). We
 * reproduce that fan-out here so the piece registered by the workflows module lands in
 * `routes` where the app can pick it up and render it.
 */
function createFeatureAreaRegistry(
  navigation: StudioContributionRegistry<StudioNavigationContribution>,
  routes: StudioContributionRegistry<StudioRouteContribution>
): StudioContributionRegistry<StudioFeatureAreaContribution> {
  const featureAreas = createContributionRegistry<StudioFeatureAreaContribution>({ slot: studioSlots.featureAreas });

  return {
    slot: featureAreas.slot,
    add(featureArea) {
      featureAreas.add(featureArea);
      navigation.add({
        id: featureArea.id,
        label: featureArea.nav.title,
        path: featureArea.nav.path,
        iconColor: featureArea.nav.iconColor,
        order: featureArea.order
      });
      for (const route of featureArea.routes) {
        routes.add(route);
      }
    },
    list: featureAreas.list,
    compose: featureAreas.compose
  };
}
