import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { registerBuiltInSettingEditors } from "@elsa-workflows/studio-ui";
import { FeatureManagementScreen } from "./FeatureManagementPage";
import "./styles.css";

let moduleApi: ElsaStudioModuleApi;

export function register(api: ElsaStudioModuleApi) {
  moduleApi = api;
  registerBuiltInSettingEditors(api);

  api.navigation.add({
    id: "feature-management",
    label: "Features",
    path: "/features",
    order: 40,
    placement: "admin"
  });

  api.routes.add({
    id: "feature-management",
    label: "Features",
    path: "/features",
    component: () => <FeatureManagementPage key={api.backendSession?.key ?? api.backend.baseUrl} />
  });
}

export function FeatureManagementPage() {
  return <FeatureManagementScreen moduleApi={moduleApi} />;
}

export { registerBuiltInSettingEditors, selectSettingEditor } from "@elsa-workflows/studio-ui";
export { applyFeatureToggle, buildDependentsIndex, computeFeatureCascade, createApplyPayload, featureKeys, filterFeaturesByText, getErrorMessage, isDirty } from "./featureModel";
