import React, { useCallback, useEffect, useLayoutEffect, useState } from "react";
import type { RefCallback } from "react";
import {
  collapsedSidePanelWidth,
  defaultInspectorWidth,
  defaultPaletteWidth,
  maxInspectorWidth,
  maxPaletteWidth,
  minInspectorWidth,
  minPaletteWidth,
  sidePanelResizeStep,
  workflowInspectorCollapsedStorageKey,
  workflowInspectorWidthStorageKey,
  workflowPaletteCollapsedStorageKey,
  workflowPaletteWidthStorageKey,
  workflowSidePanelMaximizedStorageKey
} from "./constants";
import type { WorkflowSidePanel } from "./editorTypes";
import { clamp, readStoredBoolean, readStoredMaximizedSide, readStoredNumber, writeStoredValue } from "./editorHelpers";

const sidePanelResizeHandleWidth = 10;
const workflowCanvasMinimumWidth = 360;
const compactCanvasMinimumWidth = 260;

export interface SidePanelLayoutStorageKeys {
  paletteWidth: string;
  inspectorWidth: string;
  paletteCollapsed: string;
  inspectorCollapsed: string;
  maximized: string;
}

export interface SidePanelLayoutOptions {
  palettePresent?: boolean;
}

export interface AdaptiveSidePanelLayout {
  paletteWidth: number;
  inspectorWidth: number;
  paletteMinimumWidth: number;
  inspectorMinimumWidth: number;
  paletteCollapsed: boolean;
  inspectorCollapsed: boolean;
  autoCollapsePalette: boolean;
}

export type SidePanelEditorBodyStyle = React.CSSProperties & {
  "--wf-palette-width": string;
  "--wf-inspector-width": string;
};

/**
 * Resolves rendered panel sizes from the available workbench width without changing the saved
 * user preferences. When both panels no longer fit at their minimum widths, the activity palette
 * becomes a rail first so that the inspector remains available for editing. Hosts without a
 * palette allocate that width and its resize handle directly to the canvas and inspector.
 */
