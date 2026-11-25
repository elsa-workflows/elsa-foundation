import { describe, expect, it } from "vitest";
import { homeFeatureTokensMatch, isHomeWidgetFeatureEnabled } from "@elsa-workflows/studio-sdk";
import { builtInHomeLayout } from "../homeLayout";

describe("Home layout defaults and feature gates", () => {
  it("ships workflow health and portfolio before the core Home widgets", () => {
    expect(builtInHomeLayout.widgets.map(entry => entry.id)).toEqual([
      "home.get-started",
      "workflows.run-health",
      "workflows.portfolio",
      "home.quick-actions",
      "home.needs-attention",
      "home.recent-runs",
      "home.recent-workflows",
      "home.tasks"
    ]);
    expect(builtInHomeLayout.widgets.map(entry => entry.size)).toEqual([
      "full", "full", "medium", "medium", "medium", "medium", "medium", "medium"
    ]);
  });

  it("matches Studio module assembly ids to feature slugs", () => {
    expect(homeFeatureTokensMatch("workflows", "Elsa.Studio.Workflows")).toBe(true);
    expect(homeFeatureTokensMatch("user-tasks", "Elsa.Studio.UserTasks")).toBe(true);
    expect(homeFeatureTokensMatch("Elsa.Studio.Workflows", "workflows")).toBe(true);
    expect(homeFeatureTokensMatch("user-tasks", "workflows")).toBe(false);
    expect(homeFeatureTokensMatch("workflows", "Elsa.Studio.Dashboard")).toBe(false);
    expect(homeFeatureTokensMatch("workflows", "Elsa.Studio.WorkflowsX")).toBe(false);
    expect(homeFeatureTokensMatch("workflows", "Elsa.Studio.Other.Workflows")).toBe(false);
    expect(homeFeatureTokensMatch("workflows", "Contoso.Workflows")).toBe(false);
    expect(homeFeatureTokensMatch("workflows", "Elsa.Studio.Workflows.Extra")).toBe(false);
    expect(homeFeatureTokensMatch("workflows", "Elsa.Studio.XWorkflows")).toBe(false);
    expect(homeFeatureTokensMatch("workflows", "Elsa.Workflows")).toBe(false);
    expect(homeFeatureTokensMatch("workflows", "Studio.Workflows")).toBe(false);
    expect(homeFeatureTokensMatch("workflows", "workflows-extra")).toBe(false);
    expect(homeFeatureTokensMatch("workflows", "extra-workflows")).toBe(false);
    expect(homeFeatureTokensMatch("user-tasks", "Elsa.Studio.UserTasksX")).toBe(false);
    expect(homeFeatureTokensMatch("user-tasks", "Contoso.UserTasks")).toBe(false);
  });

  it("fails closed when enabledFeatures is omitted", () => {
    expect(isHomeWidgetFeatureEnabled({ requiresFeature: "workflows" }, undefined)).toBe(false);
    expect(isHomeWidgetFeatureEnabled({}, undefined)).toBe(true);
  });

});
