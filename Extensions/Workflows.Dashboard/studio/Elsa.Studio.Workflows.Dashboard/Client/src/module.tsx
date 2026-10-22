import { registerHomeWidget, type ElsaStudioModuleApi, type StudioSettingDescriptor } from "@elsa-workflows/studio-sdk";
import { defaultRunHealthSettings, loadPortfolio, loadRunHealth, type RunHealthSettings, type WorkflowPortfolioSnapshot, type WorkflowRunHealthSnapshot } from "./workflowDashboardApi";
import { WorkflowPortfolioWidget } from "./WorkflowPortfolioWidget";
import { WorkflowRunHealthWidget } from "./WorkflowRunHealthWidget";
import "./styles.css";
const rangeDescriptor: StudioSettingDescriptor = {
  name: "range", displayName: "Date range", jsonType: "string", required: true,
  secret: false, sensitive: false, restartRequired: false, advanced: false, experimental: false,
  options: [{ label: "24 hours", value: "24h" }, { label: "7 days", value: "7d" }, { label: "30 days", value: "30d" }]
};
const testsDescriptor: StudioSettingDescriptor = {
  name: "includeTestRuns", displayName: "Include test runs", jsonType: "boolean", required: true,
  secret: false, sensitive: false, restartRequired: false, advanced: false, experimental: false, options: []
};
export function register(api: ElsaStudioModuleApi) {
  registerHomeWidget<WorkflowPortfolioSnapshot>(api, {
    id: "workflows.portfolio", moduleId: "Elsa.Studio.Workflows.Dashboard", title: "Workflow portfolio",
    description: "Current workflow definition readiness.", order: 10, defaultVisible: true, defaultSize: "medium",
    permissions: ["workflows/dashboard:read"], requiresFeature: "workflows",
    supportedSizes: ["small", "medium"], minimumRefreshIntervalMs: 300_000, cacheLifetimeMs: 300_000,
    load: ({ signal }) => loadPortfolio(api, signal), component: WorkflowPortfolioWidget
  });
  registerHomeWidget<WorkflowRunHealthSnapshot, RunHealthSettings>(api, {
    id: "workflows.run-health", moduleId: "Elsa.Studio.Workflows.Dashboard", title: "Workflow run health",
    description: "Run outcomes, start activity and workflow failures.", order: 20, defaultVisible: true, defaultSize: "full",
    permissions: ["workflows/dashboard:read"], requiresFeature: "workflows",
    supportedSizes: ["medium", "wide", "full"], minimumRefreshIntervalMs: 60_000, cacheLifetimeMs: 60_000,
    settings: { schemaVersion: 1, defaults: defaultRunHealthSettings, descriptors: [rangeDescriptor, testsDescriptor], validate: value => isSettings(value) ? value : null },
    load: ({ settings, signal }) => loadRunHealth(api, settings, signal), component: WorkflowRunHealthWidget
  });
}
function isSettings(value: unknown): value is RunHealthSettings {
  if (!value || typeof value !== "object") return false;
  const candidate = value as Partial<RunHealthSettings>;
  return ["24h", "7d", "30d"].includes(candidate.range ?? "") && typeof candidate.includeTestRuns === "boolean";
}
