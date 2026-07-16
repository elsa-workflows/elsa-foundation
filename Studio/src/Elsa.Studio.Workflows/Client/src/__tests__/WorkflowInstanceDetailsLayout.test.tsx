import React from "react";
import { createRoot, type Root } from "react-dom/client";
import { flushSync } from "react-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { StudioAiContributionApi, StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { WorkflowInstanceDetailsPage, WorkflowInstancesPage } from "../workflow-editor/pages";
import {
  clampRunDetailInspectorWidth,
  getRunDetailInspectorMaxWidth,
  getRunDetailLayoutMode,
  useRunDetailLayout
} from "../workflow-editor/useRunDetailLayout";

vi.mock("../workflow-editor/WorkflowInstances", () => ({
  WorkflowInstances: () => <div>Runs list</div>,
  WorkflowInstanceDetailsWorkbench: () => <div>Run workbench</div>
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

  it("uses a full-bleed workbench root without changing the Runs list page", async () => {
    window.history.replaceState({}, "", "/workflows/instances/run-1");
    flushSync(() => root.render(
      <WorkflowInstanceDetailsPage
        context={{} as StudioEndpointContext}
        ai={{} as StudioAiContributionApi}
        navigate={() => {}}
      />
    ));

    await vi.waitFor(() => expect(container.textContent).toContain("Run workbench"));
    expect(container.querySelector(".wf-page--run-workbench")).toBeTruthy();

    flushSync(() => root.render(
      <WorkflowInstancesPage context={{} as StudioEndpointContext} navigate={() => {}} />
    ));
    await vi.waitFor(() => expect(container.textContent).toContain("Runs list"));
    expect(container.querySelector(".wf-page--run-workbench")).toBeNull();
    expect(container.querySelector(".wf-page")).toBeTruthy();
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
    />
  );
}
