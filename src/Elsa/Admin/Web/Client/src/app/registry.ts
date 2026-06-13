import {
  type AdminDashboardWidgetContribution,
  type AdminModuleDiagnostic,
  type AdminNavigationContribution,
  type AdminRouteContribution,
  createContributionRegistry,
  type ElsaAdminHostContext,
  type ElsaAdminModuleApi
} from "../sdk";

export function createAdminRegistry(host: ElsaAdminHostContext): ElsaAdminModuleApi {
  return {
    host,
    navigation: createContributionRegistry<AdminNavigationContribution>(),
    routes: createContributionRegistry<AdminRouteContribution>(),
    dashboardWidgets: createContributionRegistry<AdminDashboardWidgetContribution>(),
    panels: createContributionRegistry<unknown>(),
    toolbarActions: createContributionRegistry<unknown>(),
    activityEditors: createContributionRegistry<unknown>(),
    propertyEditors: createContributionRegistry<unknown>(),
    workflowDesigner: {
      nodeRenderers: createContributionRegistry<unknown>(),
      toolboxItems: createContributionRegistry<unknown>()
    },
    diagnostics: createContributionRegistry<AdminModuleDiagnostic>()
  };
}
