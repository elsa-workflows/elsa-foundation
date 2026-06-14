import {
  createContributionRegistry,
  type ElsaStudioHostContext,
  type ElsaStudioModuleApi,
  type StudioModuleDiagnostic
} from "../sdk";

export function createStudioRegistry(host: ElsaStudioHostContext): ElsaStudioModuleApi {
  return {
    host,
    navigation: createContributionRegistry(),
    routes: createContributionRegistry(),
    dashboardWidgets: createContributionRegistry(),
    panels: createContributionRegistry(),
    toolbarActions: createContributionRegistry(),
    activityEditors: createContributionRegistry(),
    propertyEditors: createContributionRegistry(),
    workflowDesigner: {
      nodeRenderers: createContributionRegistry(),
      toolboxItems: createContributionRegistry()
    },
    diagnostics: createContributionRegistry<StudioModuleDiagnostic>()
  };
}