export function resolveAdaptiveSidePanelLayout({
  containerWidth,
  preferredPaletteWidth,
  preferredInspectorWidth,
  paletteCollapsed,
  inspectorCollapsed,
  maximizedSidePanel,
  responsiveExpandedSide,
  resizePriority,
  palettePresent = true
}: {
  containerWidth: number;
  preferredPaletteWidth: number;
  preferredInspectorWidth: number;
  paletteCollapsed: boolean;
  inspectorCollapsed: boolean;
  maximizedSidePanel: WorkflowSidePanel | null;
  responsiveExpandedSide: WorkflowSidePanel | null;
  resizePriority?: WorkflowSidePanel | null;
  palettePresent?: boolean;
}): AdaptiveSidePanelLayout {
  const measured = Number.isFinite(containerWidth) && containerWidth > 0;
  const compact = measured && containerWidth <= 900;
  const resizeHandleCount = palettePresent ? 2 : 1;
  const canvasMinimumWidth = compact
    ? compactCanvasMinimumWidth
    : workflowCanvasMinimumWidth;
  const initialPanelSpace = measured
    ? Math.max(0, containerWidth - canvasMinimumWidth - sidePanelResizeHandleWidth * resizeHandleCount)
    : Number.POSITIVE_INFINITY;
  const effectiveMaximizedSidePanel = !palettePresent && maximizedSidePanel === "palette"
    ? null
    : maximizedSidePanel;
  const wantsPalette = palettePresent && !paletteCollapsed && effectiveMaximizedSidePanel !== "inspector";
  const wantsInspector = !inspectorCollapsed && effectiveMaximizedSidePanel !== "palette";
  const canFitBothPanels = !palettePresent || !measured || initialPanelSpace >= minPaletteWidth + minInspectorWidth;
  const autoCollapsePalette = measured && wantsPalette && wantsInspector && !canFitBothPanels;
  const forcePaletteOpen = autoCollapsePalette && responsiveExpandedSide === "palette";
  const effectivePaletteCollapsed = !wantsPalette || (autoCollapsePalette && !forcePaletteOpen);
  const effectiveInspectorCollapsed = !wantsInspector || (autoCollapsePalette && forcePaletteOpen);

  if (!measured || effectiveMaximizedSidePanel) {
    return {
      paletteWidth: !palettePresent ? 0 : effectivePaletteCollapsed ? collapsedSidePanelWidth : preferredPaletteWidth,
      inspectorWidth: effectiveInspectorCollapsed ? collapsedSidePanelWidth : preferredInspectorWidth,
      paletteMinimumWidth: !palettePresent ? 0 : Math.min(minPaletteWidth, effectivePaletteCollapsed ? collapsedSidePanelWidth : preferredPaletteWidth),
      inspectorMinimumWidth: Math.min(minInspectorWidth, effectiveInspectorCollapsed ? collapsedSidePanelWidth : preferredInspectorWidth),
      paletteCollapsed: effectivePaletteCollapsed,
      inspectorCollapsed: effectiveInspectorCollapsed,
      autoCollapsePalette
    };
  }

  // On compact workbenches, retain a usable minimum for the expanded panel before giving
  // the canvas its usual 260px reserve. At widths below that combined minimum, the canvas
  // reserve can shrink to zero and the panel takes the space that remains.
  const expandedPanelMinimumWidth = (!palettePresent || effectivePaletteCollapsed ? 0 : minPaletteWidth)
    + (effectiveInspectorCollapsed ? 0 : minInspectorWidth);
  const collapsedWidth = (palettePresent && effectivePaletteCollapsed ? collapsedSidePanelWidth : 0)
    + (effectiveInspectorCollapsed ? collapsedSidePanelWidth : 0);
  const effectiveCanvasMinimumWidth = compact
    ? Math.min(
        compactCanvasMinimumWidth,
        Math.max(0, containerWidth - expandedPanelMinimumWidth - collapsedWidth - sidePanelResizeHandleWidth * resizeHandleCount)
      )
    : workflowCanvasMinimumWidth;
  const panelSpace = Math.max(0, containerWidth - effectiveCanvasMinimumWidth - sidePanelResizeHandleWidth * resizeHandleCount);
  const expandableSpace = Math.max(0, panelSpace - collapsedWidth);
  const widths = effectivePaletteCollapsed || effectiveInspectorCollapsed
    ? {
        paletteWidth: !palettePresent
          ? 0
          : effectivePaletteCollapsed
            ? collapsedSidePanelWidth
            : Math.min(preferredPaletteWidth, expandableSpace),
        inspectorWidth: effectiveInspectorCollapsed
          ? collapsedSidePanelWidth
          : Math.min(preferredInspectorWidth, expandableSpace)
      }
    : allocatePanelWidths(preferredPaletteWidth, preferredInspectorWidth, expandableSpace, resizePriority);

  return {
    ...widths,
    paletteMinimumWidth: Math.min(minPaletteWidth, widths.paletteWidth),
    inspectorMinimumWidth: Math.min(minInspectorWidth, widths.inspectorWidth),
    paletteCollapsed: effectivePaletteCollapsed,
    inspectorCollapsed: effectiveInspectorCollapsed,
    autoCollapsePalette
  };
}

function allocatePanelWidths(
  preferredPaletteWidth: number,
  preferredInspectorWidth: number,
  availableWidth: number,
  resizePriority?: WorkflowSidePanel | null
) {
  const preferredTotal = preferredPaletteWidth + preferredInspectorWidth;
  if (preferredTotal <= availableWidth) {
    return { paletteWidth: preferredPaletteWidth, inspectorWidth: preferredInspectorWidth };
  }

  const minimumTotal = minPaletteWidth + minInspectorWidth;
  if (availableWidth < minimumTotal) {
    return {
      paletteWidth: Math.min(preferredPaletteWidth, availableWidth),
      inspectorWidth: Math.min(preferredInspectorWidth, Math.max(0, availableWidth - Math.min(preferredPaletteWidth, availableWidth)))
    };
  }

  if (resizePriority === "palette") {
    const paletteWidth = Math.min(preferredPaletteWidth, availableWidth - minInspectorWidth);
    return {
      paletteWidth,
      inspectorWidth: Math.min(preferredInspectorWidth, availableWidth - paletteWidth)
    };
  }

  if (resizePriority === "inspector") {
    const inspectorWidth = Math.min(preferredInspectorWidth, availableWidth - minPaletteWidth);
    return {
      paletteWidth: Math.min(preferredPaletteWidth, availableWidth - inspectorWidth),
      inspectorWidth
    };
  }

  const paletteFlex = Math.max(0, preferredPaletteWidth - minPaletteWidth);
  const inspectorFlex = Math.max(0, preferredInspectorWidth - minInspectorWidth);
  const flexTotal = paletteFlex + inspectorFlex;
  const availableFlex = availableWidth - minimumTotal;
  if (flexTotal === 0) {
    return { paletteWidth: minPaletteWidth, inspectorWidth: minInspectorWidth };
  }

  const paletteFlexWidth = Math.floor(paletteFlex * availableFlex / flexTotal);
  return {
    paletteWidth: minPaletteWidth + paletteFlexWidth,
    inspectorWidth: minInspectorWidth + Math.min(inspectorFlex, availableFlex - paletteFlexWidth)
  };
}

