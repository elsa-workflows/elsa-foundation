import React from "react";
import { createRoot } from "react-dom/client";
import { flushSync } from "react-dom";
import { describe, expect, it } from "vitest";
import { builtInHomeLayout, homeFeatureTokensMatch, isHomeWidgetFeatureEnabled } from "../homeLayout";
import { GetStartedWidget, homeDocsLinks } from "../widgets/GetStartedWidget";
import { QuickActionsWidget, RecentRunsWidget, RecentWorkflowsWidget, TasksWidget } from "../widgets/HomeListWidget";
import { runStatusTone } from "../widgets/homeData";

describe("Home default landing", () => {
  it("ships Get started, quick actions, recent lists, attention, and gated Tasks", () => {
    expect(builtInHomeLayout.widgets.map(entry => entry.id)).toEqual([
      "home.get-started",
      "home.quick-actions",
      "home.recent-workflows",
      "home.recent-runs",
      "home.needs-attention",
      "home.tasks"
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
  });

  it("fails closed when enabledFeatures is omitted", () => {
    expect(isHomeWidgetFeatureEnabled({ requiresFeature: "workflows" }, undefined)).toBe(false);
    expect(isHomeWidgetFeatureEnabled({}, undefined)).toBe(true);
  });

  it("maps run status to semantic tones", () => {
    expect(runStatusTone("Completed")).toBe("success");
    expect(runStatusTone("Faulted")).toBe("danger");
    expect(runStatusTone("Running")).toBe("accent");
    expect(runStatusTone("Suspended")).toBe("warning");
    expect(runStatusTone(undefined)).toBe("neutral");
  });

  it("renders Get started docs links and the next step", () => {
    const host = mount(<GetStartedWidget snapshot={{ definitionExists: false, runExists: false, runDetailOpened: false }} settings={undefined} size="full" />);
    expect(host.textContent).toContain("Create a workflow");
    expect(host.textContent).toContain("Documentation");
    expect(host.textContent).toContain("New to Elsa?");
    for (const link of homeDocsLinks) {
      expect(host.querySelector(`a[href="${link.href}"]`)).not.toBeNull();
    }
    host.unmount();
  });

  it("renders quick actions, empty states, run status, and the Tasks empty state", () => {
    const host = mount(
      <div>
        <QuickActionsWidget />
        <RecentWorkflowsWidget snapshot={[]} settings={undefined} size="medium" />
        <RecentRunsWidget snapshot={[{ id: "run-1", name: "Orders", status: "Completed" }]} settings={undefined} size="medium" />
        <TasksWidget />
      </div>
    );
    expect(host.textContent).toContain("Create a workflow");
    expect(host.textContent).toContain("Open the designer");
    expect(host.textContent).toContain("No workflows yet");
    expect(host.textContent).toContain("Orders");
    expect(host.textContent).toContain("Completed");
    expect(host.querySelector('[data-tone="success"]')?.textContent).toBe("Completed");
    expect(host.textContent).toContain("Assigned work lives in Tasks.");
    expect(host.textContent).toContain("Open tasks");
    host.unmount();
  });
});

function mount(node: React.ReactElement) {
  const container = document.createElement("div");
  document.body.append(container);
  const root = createRoot(container);
  flushSync(() => root.render(node));
  return {
    textContent: container.textContent,
    querySelector: (selector: string) => container.querySelector(selector),
    unmount() {
      flushSync(() => root.unmount());
      container.remove();
    }
  };
}
