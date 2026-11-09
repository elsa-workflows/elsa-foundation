import React from "react";
import { createRoot, type Root } from "react-dom/client";
import { flushSync } from "react-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  workflowInspectorCollapsedStorageKey,
  workflowInspectorWidthStorageKey,
  workflowPaletteCollapsedStorageKey,
  workflowPaletteWidthStorageKey
} from "../workflow-editor/constants";
import { resolveAdaptiveSidePanelLayout, useSidePanelLayout } from "../workflow-editor/useSidePanelLayout";

type Layout = ReturnType<typeof useSidePanelLayout>;

let container: HTMLDivElement;
let root: Root;
let resize: ((width: number) => void) | undefined;
let layout: Layout;

beforeEach(() => {
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
  resize = undefined;
  vi.stubGlobal("ResizeObserver", class {
    constructor(callback: ResizeObserverCallback) {
      resize = width => callback([{ contentRect: { width } } as ResizeObserverEntry], this as unknown as ResizeObserver);
    }
    observe() {}
    disconnect() {}
  });
});

afterEach(() => {
  flushSync(() => root.unmount());
  container.remove();
  window.localStorage.clear();
  vi.unstubAllGlobals();
});

describe("adaptive workflow side-panel layout", () => {
  it("constrains saved maximum widths at 1280px and restores them at 1440px without rewriting preferences", async () => {
    window.localStorage.setItem(workflowPaletteWidthStorageKey, "460");
    window.localStorage.setItem(workflowInspectorWidthStorageKey, "560");
    renderLayout();
    await vi.waitFor(() => expect(resize).toBeTypeOf("function"));

    flushSync(() => resize?.(1280));
    await vi.waitFor(() => expect(layout.editorBodyStyle["--wf-inspector-width"]).toBe("498px"));
    expect(layout.editorBodyStyle["--wf-palette-width"]).toBe("402px");
    expect(layout.paletteWidth + layout.inspectorWidth + 20 + 360).toBeLessThanOrEqual(1280);
    expect(window.localStorage.getItem(workflowPaletteWidthStorageKey)).toBe("460");
    expect(window.localStorage.getItem(workflowInspectorWidthStorageKey)).toBe("560");

    flushSync(() => resize?.(1440));
    await vi.waitFor(() => expect(layout.editorBodyStyle["--wf-inspector-width"]).toBe("560px"));
    expect(layout.editorBodyStyle["--wf-palette-width"]).toBe("460px");
    expect(layout.paletteWidth + layout.inspectorWidth + 20 + 360).toBeLessThanOrEqual(1440);
    expect(window.localStorage.getItem(workflowPaletteWidthStorageKey)).toBe("460");
    expect(window.localStorage.getItem(workflowInspectorWidthStorageKey)).toBe("560");

    flushSync(() => resize?.(1280));
    await vi.waitFor(() => expect(layout.editorBodyStyle["--wf-inspector-width"]).toBe("498px"));
    expect(window.localStorage.getItem(workflowPaletteWidthStorageKey)).toBe("460");
    expect(window.localStorage.getItem(workflowInspectorWidthStorageKey)).toBe("560");

    flushSync(() => layout.handleSidePanelResizeKeyDown("inspector", {
      key: "ArrowLeft",
      preventDefault: vi.fn()
    } as unknown as React.KeyboardEvent<HTMLDivElement>));
    expect(layout.editorBodyStyle["--wf-inspector-width"]).toBe("514px");
    expect(layout.editorBodyStyle["--wf-palette-width"]).toBe("386px");
    expect(window.localStorage.getItem(workflowInspectorWidthStorageKey)).toBe("514");

    flushSync(() => resize?.(1440));
    await vi.waitFor(() => expect(layout.editorBodyStyle["--wf-inspector-width"]).toBe("514px"));
    expect(layout.editorBodyStyle["--wf-palette-width"]).toBe("460px");
    expect(window.localStorage.getItem(workflowPaletteWidthStorageKey)).toBe("460");
  });

  it("does not reserve palette width or a palette resize handle when the palette is absent", async () => {
    window.localStorage.setItem(workflowPaletteWidthStorageKey, "460");
    window.localStorage.setItem(workflowInspectorWidthStorageKey, "560");
    renderLayout(false);
    await vi.waitFor(() => expect(resize).toBeTypeOf("function"));

    flushSync(() => resize?.(1204));
    await vi.waitFor(() => expect(layout.editorBodyStyle["--wf-inspector-width"]).toBe("560px"));
    expect(layout.paletteWidth).toBe(0);
    expect(layout.paletteMinimumWidth).toBe(0);
    expect(layout.inspectorWidth).toBe(560);
    expect(layout.inspectorMinimumWidth).toBe(260);
    expect(layout.paletteWidth + layout.inspectorWidth + 10 + 360).toBeLessThanOrEqual(1204);
  });

  it("collapses the palette to a rail when both minimum panels no longer fit and lets the user temporarily expand either side", async () => {
    window.localStorage.setItem(workflowPaletteWidthStorageKey, "460");
    window.localStorage.setItem(workflowInspectorWidthStorageKey, "560");
    renderLayout();
    await vi.waitFor(() => expect(resize).toBeTypeOf("function"));

    flushSync(() => resize?.(700));
    await vi.waitFor(() => expect(container.firstElementChild?.className).toContain("palette-collapsed"));
    expect(layout.paletteWidth).toBe(42);
    expect(layout.inspectorWidth).toBe(378);
    expect(layout.editorBodyStyle["--wf-palette-width"]).toBe("42px");
    expect(layout.editorBodyStyle["--wf-inspector-width"]).toBe("378px");
    expect(layout.paletteWidth + layout.inspectorWidth + 20 + 260).toBeLessThanOrEqual(700);

    flushSync(() => layout.toggleSidePanelCollapsed("palette"));
    expect(container.firstElementChild?.className).toContain("inspector-collapsed");
    expect(container.firstElementChild?.className).not.toContain("palette-collapsed");
    expect(layout.paletteWidth).toBe(378);
    expect(layout.inspectorWidth).toBe(42);

    flushSync(() => layout.toggleSidePanelCollapsed("inspector"));
    expect(container.firstElementChild?.className).toContain("palette-collapsed");
    expect(container.firstElementChild?.className).not.toContain("inspector-collapsed");
    expect(layout.paletteWidth + layout.inspectorWidth + 20 + 260).toBeLessThanOrEqual(700);
    expect(window.localStorage.getItem(workflowPaletteCollapsedStorageKey)).toBe("false");
    expect(window.localStorage.getItem(workflowInspectorCollapsedStorageKey)).toBe("false");

    flushSync(() => layout.toggleSidePanelCollapsed("inspector"));
    expect(container.firstElementChild?.className).not.toContain("palette-collapsed");
    expect(container.firstElementChild?.className).toContain("inspector-collapsed");
    expect(window.localStorage.getItem(workflowInspectorCollapsedStorageKey)).toBe("true");
    expect(window.localStorage.getItem(workflowPaletteWidthStorageKey)).toBe("460");
    expect(window.localStorage.getItem(workflowInspectorWidthStorageKey)).toBe("560");
  });

  it("keeps a constrained panel steady on pointer down and starts the first drag from its rendered width", async () => {
    window.localStorage.setItem(workflowPaletteWidthStorageKey, "460");
    window.localStorage.setItem(workflowInspectorWidthStorageKey, "560");
    renderLayout();
    await vi.waitFor(() => expect(resize).toBeTypeOf("function"));

    flushSync(() => resize?.(1280));
    await vi.waitFor(() => expect(layout.editorBodyStyle["--wf-inspector-width"]).toBe("498px"));

    flushSync(() => layout.startSidePanelResize("inspector", {
      clientX: 500,
      preventDefault: vi.fn()
    } as unknown as React.PointerEvent<HTMLDivElement>));
    expect(layout.editorBodyStyle["--wf-inspector-width"]).toBe("498px");
    expect(layout.editorBodyStyle["--wf-palette-width"]).toBe("402px");

    flushSync(() => window.dispatchEvent(new MouseEvent("pointermove", { clientX: 484 })));
    expect(layout.editorBodyStyle["--wf-inspector-width"]).toBe("514px");
    expect(layout.editorBodyStyle["--wf-palette-width"]).toBe("386px");
    expect(window.localStorage.getItem(workflowInspectorWidthStorageKey)).toBe("514");
    window.dispatchEvent(new Event("pointerup"));
  });

  it("keeps a temporarily expanded compact palette open during keyboard and pointer resize", async () => {
    window.localStorage.setItem(workflowPaletteWidthStorageKey, "460");
    window.localStorage.setItem(workflowInspectorWidthStorageKey, "560");
    renderLayout();
    await vi.waitFor(() => expect(resize).toBeTypeOf("function"));

    flushSync(() => resize?.(700));
    flushSync(() => layout.toggleSidePanelCollapsed("palette"));
    expect(layout.editorBodyClassName).not.toContain("palette-collapsed");
    expect(layout.inspectorCollapsed).toBe(true);
    expect(layout.paletteWidth).toBe(378);

    flushSync(() => layout.handleSidePanelResizeKeyDown("palette", {
      key: "ArrowLeft",
      preventDefault: vi.fn()
    } as unknown as React.KeyboardEvent<HTMLDivElement>));
    expect(layout.editorBodyClassName).not.toContain("palette-collapsed");
    expect(layout.paletteWidth).toBe(362);

    flushSync(() => layout.startSidePanelResize("palette", {
      clientX: 500,
      preventDefault: vi.fn()
    } as unknown as React.PointerEvent<HTMLDivElement>));
    expect(layout.editorBodyClassName).not.toContain("palette-collapsed");
    flushSync(() => window.dispatchEvent(new MouseEvent("pointermove", { clientX: 508 })));
    expect(layout.editorBodyClassName).not.toContain("palette-collapsed");
    expect(layout.paletteWidth).toBe(370);
    expect(layout.inspectorCollapsed).toBe(true);
    window.dispatchEvent(new Event("pointerup"));
  });

  it("opens the inspector for programmatic actions while the compact palette is selected", async () => {
    renderLayout();
    await vi.waitFor(() => expect(resize).toBeTypeOf("function"));
    flushSync(() => resize?.(700));
    flushSync(() => layout.toggleSidePanelCollapsed("palette"));
    expect(layout.inspectorExpanded).toBe(false);

    flushSync(() => layout.setInspectorCollapsed(false));
    expect(layout.inspectorExpanded).toBe(true);
    expect(layout.paletteCollapsed).toBe(true);
  });

  it("leaves enough room for both preferred panels at a standard desktop width", () => {
    const resolved = resolveAdaptiveSidePanelLayout({
      containerWidth: 1440,
      preferredPaletteWidth: 460,
      preferredInspectorWidth: 560,
      paletteCollapsed: false,
      inspectorCollapsed: false,
      maximizedSidePanel: null,
      responsiveExpandedSide: null
    });

    expect(resolved).toMatchObject({
      paletteWidth: 460,
      inspectorWidth: 560,
      paletteCollapsed: false,
      inspectorCollapsed: false,
      autoCollapsePalette: false
    });
  });

  it("preserves the compact inspector minimum by reducing canvas reserve only when needed", () => {
    const resolve = (containerWidth: number) => resolveAdaptiveSidePanelLayout({
      containerWidth,
      preferredPaletteWidth: 460,
      preferredInspectorWidth: 560,
      paletteCollapsed: false,
      inspectorCollapsed: false,
      maximizedSidePanel: null,
      responsiveExpandedSide: null
    });

    const compact = resolve(500);
    expect(compact).toMatchObject({
      paletteWidth: 42,
      inspectorWidth: 260,
      paletteCollapsed: true,
      inspectorCollapsed: false,
      paletteMinimumWidth: 42,
      inspectorMinimumWidth: 260
    });
    expect(500 - compact.paletteWidth - compact.inspectorWidth - 20).toBe(178);

    const tooNarrow = resolve(320);
    expect(tooNarrow.paletteWidth).toBe(42);
    expect(tooNarrow.inspectorWidth).toBe(258);
    expect(tooNarrow.inspectorMinimumWidth).toBe(tooNarrow.inspectorWidth);
    expect(tooNarrow.inspectorMinimumWidth).toBeLessThanOrEqual(tooNarrow.inspectorWidth);
  });
});

function renderLayout(palettePresent = true) {
  flushSync(() => root.render(<LayoutHarness palettePresent={palettePresent} />));
}

function LayoutHarness({ palettePresent }: { palettePresent: boolean }) {
  layout = useSidePanelLayout(undefined, { palettePresent });
  return <div ref={layout.containerRef} className={layout.editorBodyClassName} style={layout.editorBodyStyle} />;
}