const workflowSidePanelStorageKeys: SidePanelLayoutStorageKeys = {
  paletteWidth: workflowPaletteWidthStorageKey,
  inspectorWidth: workflowInspectorWidthStorageKey,
  paletteCollapsed: workflowPaletteCollapsedStorageKey,
  inspectorCollapsed: workflowInspectorCollapsedStorageKey,
  maximized: workflowSidePanelMaximizedStorageKey
};

// Owns the palette/inspector width, collapse and maximize state, their persistence, and the
// pointer/keyboard resize handlers. Lifted out of WorkflowEditor so the editor no longer carries the
// five side-panel useStates and their sync effects.
export function useSidePanelLayout(
  storageKeys: SidePanelLayoutStorageKeys = workflowSidePanelStorageKeys,
  options: SidePanelLayoutOptions = {}
) {
  const palettePresent = options.palettePresent ?? true;
  const [containerElement, setContainerElement] = useState<HTMLDivElement | null>(null);
  const [containerWidth, setContainerWidth] = useState(0);
  const [paletteWidth, setPaletteWidth] = useState(() => readStoredNumber(storageKeys.paletteWidth, defaultPaletteWidth, minPaletteWidth, maxPaletteWidth));
  const [inspectorWidth, setInspectorWidth] = useState(() => readStoredNumber(storageKeys.inspectorWidth, defaultInspectorWidth, minInspectorWidth, maxInspectorWidth));
  const [paletteCollapsed, setPaletteCollapsed] = useState(() => readStoredBoolean(storageKeys.paletteCollapsed, false));
  const [inspectorCollapsed, setInspectorCollapsed] = useState(() => readStoredBoolean(storageKeys.inspectorCollapsed, false));
  const [maximizedSidePanel, setMaximizedSidePanel] = useState<WorkflowSidePanel | null>(() => readStoredMaximizedSide(storageKeys.maximized));
  const [responsiveExpandedSide, setResponsiveExpandedSide] = useState<WorkflowSidePanel | null>(null);
  const [resizePriority, setResizePriority] = useState<WorkflowSidePanel | null>(null);
  const containerRef: RefCallback<HTMLDivElement> = useCallback(element => setContainerElement(element), []);
  const adaptiveLayout = resolveAdaptiveSidePanelLayout({
    containerWidth,
    preferredPaletteWidth: paletteWidth,
    preferredInspectorWidth: inspectorWidth,
    paletteCollapsed,
    inspectorCollapsed,
    maximizedSidePanel: !palettePresent && maximizedSidePanel === "palette" ? null : maximizedSidePanel,
    responsiveExpandedSide,
    resizePriority,
    palettePresent
  });

  useLayoutEffect(() => {
    if (!containerElement) return;

    const measure = () => {
      const width = Math.round(containerElement.getBoundingClientRect().width);
      setContainerWidth(Number.isFinite(width) ? width : 0);
    };
    measure();

    if (typeof ResizeObserver === "undefined") {
      window.addEventListener("resize", measure);
      return () => window.removeEventListener("resize", measure);
    }

    const observer = new ResizeObserver(entries => {
      const entry = entries[0];
      const width = entry?.contentRect.width ?? containerElement.getBoundingClientRect().width;
      setContainerWidth(Number.isFinite(width) ? Math.round(width) : 0);
    });
    observer.observe(containerElement);
    return () => observer.disconnect();
  }, [containerElement]);

  useEffect(() => {
    if (!adaptiveLayout.autoCollapsePalette && responsiveExpandedSide) {
      setResponsiveExpandedSide(null);
    }
  }, [adaptiveLayout.autoCollapsePalette, responsiveExpandedSide]);

  useEffect(() => {
    writeStoredValue(storageKeys.paletteWidth, String(paletteWidth));
  }, [paletteWidth, storageKeys.paletteWidth]);

  useEffect(() => {
    writeStoredValue(storageKeys.inspectorWidth, String(inspectorWidth));
  }, [inspectorWidth, storageKeys.inspectorWidth]);

  useEffect(() => {
    writeStoredValue(storageKeys.paletteCollapsed, String(paletteCollapsed));
  }, [paletteCollapsed, storageKeys.paletteCollapsed]);

  useEffect(() => {
    writeStoredValue(storageKeys.inspectorCollapsed, String(inspectorCollapsed));
  }, [inspectorCollapsed, storageKeys.inspectorCollapsed]);

  useEffect(() => {
    writeStoredValue(storageKeys.maximized, maximizedSidePanel);
  }, [maximizedSidePanel, storageKeys.maximized]);

  useEffect(() => {
    if (!maximizedSidePanel) return;

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") setMaximizedSidePanel(null);
    };

    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [maximizedSidePanel]);

  const toggleSidePanelCollapsed = useCallback((side: WorkflowSidePanel) => {
    if (adaptiveLayout.autoCollapsePalette && !paletteCollapsed && !inspectorCollapsed) {
      const sideIsAutoCollapsed = side === "palette"
        ? adaptiveLayout.paletteCollapsed
        : adaptiveLayout.inspectorCollapsed;
      if (sideIsAutoCollapsed) {
        setResponsiveExpandedSide(side);
      } else if (side === "palette") {
        setResponsiveExpandedSide(null);
      } else {
        setResponsiveExpandedSide(null);
        setInspectorCollapsed(true);
      }
      setMaximizedSidePanel(null);
      return;
    }

    setResponsiveExpandedSide(null);
    setMaximizedSidePanel(current => current === side ? null : current);
    if (side === "palette") {
      setPaletteCollapsed(current => !current);
    } else {
      setInspectorCollapsed(current => !current);
    }
  }, [
    adaptiveLayout.autoCollapsePalette,
    adaptiveLayout.inspectorCollapsed,
    adaptiveLayout.paletteCollapsed,
    inspectorCollapsed,
    paletteCollapsed
  ]);

  const toggleSidePanelMaximized = useCallback((side: WorkflowSidePanel) => {
    setResponsiveExpandedSide(null);
    if (side === "palette") {
      setPaletteCollapsed(false);
    } else {
      setInspectorCollapsed(false);
    }

    setMaximizedSidePanel(current => current === side ? null : side);
  }, []);

  const resizeSidePanel = useCallback((side: WorkflowSidePanel, delta: number) => {
    setMaximizedSidePanel(null);
    setResizePriority(side);
    if (side === "palette") {
      setPaletteCollapsed(false);
      setPaletteWidth(clamp(adaptiveLayout.paletteWidth + delta, minPaletteWidth, maxPaletteWidth));
    } else {
      setInspectorCollapsed(false);
      setInspectorWidth(clamp(adaptiveLayout.inspectorWidth + delta, minInspectorWidth, maxInspectorWidth));
    }
  }, [adaptiveLayout.inspectorWidth, adaptiveLayout.paletteWidth]);

  const startSidePanelResize = useCallback((side: WorkflowSidePanel, event: React.PointerEvent<HTMLDivElement>) => {
    event.preventDefault();
    setMaximizedSidePanel(null);
    if (side === "palette") {
      setPaletteCollapsed(false);
    } else {
      setInspectorCollapsed(false);
    }

    const startX = event.clientX;
    const startWidth = side === "palette" ? adaptiveLayout.paletteWidth : adaptiveLayout.inspectorWidth;
    const min = side === "palette" ? minPaletteWidth : minInspectorWidth;
    const max = side === "palette" ? maxPaletteWidth : maxInspectorWidth;

    document.body.classList.add("wf-side-panel-resizing");

    const handlePointerMove = (moveEvent: PointerEvent) => {
      const delta = side === "palette"
        ? moveEvent.clientX - startX
        : startX - moveEvent.clientX;
      const nextWidth = clamp(startWidth + delta, min, max);

      setResizePriority(side);
      if (side === "palette") {
        setPaletteWidth(nextWidth);
      } else {
        setInspectorWidth(nextWidth);
      }
    };

    const stopResize = () => {
      document.body.classList.remove("wf-side-panel-resizing");
      window.removeEventListener("pointermove", handlePointerMove);
      window.removeEventListener("pointerup", stopResize);
      window.removeEventListener("pointercancel", stopResize);
    };

    window.addEventListener("pointermove", handlePointerMove);
    window.addEventListener("pointerup", stopResize);
    window.addEventListener("pointercancel", stopResize);
  }, [adaptiveLayout.inspectorWidth, adaptiveLayout.paletteWidth]);

  const handleSidePanelResizeKeyDown = useCallback((side: WorkflowSidePanel, event: React.KeyboardEvent<HTMLDivElement>) => {
    if (event.key === "ArrowLeft") {
      event.preventDefault();
      resizeSidePanel(side, side === "palette" ? -sidePanelResizeStep : sidePanelResizeStep);
    } else if (event.key === "ArrowRight") {
      event.preventDefault();
      resizeSidePanel(side, side === "palette" ? sidePanelResizeStep : -sidePanelResizeStep);
    } else if (event.key === "Home") {
      event.preventDefault();
      setResizePriority(side);
      if (side === "palette") setPaletteWidth(minPaletteWidth);
      else setInspectorWidth(minInspectorWidth);
    } else if (event.key === "End") {
      event.preventDefault();
      setResizePriority(side);
      if (side === "palette") setPaletteWidth(maxPaletteWidth);
      else setInspectorWidth(maxInspectorWidth);
    }
  }, [resizeSidePanel]);

  const effectiveMaximizedSidePanel = !palettePresent && maximizedSidePanel === "palette" ? null : maximizedSidePanel;
  const paletteExpanded = palettePresent && !adaptiveLayout.paletteCollapsed && effectiveMaximizedSidePanel !== "inspector";
  const inspectorExpanded = !adaptiveLayout.inspectorCollapsed && effectiveMaximizedSidePanel !== "palette";
  const editorBodyClassName = [
    "wf-editor-body",
    adaptiveLayout.paletteCollapsed ? "palette-collapsed" : "",
    adaptiveLayout.inspectorCollapsed ? "inspector-collapsed" : "",
    effectiveMaximizedSidePanel === "palette" ? "palette-maximized" : "",
    effectiveMaximizedSidePanel === "inspector" ? "inspector-maximized" : ""
  ].filter(Boolean).join(" ");
  const editorBodyStyle: SidePanelEditorBodyStyle = {
    "--wf-palette-width": `${adaptiveLayout.paletteWidth}px`,
    "--wf-inspector-width": `${adaptiveLayout.inspectorWidth}px`
  };

  return {
    containerRef,
    paletteWidth: adaptiveLayout.paletteWidth,
    inspectorWidth: adaptiveLayout.inspectorWidth,
    paletteMinimumWidth: adaptiveLayout.paletteMinimumWidth,
    inspectorMinimumWidth: adaptiveLayout.inspectorMinimumWidth,
    paletteCollapsed: adaptiveLayout.paletteCollapsed,
    inspectorCollapsed: adaptiveLayout.inspectorCollapsed,
    maximizedSidePanel: effectiveMaximizedSidePanel,
    setInspectorCollapsed,
    paletteExpanded,
    inspectorExpanded,
    editorBodyClassName,
    editorBodyStyle,
    toggleSidePanelCollapsed,
    toggleSidePanelMaximized,
    startSidePanelResize,
    handleSidePanelResizeKeyDown
  };
}
