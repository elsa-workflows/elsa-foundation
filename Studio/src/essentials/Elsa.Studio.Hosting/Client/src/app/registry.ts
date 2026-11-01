import {
  createEndpointContext,
  createAgentRegistry,
  createContributionRegistry,
  createDashboardWidgetRegistry,
  registerActivityIcons,
  studioSlots,
  evaluateBackendRequirements,
  type AuthSession,
  type StudioBackendAwareContribution,
  type StudioBackendSession,
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

type RegistryItem<T> = T extends StudioContributionRegistry<infer Item> ? Item : never;

export interface CreateStudioRegistryOptions {
  backendContext?: StudioEndpointContext;
  backendSession?: StudioBackendSession;
  hostSession?: AuthSession;
  backendBaseUrl?: string;
  hostHttp?: StudioEndpointContext["http"];
  backendHttp?: StudioEndpointContext["http"];
  runtime?: StudioRuntimeSettings;
}

export function createStudioRegistry(
  host: ElsaStudioHostContext,
  backendBaseUrlOrOptions?: string | CreateStudioRegistryOptions
): ElsaStudioModuleApi {
  const options = typeof backendBaseUrlOrOptions === "string"
    ? { backendBaseUrl: backendBaseUrlOrOptions }
    : backendBaseUrlOrOptions ?? {};
  const hostContext = options.hostHttp ? { ...host, http: options.hostHttp } : host;
  const backend = createBackendContext(hostContext, options);
  const hostPolicy = ({contribution}:{contribution:unknown}) => evaluateBackendRequirements(options.backendSession,(contribution as StudioBackendAwareContribution)?.requiresBackend);
  const navigation = createContributionRegistry<StudioNavigationContribution>({ slot: studioSlots.navigation,hostPolicy });
  const routes = createContributionRegistry<StudioRouteContribution>({ slot: studioSlots.routes,hostPolicy });
  const featureAreas = createFeatureAreaRegistry(navigation, routes);
  const dashboardWidgets = createDashboardWidgetRegistry({hostPolicy});
  // One dispatcher/availability instance serves both names during producer migration.
  const agent = createAgentRegistry({hostPolicy});
  const ai = { dispatchPrompt: agent.dispatchPrompt, onPrompt: agent.onPrompt, publishPromptResult: agent.publishPromptResult, onPromptResult: agent.onPromptResult };

  return {
    host: hostContext,
    backend,
    backendSession: options.backendSession,
    hostSession: options.hostSession,
    runtime: options.runtime ?? {},
    featureAreas,
    navigation,
    routes,
    dashboardWidgets,
    homeWidgets: dashboardWidgets,
    diagnosticsWidgets: createContributionRegistry<StudioDiagnosticsWidgetContribution>({ slot: studioSlots.diagnosticsWidgets,hostPolicy }),
    panels: createContributionRegistry<RegistryItem<NonNullable<ElsaStudioModuleApi["panels"]>>>({ slot: studioSlots.panels,hostPolicy }),
    toolbarActions: createContributionRegistry({ slot: studioSlots.toolbarActions,hostPolicy }),
    activityEditors: createContributionRegistry<RegistryItem<NonNullable<ElsaStudioModuleApi["activityEditors"]>>>({ slot: studioSlots.activityEditors,hostPolicy }),
    propertyEditors: createContributionRegistry<RegistryItem<NonNullable<ElsaStudioModuleApi["propertyEditors"]>>>({ slot: studioSlots.propertyEditors,hostPolicy }),
    expressionEditors: createContributionRegistry<RegistryItem<NonNullable<ElsaStudioModuleApi["expressionEditors"]>>>({ slot: studioSlots.expressionEditors,hostPolicy }),
    workflowRunInputEditors: createContributionRegistry<RegistryItem<NonNullable<ElsaStudioModuleApi["workflowRunInputEditors"]>>>({ slot: studioSlots.workflowRunInputEditors,hostPolicy }),
    workflowTestStimulusEditors: createContributionRegistry<RegistryItem<NonNullable<ElsaStudioModuleApi["workflowTestStimulusEditors"]>>>({ slot: studioSlots.workflowTestStimulusEditors,hostPolicy }),
    settingEditors: createContributionRegistry<RegistryItem<NonNullable<ElsaStudioModuleApi["settingEditors"]>>>({ slot: studioSlots.settingEditors,hostPolicy }),
    agent,
    workflowDesigner: {
      nodeRenderers: createContributionRegistry({ slot: studioSlots.workflowDesignerNodeRenderers,hostPolicy }),
      toolboxItems: createContributionRegistry({ slot: studioSlots.workflowDesignerToolboxItems,hostPolicy }),
      panels: createContributionRegistry<RegistryItem<ElsaStudioModuleApi["workflowDesigner"]["panels"]>>({ slot: studioSlots.workflowDesignerPanels,hostPolicy }),
      registerActivityIcons
    },
    ai,
    dialogs: dialogController.api,
    diagnostics: createContributionRegistry<StudioModuleDiagnostic>({ slot: studioSlots.diagnostics })
  };
}

function createBackendContext(host: ElsaStudioHostContext, options: CreateStudioRegistryOptions) {
  if (options.backendContext) return options.backendContext;
  const backend = createEndpointContext(options.backendBaseUrl ?? host.baseUrl);
  return {
    ...backend,
    ...(options.backendHttp && { http: options.backendHttp }),
    ...(host.accessTokenFactory && { accessTokenFactory: host.accessTokenFactory })
  };
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

  const children = featureArea.nav.items
    .filter(item => item.path !== parent.path)
    .map((item, index) => {
      const contribution = {
        id: item.id ?? `${featureArea.id}-${slug(item.title)}`,
        label: item.title,
        path: item.path,
        activePathPrefix: item.path,
        order: item.order ?? (featureArea.order ?? 500) + index + 1,
        iconColor: item.iconColor ?? parent.iconColor,
        parentId: item.placement ? undefined : parent.id,
        ...copyPlacementMetadata(item)
      };
      return contribution;
    });

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
    order: nav.order ?? featureArea.order,
    iconColor: nav.iconColor,
    ...copyPlacementMetadata(nav)
  };
}

function copyPlacementMetadata(nav: {
  placement?: StudioFeatureAreaNavContribution["placement"];
  requiresFeature?: StudioFeatureAreaNavContribution["requiresFeature"];
  requiresRole?: StudioFeatureAreaNavContribution["requiresRole"];
  requiresPermission?: StudioFeatureAreaNavContribution["requiresPermission"];
  requiresBackend?: StudioBackendAwareContribution["requiresBackend"];
}) {
  return {
    ...(nav.placement ? { placement: nav.placement } : {}),
    ...(nav.requiresFeature ? { requiresFeature: nav.requiresFeature } : {}),
    ...(nav.requiresRole ? { requiresRole: nav.requiresRole } : {}),
    ...(nav.requiresPermission ? { requiresPermission: nav.requiresPermission } : {}),
    ...(nav.requiresBackend ? {requiresBackend:nav.requiresBackend} : {})
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
