import React from "react";
import { createRoot, type Root } from "react-dom/client";
import { flushSync } from "react-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { StudioAgentRegistry, StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { WorkflowInstanceDetailsPage, WorkflowInstancesPage } from "../workflow-editor/pages";
import {
  clampRunDetailInspectorWidth,
  getRunDetailInspectorMaxWidth,
  getRunDetailLayoutMode,
  useRunDetailLayout
} from "../workflow-editor/useRunDetailLayout";

vi.mock("../workflow-editor/WorkflowInstances", () => ({
  WorkflowInstances: () => <div>Runs list</div>,
  WorkflowInstanceDetailsWorkbench: ({ initialInspectorTab, initialActivityExecutionId, initialReturnTo }: { initialInspectorTab?: string; initialActivityExecutionId?: string | null; initialReturnTo?: string | null }) =>
    <div data-initial-inspector-tab={initialInspectorTab} data-initial-activity-execution-id={initialActivityExecutionId ?? ""} data-initial-return-to={initialReturnTo ?? ""}>Run workbench</div>
}));

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(() => {
  flushSync(() => root.unmount());
  container.remove();
  window.history.replaceState({}, "", "/");
  window.localStorage.clear();
  vi.unstubAllGlobals();
});

describe("Run detail page layout", () => {
  it("selects responsive modes from the workbench width at the exact boundaries", () => {
    expect(getRunDetailLayoutMode(829)).toBe("medium");
    expect(getRunDetailLayoutMode(830)).toBe("desktop");
    expect(getRunDetailLayoutMode(479)).toBe("narrow");
    expect(getRunDetailLayoutMode(480)).toBe("medium");
  });

  it("keeps a 480px canvas while clamping the desktop inspector", () => {
    expect(getRunDetailInspectorMaxWidth(830)).toBe(340);
    expect(clampRunDetailInspectorWidth(400, 830)).toBe(340);
    expect(getRunDetailInspectorMaxWidth(890)).toBe(400);
    expect(clampRunDetailInspectorWidth(400, 890)).toBe(400);
    expect(getRunDetailInspectorMaxWidth(2000)).toBe(640);
    expect(clampRunDetailInspectorWidth(900, 2000)).toBe(640);
  });

  it("observes the workbench container and exposes the inspector for activity selection", async () => {
    let resize: ((width: number) => void) | undefined;
    vi.stubGlobal("ResizeObserver", class {
      constructor(callback: ResizeObserverCallback) {
        resize = width => callback([{ contentRect: { width } } as ResizeObserverEntry], this as unknown as ResizeObserver);
      }
      observe() {}
      disconnect() {}
    });

    flushSync(() => root.render(<RunLayoutHarness selectedActivityId="activity-1" />));
    await vi.waitFor(() => expect(resize).toBeTypeOf("function"));

    flushSync(() => resize?.(600));
    await vi.waitFor(() => expect(container.firstElementChild?.getAttribute("data-mode")).toBe("medium"));
    await vi.waitFor(() => expect(container.firstElementChild?.getAttribute("data-drawer-open")).toBe("true"));

    flushSync(() => resize?.(479));
    await vi.waitFor(() => expect(container.firstElementChild?.getAttribute("data-mode")).toBe("narrow"));
    await vi.waitFor(() => expect(container.firstElementChild?.getAttribute("data-narrow-view")).toBe("inspector"));

    flushSync(() => root.render(<RunLayoutHarness selectedActivityId={null} />));
    await vi.waitFor(() => expect(container.firstElementChild?.getAttribute("data-narrow-view")).toBe("canvas"));
  });

  it("keeps an initial Issues open request through responsive measurement and honors a later close", async () => {
    let resize: ((width: number) => void) | undefined;
    vi.stubGlobal("ResizeObserver", class {
      constructor(callback: ResizeObserverCallback) {
        resize = width => callback([{ contentRect: { width } } as ResizeObserverEntry], this as unknown as ResizeObserver);
      }
      observe() {}
      disconnect() {}
    });

    flushSync(() => root.render(<RunLayoutHarness selectedActivityId={null} />));
    await vi.waitFor(() => expect(resize).toBeTypeOf("function"));
    flushSync(() => container.querySelector<HTMLButtonElement>("[data-action='open']")?.click());
    expect(container.firstElementChild?.getAttribute("data-layout-intent")).toBe("inspector");

    flushSync(() => resize?.(390));
    await vi.waitFor(() => expect(container.firstElementChild?.getAttribute("data-narrow-view")).toBe("inspector"));

    flushSync(() => container.querySelector<HTMLButtonElement>("[data-action='close']")?.click());
    await vi.waitFor(() => expect(container.firstElementChild?.getAttribute("data-narrow-view")).toBe("canvas"));
    flushSync(() => resize?.(390));
    expect(container.firstElementChild?.getAttribute("data-narrow-view")).toBe("canvas");
  });

  it("uses a full-bleed workbench root without changing the Runs list page", async () => {
    window.history.replaceState({}, "", "/workflows/instances/run-1");
    flushSync(() => root.render(
      <WorkflowInstanceDetailsPage
        context={{} as StudioEndpointContext}
        ai={{} as StudioAgentRegistry}
        navigate={() => {}}
      />
    ));

    await vi.waitFor(() => expect(container.textContent).toContain("Run workbench"));
    const page = container.querySelector(".wf-page--run-workbench");
    expect(page).toBeTruthy();
    expect(page?.querySelector(":scope > .studio-page-header")).toBeTruthy();
    expect(page?.querySelector(".wf-status-line")).toBeNull();
    expect(page?.querySelector(".studio-result-state[data-tone='danger']")).toBeNull();
    expect(container.querySelector("[data-initial-inspector-tab]")?.getAttribute("data-initial-inspector-tab")).toBe("timeline");

    flushSync(() => root.render(
      <WorkflowInstancesPage context={{} as StudioEndpointContext} navigate={() => {}} />
    ));
    await vi.waitFor(() => expect(container.textContent).toContain("Runs list"));
    expect(container.querySelector(".wf-page--run-workbench")).toBeNull();
    expect(container.querySelector(".wf-page")).toBeTruthy();
  });

  it("opens the Issues tab when incident review supplies a tab query", async () => {
    window.history.replaceState({}, "", "/workflows/instances/run-1?tab=issues");
    flushSync(() => root.render(
      <WorkflowInstanceDetailsPage
        context={{} as StudioEndpointContext}
        ai={{} as StudioAgentRegistry}
        navigate={() => {}}
      />
    ));

    await vi.waitFor(() => expect(container.querySelector("[data-initial-inspector-tab]")?.getAttribute("data-initial-inspector-tab")).toBe("issues"));

    window.history.replaceState({}, "", "/workflows/instances/run-1");
    flushSync(() => root.render(
      <WorkflowInstanceDetailsPage
        context={{} as StudioEndpointContext}
        ai={{} as StudioAgentRegistry}
        navigate={() => {}}
      />
    ));
    await vi.waitFor(() => expect(container.querySelector("[data-initial-inspector-tab]")?.getAttribute("data-initial-inspector-tab")).toBe("timeline"));
  });

  it("opens the exact Activity tab for a run deep link with an activity execution", async () => {
    window.history.replaceState({}, "", "/workflows/instances/run-1?activityExecutionId=outer-execution");
    flushSync(() => root.render(
      <WorkflowInstanceDetailsPage
        context={{} as StudioEndpointContext}
        ai={{} as StudioAgentRegistry}
        navigate={() => {}}
      />
    ));

    await vi.waitFor(() => expect(container.querySelector("[data-initial-inspector-tab]")?.getAttribute("data-initial-inspector-tab")).toBe("activity"));
    expect(container.querySelector("[data-initial-activity-execution-id]")?.getAttribute("data-initial-activity-execution-id")).toBe("outer-execution");
  });

  it("preserves return context alongside an initial detail tab and activity selection", async () => {
    const returnTo = "/workflows/instances?status=Running&pageSize=10&cursor=page-two";
    window.history.replaceState({}, "", `/workflows/instances/run-1?tab=activity&activityExecutionId=outer-execution&returnTo=${encodeURIComponent(returnTo)}`);
    flushSync(() => root.render(
      <WorkflowInstanceDetailsPage
        context={{} as StudioEndpointContext}
        ai={{} as StudioAgentRegistry}
        navigate={() => {}}
      />
    ));

    await vi.waitFor(() => expect(container.querySelector("[data-initial-return-to]")?.getAttribute("data-initial-return-to")).toBe(returnTo));
    expect(container.querySelector("[data-initial-inspector-tab]")?.getAttribute("data-initial-inspector-tab")).toBe("activity");
    expect(container.querySelector("[data-initial-activity-execution-id]")?.getAttribute("data-initial-activity-execution-id")).toBe("outer-execution");
  });

  it("keeps the initial activity and return context for one run, then refreshes both for another run", async () => {
    const firstReturnTo = "/workflows/instances?status=Running&pageSize=10&cursor=page-two";
    const secondReturnTo = "/workflows/instances?incidentHealth=active&pageSize=25";
    const renderRunAt = (path: string) => {
      window.history.replaceState({}, "", path);
      flushSync(() => root.render(
        <WorkflowInstanceDetailsPage
          context={{} as StudioEndpointContext}
          ai={{} as StudioAgentRegistry}
          navigate={() => {}}
        />
      ));
    };
    const readInitial = (attribute: string) => container.querySelector(`[${attribute}]`)?.getAttribute(attribute);

    renderRunAt(`/workflows/instances/run-1?${new URLSearchParams({
      tab: "activity",
      activityExecutionId: "outer-execution",
      returnTo: firstReturnTo
    })}`);
    await vi.waitFor(() => {
      expect(readInitial("data-initial-activity-execution-id")).toBe("outer-execution");
      expect(readInitial("data-initial-return-to")).toBe(firstReturnTo);
    });

    renderRunAt(`/workflows/instances/run-1?${new URLSearchParams({
      tab: "activity",
      activityExecutionId: "later-selection"
    })}`);
    expect(readInitial("data-initial-activity-execution-id")).toBe("outer-execution");
    expect(readInitial("data-initial-return-to")).toBe(firstReturnTo);

    renderRunAt(`/workflows/instances/run-2?${new URLSearchParams({
      tab: "activity",
      activityExecutionId: "next-run-selection",
      returnTo: secondReturnTo
    })}`);
    expect(readInitial("data-initial-activity-execution-id")).toBe("next-run-selection");
    expect(readInitial("data-initial-return-to")).toBe(secondReturnTo);
  });
});

function RunLayoutHarness({ selectedActivityId }: { selectedActivityId: string | null }) {
  const layout = useRunDetailLayout({ selectedActivityId });
  return (
    <div
      ref={layout.containerRef}
      data-mode={layout.mode}
      data-drawer-open={layout.mediumDrawerOpen}
      data-narrow-view={layout.narrowView}
      data-layout-intent={layout.layoutIntent}
    >
      <button type="button" data-action="open" onClick={layout.openInspector}>Open</button>
      <button type="button" data-action="close" onClick={layout.closeInspector}>Close</button>
    </div>
  );
}
