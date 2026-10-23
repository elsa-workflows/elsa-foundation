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
const QuickActionsWidget = lazy(() => import("./HomeListWidget").then(module => ({ default: module.QuickActionsWidget })));
const RecentWorkflowsWidget = lazy(() => import("./HomeListWidget").then(module => ({ default: module.RecentWorkflowsWidget })));
const RecentRunsWidget = lazy(() => import("./HomeListWidget").then(module => ({ default: module.RecentRunsWidget })));
const NeedsAttentionWidget = lazy(() => import("./HomeListWidget").then(module => ({ default: module.NeedsAttentionWidget })));
const TasksWidget = lazy(() => import("./HomeListWidget").then(module => ({ default: module.TasksWidget })));

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
    id: homeWidgetIds.quickActions,
    moduleId: "Elsa.Studio.Dashboard",
    title: "Quick actions",
    description: "Create a workflow or open the designer.",
    core: true,
    requiresFeature: "workflows",
    defaultVisible: true,
    defaultSize: "M",
    supportedSizes: ["S", "M", "wide"],
    component: QuickActionsWidget
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
    component: RecentWorkflowsWidget
  });

  registerHomeWidget(api, {
    id: homeWidgetIds.recentRuns,
    moduleId: "Elsa.Studio.Dashboard",
    title: "Recent runs",
    description: "The latest workflow executions and their status.",
    core: true,
    requiresFeature: "workflows",
    defaultVisible: true,
    defaultSize: "M",
    supportedSizes: ["M", "wide", "L"],
    minimumRefreshIntervalMs: 60_000,
    load: ({ signal }) => loadRecentRuns(api, signal),
    component: RecentRunsWidget
  });

  registerHomeWidget(api, {
    id: homeWidgetIds.needsAttention,
    moduleId: "Elsa.Studio.Dashboard",
    title: "Needs attention",
    description: "Runs with active incidents that need your attention.",
    core: true,
    requiresFeature: "workflows",
    defaultVisible: true,
    defaultSize: "M",
    supportedSizes: ["S", "M", "wide", "L"],
    minimumRefreshIntervalMs: 60_000,
    load: ({ signal }) => loadNeedsAttention(api, signal),
    component: NeedsAttentionWidget
  });

  registerHomeWidget(api, {
    id: homeWidgetIds.tasks,
    moduleId: "Elsa.Studio.Dashboard",
    title: "Tasks",
    description: "Open assigned work in the Tasks inbox.",
    core: true,
    requiresFeature: "user-tasks",
    defaultVisible: true,
    defaultSize: "M",
    supportedSizes: ["S", "M", "wide"],
    component: TasksWidget
  });
}
