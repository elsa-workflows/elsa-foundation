import React, { createElement, lazy } from "react";
import {
  evaluateBackendRequirements,
  hasHomeRunDetailOpened,
  homeWidgetIds,
  registerHomeWidget,
  type ElsaStudioModuleApi
} from "@elsa-workflows/studio-sdk";
import { homeHasRun, homeHasWorkflow, loadNeedsAttention, loadRecentRuns, loadRecentWorkflows } from "./homeData";
import { getStartedSteps, type GetStartedSnapshot } from "./getStartedSteps";
import { HomeTaskGroup } from "./HomeTaskGroup";
import "./home.css";

const GetStartedWidget = lazy(() => import("./GetStartedWidget").then(module => ({ default: module.GetStartedWidget })));
const QuickActionsWidget = lazy(() => import("./HomeListWidget").then(module => ({ default: module.QuickActionsWidget })));
const RecentWorkflowsWidget = lazy(() => import("./HomeListWidget").then(module => ({ default: module.RecentWorkflowsWidget })));
const RecentRunsWidget = lazy(() => import("./HomeListWidget").then(module => ({ default: module.RecentRunsWidget })));
const NeedsAttentionWidget = lazy(() => import("./HomeListWidget").then(module => ({ default: module.NeedsAttentionWidget })));
const TasksWidget = lazy(() => import("./HomeListWidget").then(module => ({ default: module.TasksWidget })));

const moduleId = "Elsa.Studio.Workflows.Dashboard";

/** Workflow Home content: the workflow task group above the grid and the core workflow widgets. */
export function registerWorkflowHome(api: ElsaStudioModuleApi) {
  api.homeSections.add({
    id: "workflows.tasks",
    moduleId,
    order: 10,
    requiresFeature: "workflows",
    component: () => <HomeTaskGroup api={api} />
  });

  registerHomeWidget<GetStartedSnapshot>(api, {
    id: homeWidgetIds.getStarted,
    requiresBackend: [{id:"elsa.api.workflow-design",contractVersion:"1"},{id:"elsa.api.runtime",contractVersion:"1"}],
    moduleId,
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
    isComplete: snapshot => getStartedSteps(snapshot).every(step => step.done),
    component: GetStartedWidget
  });

  registerHomeWidget(api, {
    id: homeWidgetIds.quickActions,
    requiresBackend: [{id:"elsa.api.workflow-design",contractVersion:"1"}],
    moduleId,
    title: "Quick actions",
    description: "Create a workflow or open the designer.",
    core: true,
    requiresFeature: "workflows",
    defaultVisible: true,
    defaultSize: "M",
    supportedSizes: ["S", "M", "wide"],
    component: () => createElement(QuickActionsWidget,{runtimeAvailable:evaluateBackendRequirements(api.backendSession,[{id:"elsa.api.runtime",contractVersion:"1"}]).state === "available"})
  });

  registerHomeWidget(api, {
    id: homeWidgetIds.recentWorkflows,
    requiresBackend: [{id:"elsa.api.workflow-design",contractVersion:"1"}],
    moduleId,
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
    requiresBackend: [{id:"elsa.api.runtime",contractVersion:"1"}],
    moduleId,
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
    requiresBackend: [{id:"elsa.api.attention",contractVersion:"1"}],
    moduleId,
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
    moduleId,
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
