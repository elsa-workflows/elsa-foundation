import {
  createEndpointContext,
  createAiContributionApi,
  createContributionRegistry,
  type ElsaStudioHostContext,
  type ElsaStudioModuleApi,
  type StudioModuleDiagnostic
} from "../sdk";

export function createStudioRegistry(host: ElsaStudioHostContext, backendBaseUrl?: string, backendHeaders?: HeadersInit): ElsaStudioModuleApi {
  const backend = createEndpointContext(backendBaseUrl ?? host.baseUrl, { headers: backendHeaders });

  return {
    host,
    backend,
    navigation: createContributionRegistry(),
    routes: createContributionRegistry(),
    dashboardWidgets: createContributionRegistry(),
    panels: createContributionRegistry(),
    toolbarActions: createContributionRegistry(),
    activityEditors: createContributionRegistry(),
    propertyEditors: createContributionRegistry(),
    settingEditors: createContributionRegistry(),
    workflowDesigner: {
      nodeRenderers: createContributionRegistry(),
      toolboxItems: createContributionRegistry()
    },
    ai: createAiContributionApi(),
    diagnostics: createContributionRegistry<StudioModuleDiagnostic>()
  };
}
