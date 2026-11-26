import React, { useEffect } from "react";
import { createRoot, type Root } from "react-dom/client";
import { flushSync } from "react-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { StudioWorkspaceTabProvider, type StudioWeaverRegistry, type StudioEndpointContext, type StudioWorkspaceTabHandle } from "@elsa-workflows/studio-sdk";
import { WorkflowInstanceDetailsPage, WorkflowInstancesPage } from "../workflow-editor/pages";
import {
  clampRunDetailInspectorWidth,
  getRunDetailInspectorMaxWidth,
  getRunDetailLayoutMode,
  useRunDetailLayout
} from "../workflow-editor/useRunDetailLayout";

vi.mock("../workflow-editor/WorkflowInstances", () => ({
  WorkflowInstances: () => <div>Runs list</div>,
  WorkflowInstanceDetailsWorkbench: ({ initialInspectorTab, initialActivityExecutionId, initialReturnTo, initialHistorySearch, onRunTitleChange }: {
    initialInspectorTab?: string;
    initialActivityExecutionId?: string | null;
    initialReturnTo?: string | null;
    initialHistorySearch?: string;
    onRunTitleChange?(title: string): void;
  }) => {
    useEffect(() => {
      onRunTitleChange?.("Run · Invoice settlement");
    }, [onRunTitleChange]);
    return <>
      <div className="wf-detail-header" role="group" aria-label="Run workspace actions">
        <button type="button">Runs</button>
        <h2 className="wf-detail-header__title" title="Run · Invoice settlement">Run · Invoice settlement</h2>
        <button type="button" aria-label="Refresh execution">Refresh</button>
      </div>
      <div data-initial-inspector-tab={initialInspectorTab} data-initial-activity-execution-id={initialActivityExecutionId ?? ""} data-initial-return-to={initialReturnTo ?? ""} data-initial-history-search={initialHistorySearch}>Run workbench</div>
    </>;
  }
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

  it("reserves the timeline and both resize handles when sizing a docked inspector", () => {
    expect(getRunDetailInspectorMaxWidth(1400, 320)).toBe(580);
    expect(clampRunDetailInspectorWidth(640, 1400, 320)).toBe(580);
    expect(getRunDetailInspectorMaxWidth(1400, 42)).toBe(640);
  });

  it("docks the timeline only when the measured workbench has room for three columns", async () => {
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

    flushSync(() => resize?.(1400));
    await vi.waitFor(() => expect(container.firstElementChild?.getAttribute("data-timeline-docked")).toBe("true"));
    expect(container.firstElementChild?.getAttribute("data-timeline-width")).toBe("320");
    expect(container.firstElementChild?.getAttribute("data-inspector-max-width")).toBe("580");

    flushSync(() => resize?.(1100));
    await vi.waitFor(() => expect(container.firstElementChild?.getAttribute("data-timeline-docked")).toBe("false"));
  });

  it("persists timeline collapse and supports keyboard resizing", async () => {
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
    flushSync(() => resize?.(1400));
    await vi.waitFor(() => expect(container.firstElementChild?.getAttribute("data-timeline-docked")).toBe("true"));

    flushSync(() => container.querySelector<HTMLButtonElement>("[data-action='toggle-timeline']")?.click());
    expect(container.firstElementChild?.getAttribute("data-timeline-collapsed")).toBe("true");
    expect(container.firstElementChild?.getAttribute("data-timeline-width")).toBe("42");
    expect(window.localStorage.getItem("elsa-studio-run-detail-timeline-collapsed")).toBe("true");
    flushSync(() => container.querySelector<HTMLButtonElement>("[data-action='expand-timeline']")?.click());
    expect(container.firstElementChild?.getAttribute("data-timeline-collapsed")).toBe("false");

    const resizeHandle = container.querySelector<HTMLDivElement>("[data-action='resize-timeline']");
    flushSync(() => resizeHandle?.dispatchEvent(new KeyboardEvent("keydown", { key: "ArrowRight", bubbles: true })));
    expect(container.firstElementChild?.getAttribute("data-timeline-width")).toBe("336");
    flushSync(() => resizeHandle?.dispatchEvent(new KeyboardEvent("keydown", { key: "Home", bubbles: true })));
    expect(container.firstElementChild?.getAttribute("data-timeline-width")).toBe("260");
    flushSync(() => resizeHandle?.dispatchEvent(new KeyboardEvent("keydown", { key: "End", bubbles: true })));
    expect(container.firstElementChild?.getAttribute("data-timeline-width")).toBe("480");
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
        ai={{} as StudioWeaverRegistry}
        navigate={() => {}}
      />
    ));

    await vi.waitFor(() => expect(container.textContent).toContain("Run workbench"));
    const page = container.querySelector(".wf-page--run-workbench");
    expect(page).toBeTruthy();
    expect(page?.getAttribute("data-studio-workspace-backdrop")).toBe("none");
    expect(page?.querySelector(":scope > .studio-page-header")).toBeNull();
    await vi.waitFor(() => expect(page?.querySelector(".wf-detail-header h2")?.textContent).toBe("Run · Invoice settlement"));
    expect(page?.querySelector(".wf-detail-header h2")?.getAttribute("title")).toBe("Run · Invoice settlement");
    expect(page?.querySelector(".wf-detail-header [aria-label='Refresh execution']")).toBeTruthy();
    expect(page?.querySelector(".wf-detail-header .studio-copyable-identifier")).toBeNull();
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
        ai={{} as StudioWeaverRegistry}
        navigate={() => {}}
      />
    ));

    await vi.waitFor(() => expect(container.querySelector("[data-initial-inspector-tab]")?.getAttribute("data-initial-inspector-tab")).toBe("issues"));

    window.history.replaceState({}, "", "/workflows/instances/run-1");
    flushSync(() => root.render(
      <WorkflowInstanceDetailsPage
        context={{} as StudioEndpointContext}
        ai={{} as StudioWeaverRegistry}
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
        ai={{} as StudioWeaverRegistry}
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
        ai={{} as StudioWeaverRegistry}
        navigate={() => {}}
      />
    ));

    await vi.waitFor(() => expect(container.querySelector("[data-initial-return-to]")?.getAttribute("data-initial-return-to")).toBe(returnTo));
    expect(container.querySelector("[data-initial-inspector-tab]")?.getAttribute("data-initial-inspector-tab")).toBe("activity");
    expect(container.querySelector("[data-initial-activity-execution-id]")?.getAttribute("data-initial-activity-execution-id")).toBe("outer-execution");
  });

  it("uses workspace navigation intents while ignoring the workbench's own address-bar synchronization", async () => {
    let tabLocation = "/workflows/instances/run-1?historyEvent=A&returnTo=%2Fruns-first";
    const tab: StudioWorkspaceTabHandle = { id: "retained-run", active: true, get location() { return tabLocation; },
      isActive: () => true, setTitle: vi.fn(), markPendingWork: vi.fn() };
    const renderPage = () => flushSync(() => root.render(<StudioWorkspaceTabProvider value={tab}>
      <WorkflowInstanceDetailsPage context={{} as StudioEndpointContext} ai={{} as StudioWeaverRegistry} navigate={() => {}} />
    </StudioWorkspaceTabProvider>));
    window.history.replaceState({}, "", tabLocation); renderPage();
    const initialSearch = () => container.querySelector("[data-initial-history-search]")?.getAttribute("data-initial-history-search");
    await vi.waitFor(() => expect(initialSearch()).toContain("historyEvent=A"));
    // A local causal/event selection writes only the address bar, leaving the host intent unchanged.
    window.history.replaceState({}, "", "/workflows/instances/run-1?historyEvent=B"); renderPage();
    expect(initialSearch()).toContain("historyEvent=A");
    // Real same-run navigation updates the retained tab location as well as the address bar.
    tabLocation = "/workflows/instances/run-1?bookmark=C&view=diagram&returnTo=%2Fruns-second";
    window.history.pushState({}, "", tabLocation); renderPage();
    expect(initialSearch()).toContain("bookmark=C");
    expect(initialSearch()).toContain("view=diagram");
    expect(container.querySelector("[data-initial-return-to]")?.getAttribute("data-initial-return-to")).toBe("/runs-second");
  });

  it("keeps the initial activity and return context for one run, then refreshes both for another run", async () => {
    const firstReturnTo = "/workflows/instances?status=Running&pageSize=10&cursor=page-two";
    const secondReturnTo = "/workflows/instances?incidentHealth=active&pageSize=25";
    const renderRunAt = (path: string) => {
      window.history.replaceState({}, "", path);
      flushSync(() => root.render(
        <WorkflowInstanceDetailsPage
          context={{} as StudioEndpointContext}
          ai={{} as StudioWeaverRegistry}
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
      data-timeline-docked={layout.timelineDocked}
      data-timeline-collapsed={layout.timelineCollapsed}
      data-timeline-width={layout.timelineWidth}
      data-inspector-max-width={layout.inspectorMaxWidth}
    >
      <button type="button" data-action="open" onClick={layout.openInspector}>Open</button>
      <button type="button" data-action="close" onClick={layout.closeInspector}>Close</button>
      <button type="button" data-action="toggle-timeline" onClick={layout.toggleTimelineCollapsed}>Toggle timeline</button>
      <button type="button" data-action="expand-timeline" onClick={layout.expandTimeline}>Show timeline</button>
      <div role="separator" tabIndex={0} data-action="resize-timeline" onKeyDown={layout.handleTimelineResizeKeyDown} />
    </div>
  );
}
