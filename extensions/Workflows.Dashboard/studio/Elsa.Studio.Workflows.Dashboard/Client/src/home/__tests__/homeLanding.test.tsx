import React from "react";
import { createRoot } from "react-dom/client";
import { flushSync } from "react-dom";
import { describe, expect, it } from "vitest";
import { homeWidgetIds } from "@elsa-workflows/studio-sdk";
import { GetStartedWidget, homeDocsLinks } from "../GetStartedWidget";
import { QuickActionsWidget, RecentRunsWidget, RecentWorkflowsWidget, TasksWidget } from "../HomeListWidget";
import { runStatusTone } from "../homeData";
import { registerWorkflowHome } from "../registerWorkflowHome";
import { register } from "../../module";
import { stubApi } from "./testSupport";

describe("Home default landing", () => {
  it("registers the workflow task group and the core workflow widgets with the module", () => {
    const api = stubApi();
    register(api);
    expect(api.homeSections.list().map(section => [section.id, section.moduleId, section.requiresFeature])).toEqual([
      ["workflows.tasks", "Elsa.Studio.Workflows.Dashboard", "workflows"]
    ]);
    expect(api.homeWidgets.list().map(widget => widget.id)).toEqual([
      homeWidgetIds.getStarted,
      homeWidgetIds.quickActions,
      homeWidgetIds.recentWorkflows,
      homeWidgetIds.recentRuns,
      homeWidgetIds.needsAttention,
      homeWidgetIds.tasks,
      "workflows.portfolio",
      "workflows.run-health"
    ]);
    expect(api.homeWidgets.list().every(widget => widget.moduleId === "Elsa.Studio.Workflows.Dashboard")).toBe(true);
  });

  it("marks Get started complete only when every step is done", () => {
    const api = stubApi();
    registerWorkflowHome(api);
    const getStarted = api.homeWidgets.list().find(widget => widget.id === homeWidgetIds.getStarted)!;
    expect(getStarted.isComplete?.({ definitionExists: true, runExists: true, runDetailOpened: false })).toBe(false);
    expect(getStarted.isComplete?.({ definitionExists: true, runExists: true, runDetailOpened: true })).toBe(true);
    expect(getStarted.isComplete?.(undefined)).toBe(false);
  });

  it("registers Tasks as a user-tasks inbox entry point without a data loader", () => {
    const api = stubApi();
    registerWorkflowHome(api);
    const tasks = api.homeWidgets.list().find(widget => widget.id === "home.tasks");
    expect(tasks?.requiresFeature).toBe("user-tasks");
    expect(tasks?.load).toBeUndefined();
    expect(tasks?.description).toMatch(/tasks inbox/i);
    expect(tasks?.description).not.toMatch(/when user tasks are enabled/i);
    expect(tasks?.description).not.toMatch(/assigned tasks appear here/i);
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
    expect(homeDocsLinks.map(link => link.href)).toEqual([
      "https://v4.elsaworkflows.io/essentials/quickstart",
      "https://v4.elsaworkflows.io/essentials/first-workflow",
      "https://v4.elsaworkflows.io/studio"
    ]);
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
    expect(host.querySelector('.home-record-trailing [data-tone="success"]')?.textContent).toBe("Completed");
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
