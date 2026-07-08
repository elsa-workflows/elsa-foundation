import {
  createEndpointContext,
  createAiContributionApi,
  createContributionRegistry,
  studioSlots,
  type ElsaStudioHostContext,
  type ElsaStudioModuleApi,
  type StudioContributionRegistry,
  type StudioEndpointContext,
  type StudioFeatureAreaContribution,
  type StudioFeatureAreaNavContribution,
  type StudioFeatureAreaNavParent,
  type StudioDiagnosticsWidgetContribution,
  type StudioModuleDiagnostic,
  type StudioNavigationContribution,
  type StudioRuntimeSettings,
  type StudioRouteContribution
} from "../sdk";
import { dialogController } from "./dialogs";

export interface CreateStudioRegistryOptions {
  backendBaseUrl?: string;
  backendHeaders?: HeadersInit;
  hostHttp?: StudioEndpointContext["http"];
  backendHttp?: StudioEndpointContext["http"];
  runtime?: StudioRuntimeSettings;
}

export function createStudioRegistry(
  host: ElsaStudioHostContext,
  backendBaseUrlOrOptions?: string | CreateStudioRegistryOptions,
  backendHeaders?: HeadersInit
): ElsaStudioModuleApi {
  const options = typeof backendBaseUrlOrOptions === "string"
    ? { backendBaseUrl: backendBaseUrlOrOptions, backendHeaders }
    : backendBaseUrlOrOptions ?? {};
  const hostContext = options.hostHttp ? { ...host, http: options.hostHttp } : host;
  const backend = createBackendContext(hostContext, options);
  const navigation = createContributionRegistry<StudioNavigationContribution>({ slot: studioSlots.navigation });
  const routes = createContributionRegistry<StudioRouteContribution>({ slot: studioSlots.routes });
  const featureAreas = createFeatureAreaRegistry(navigation, routes);

  return {
    host: hostContext,
    backend,
    runtime: options.runtime ?? {},
    featureAreas,
    navigation,
    routes,
    dashboardWidgets: createContributionRegistry({ slot: studioSlots.dashboardWidgets }),
    diagnosticsWidgets: createContributionRegistry<StudioDiagnosticsWidgetContribution>({ slot: studioSlots.diagnosticsWidgets }),
    panels: createContributionRegistry({ slot: studioSlots.panels }),
    toolbarActions: createContributionRegistry({ slot: studioSlots.toolbarActions }),
    activityEditors: createContributionRegistry({ slot: studioSlots.activityEditors }),
    propertyEditors: createContributionRegistry({ slot: studioSlots.propertyEditors }),
    expressionEditors: createContributionRegistry({ slot: studioSlots.expressionEditors }),
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
    diagnostics: createContributionRegistry<StudioModuleDiagnostic>({ slot: studioSlots.diagnostics })
  };
}

function createBackendContext(host: ElsaStudioHostContext, options: CreateStudioRegistryOptions) {
  const backend = createEndpointContext(options.backendBaseUrl ?? host.baseUrl, { headers: options.backendHeaders });
  return options.backendHttp ? { ...backend, http: options.backendHttp } : backend;
}

function createFeatureAreaRegistry(
  navigation: StudioContributionRegistry<StudioNavigationContribution>,
  routes: StudioContributionRegistry<StudioRouteContribution>
): StudioContributionRegistry<StudioFeatureAreaContribution> {
  const featureAreas = createContributionRegistry<StudioFeatureAreaContribution>({ slot: studioSlots.featureAreas });

  return {
    slot: featureAreas.slot,
    add(featureArea) {
      featureAreas.add(featureArea);
      for (const item of createNavigationContributions(featureArea)) {
        navigation.add(item);
      }
      for (const route of featureArea.routes) {
        routes.add(route);
      }
    },
    list: featureAreas.list,
    compose: featureAreas.compose
  };
}

function createNavigationContributions(featureArea: StudioFeatureAreaContribution): StudioNavigationContribution[] {
  const activePathPrefix = featureArea.ownedPaths[0];
  const parent = createNavigationContribution(featureArea, featureArea.nav, activePathPrefix);

  if (!isParentNav(featureArea.nav)) {
    return [parent];
  }

  const children = featureArea.nav.items.map((item, index) => ({
    id: item.id ?? `${featureArea.id}-${slug(item.title)}`,
    label: item.title,
    path: item.path,
    activePathPrefix: item.path,
    order: (featureArea.order ?? 500) + index + 1,
    iconColor: item.iconColor ?? parent.iconColor,
    parentId: parent.id
  }));

  return [parent, ...children];
}

function createNavigationContribution(
  featureArea: StudioFeatureAreaContribution,
  nav: StudioFeatureAreaNavContribution,
  activePathPrefix?: string
): StudioNavigationContribution {
  return {
    id: nav.id ?? featureArea.id,
    label: nav.title,
    path: nav.path,
    activePathPrefix,
    order: featureArea.order,
    iconColor: nav.iconColor
  };
}

function isParentNav(nav: StudioFeatureAreaNavContribution): nav is StudioFeatureAreaNavParent {
  return "items" in nav;
}

function slug(value: string) {
  return value.trim().toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "");
}

export function findFeatureAreaForPath(featureAreas: StudioFeatureAreaContribution[], path: string) {
  return featureAreas
    .flatMap(featureArea => featureArea.ownedPaths.map(ownedPath => ({ featureArea, ownedPath })))
    .filter(({ ownedPath }) => path === ownedPath || path.startsWith(`${ownedPath}/`))
    .sort((a, b) => b.ownedPath.length - a.ownedPath.length)[0]
    ?.featureArea;
}
