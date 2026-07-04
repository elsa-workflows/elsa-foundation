import React, { useCallback, useEffect, useState } from "react";
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

// Owns the palette/inspector width, collapse and maximize state, their persistence, and the
// pointer/keyboard resize handlers. Lifted out of WorkflowEditor so the editor no longer carries the
// five side-panel useStates and their sync effects.
export function useSidePanelLayout() {
  const [paletteWidth, setPaletteWidth] = useState(() => readStoredNumber(workflowPaletteWidthStorageKey, defaultPaletteWidth, minPaletteWidth, maxPaletteWidth));
  const [inspectorWidth, setInspectorWidth] = useState(() => readStoredNumber(workflowInspectorWidthStorageKey, defaultInspectorWidth, minInspectorWidth, maxInspectorWidth));
  const [paletteCollapsed, setPaletteCollapsed] = useState(() => readStoredBoolean(workflowPaletteCollapsedStorageKey, false));
  const [inspectorCollapsed, setInspectorCollapsed] = useState(() => readStoredBoolean(workflowInspectorCollapsedStorageKey, false));
  const [maximizedSidePanel, setMaximizedSidePanel] = useState<WorkflowSidePanel | null>(readStoredMaximizedSide);

  useEffect(() => {
    writeStoredValue(workflowPaletteWidthStorageKey, String(paletteWidth));
  }, [paletteWidth]);

  useEffect(() => {
    writeStoredValue(workflowInspectorWidthStorageKey, String(inspectorWidth));
  }, [inspectorWidth]);

  useEffect(() => {
    writeStoredValue(workflowPaletteCollapsedStorageKey, String(paletteCollapsed));
  }, [paletteCollapsed]);

  useEffect(() => {
    writeStoredValue(workflowInspectorCollapsedStorageKey, String(inspectorCollapsed));
  }, [inspectorCollapsed]);

  useEffect(() => {
    writeStoredValue(workflowSidePanelMaximizedStorageKey, maximizedSidePanel);
  }, [maximizedSidePanel]);

  useEffect(() => {
    if (!maximizedSidePanel) return;

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") setMaximizedSidePanel(null);
    };

    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [maximizedSidePanel]);

  const toggleSidePanelCollapsed = useCallback((side: WorkflowSidePanel) => {
    setMaximizedSidePanel(current => current === side ? null : current);
    if (side === "palette") {
      setPaletteCollapsed(current => !current);
    } else {
      setInspectorCollapsed(current => !current);
    }
  }, []);

  const toggleSidePanelMaximized = useCallback((side: WorkflowSidePanel) => {
    if (side === "palette") {
      setPaletteCollapsed(false);
    } else {
      setInspectorCollapsed(false);
    }

    setMaximizedSidePanel(current => current === side ? null : side);
  }, []);

  const resizeSidePanel = useCallback((side: WorkflowSidePanel, delta: number) => {
    setMaximizedSidePanel(null);
    if (side === "palette") {
      setPaletteCollapsed(false);
      setPaletteWidth(current => clamp(current + delta, minPaletteWidth, maxPaletteWidth));
    } else {
      setInspectorCollapsed(false);
      setInspectorWidth(current => clamp(current + delta, minInspectorWidth, maxInspectorWidth));
    }
  }, []);

  const startSidePanelResize = useCallback((side: WorkflowSidePanel, event: React.PointerEvent<HTMLDivElement>) => {
    event.preventDefault();
    setMaximizedSidePanel(null);
    if (side === "palette") {
      setPaletteCollapsed(false);
    } else {
      setInspectorCollapsed(false);
    }

    const startX = event.clientX;
    const startWidth = side === "palette" ? paletteWidth : inspectorWidth;
    const min = side === "palette" ? minPaletteWidth : minInspectorWidth;
    const max = side === "palette" ? maxPaletteWidth : maxInspectorWidth;

    document.body.classList.add("wf-side-panel-resizing");

    const handlePointerMove = (moveEvent: PointerEvent) => {
      const delta = side === "palette"
        ? moveEvent.clientX - startX
        : startX - moveEvent.clientX;
      const nextWidth = clamp(startWidth + delta, min, max);

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
  }, [inspectorWidth, paletteWidth]);

  const handleSidePanelResizeKeyDown = useCallback((side: WorkflowSidePanel, event: React.KeyboardEvent<HTMLDivElement>) => {
    if (event.key === "ArrowLeft") {
      event.preventDefault();
      resizeSidePanel(side, side === "palette" ? -sidePanelResizeStep : sidePanelResizeStep);
    } else if (event.key === "ArrowRight") {
      event.preventDefault();
      resizeSidePanel(side, side === "palette" ? sidePanelResizeStep : -sidePanelResizeStep);
    } else if (event.key === "Home") {
      event.preventDefault();
      if (side === "palette") setPaletteWidth(minPaletteWidth);
      else setInspectorWidth(minInspectorWidth);
    } else if (event.key === "End") {
      event.preventDefault();
      if (side === "palette") setPaletteWidth(maxPaletteWidth);
      else setInspectorWidth(maxInspectorWidth);
    }
  }, [resizeSidePanel]);

  const paletteExpanded = !paletteCollapsed && maximizedSidePanel !== "inspector";
  const inspectorExpanded = !inspectorCollapsed && maximizedSidePanel !== "palette";
  const editorBodyClassName = [
    "wf-editor-body",
    paletteCollapsed ? "palette-collapsed" : "",
    inspectorCollapsed ? "inspector-collapsed" : "",
    maximizedSidePanel === "palette" ? "palette-maximized" : "",
    maximizedSidePanel === "inspector" ? "inspector-maximized" : ""
  ].filter(Boolean).join(" ");
  const editorBodyStyle = {
    "--wf-palette-width": `${paletteCollapsed ? collapsedSidePanelWidth : paletteWidth}px`,
    "--wf-inspector-width": `${inspectorCollapsed ? collapsedSidePanelWidth : inspectorWidth}px`
  } as React.CSSProperties;

  return {
    paletteWidth,
    inspectorWidth,
    paletteCollapsed,
    inspectorCollapsed,
    maximizedSidePanel,
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
