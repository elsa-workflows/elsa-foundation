import { lazy } from "react";
import {
  hasHomeRunDetailOpened,
  homeWidgetIds,
  registerHomeWidget,
  type ElsaStudioModuleApi
} from "@elsa-workflows/studio-sdk";
import { homeHasRun, homeHasWorkflow, loadNeedsAttention, loadRecentRuns, loadRecentWorkflows } from "./homeData";
import type { GetStartedSnapshot } from "./GetStartedWidget";

const GetStartedWidget = lazy(() => import("./GetStartedWidget").then(module => ({ default: module.GetStartedWidget })));
const RecentWorkflowsWidget = lazy(() => import("./HomeListWidget").then(module => ({ default: module.RecentWorkflowsWidget })));
const RecentRunsWidget = lazy(() => import("./HomeListWidget").then(module => ({ default: module.RecentRunsWidget })));
const NeedsAttentionWidget = lazy(() => import("./HomeListWidget").then(module => ({ default: module.NeedsAttentionWidget })));

export function registerCoreHomeWidgets(api: ElsaStudioModuleApi) {
  registerHomeWidget(api, {
    id: homeWidgetIds.getStarted,
    moduleId: "Elsa.Studio.Dashboard",
    title: "Get started",
    description: "Create a workflow, run it, and see what happened.",
    core: true,
    defaultVisible: true,
    defaultSize: "L",
    supportedSizes: ["L", "wide"],
    load: async ({ signal }) => ({
      definitionExists: await homeHasWorkflow(api, signal),
      runExists: await homeHasRun(api, signal),
      runDetailOpened: hasHomeRunDetailOpened()
    } satisfies GetStartedSnapshot),
    component: GetStartedWidget
  });

  registerHomeWidget(api, {
    id: homeWidgetIds.recentWorkflows,
    moduleId: "Elsa.Studio.Dashboard",
    title: "Recent workflows",
    description: "The latest workflow definitions.",
    core: true,
    requiresFeature: "workflows",
    defaultVisible: true,
    defaultSize: "M",
    supportedSizes: ["S", "M", "wide"],
    minimumRefreshIntervalMs: 60_000,
    load: ({ signal }) => loadRecentWorkflows(api, signal),
    isEmpty: snapshot => !snapshot?.length,
    emptyState: { title: "No workflows yet", description: "Create a workflow to see it here." },
    component: RecentWorkflowsWidget
  });

  registerHomeWidget(api, {
    id: homeWidgetIds.needsAttention,
    moduleId: "Elsa.Studio.Dashboard",
    title: "Needs attention",
    description: "Failed, faulted, incident-bearing and long-waiting runs.",
    core: true,
    requiresFeature: "workflows",
    defaultVisible: true,
    defaultSize: "M",
    supportedSizes: ["S", "M", "wide", "L"],
    minimumRefreshIntervalMs: 60_000,
    load: ({ signal }) => loadNeedsAttention(api, signal),
    isEmpty: snapshot => !snapshot?.length,
    emptyState: { title: "All clear", description: "Nothing needs attention." },
    component: NeedsAttentionWidget
  });

  registerHomeWidget(api, {
    id: homeWidgetIds.recentRuns,
    moduleId: "Elsa.Studio.Dashboard",
    title: "Recent runs",
    description: "The latest workflow executions.",
    core: true,
    requiresFeature: "workflows",
    defaultVisible: true,
    defaultSize: "L",
    supportedSizes: ["M", "wide", "L"],
    minimumRefreshIntervalMs: 60_000,
    load: ({ signal }) => loadRecentRuns(api, signal),
    isEmpty: snapshot => !snapshot?.length,
    emptyState: { title: "No runs yet", description: "Run a workflow to see it here." },
    component: RecentRunsWidget
  });
}
