import { useCallback, useEffect, useMemo, useState } from "react";
import type {
  CSSProperties,
  KeyboardEvent as ReactKeyboardEvent,
  PointerEvent as ReactPointerEvent,
  RefCallback
} from "react";
import { readStoredBoolean, readStoredNumber, writeStoredValue } from "./editorHelpers";

export type RunDetailLayoutMode = "desktop" | "medium" | "narrow";
export type RunDetailNarrowView = "canvas" | "inspector";
type RunDetailLayoutIntent = "automatic" | "canvas" | "inspector";

export const runDetailDesktopMinWidth = 830;
export const runDetailMediumMinWidth = 480;
export const runDetailDefaultInspectorWidth = 400;
export const runDetailMinInspectorWidth = 340;
export const runDetailMaxInspectorWidth = 640;
export const runDetailCanvasMinWidth = 480;
export const runDetailResizeHandleWidth = 10;
export const runDetailCollapsedInspectorWidth = 42;
export const runDetailInspectorResizeStep = 16;
export const runDetailTimelineDockMinWidth = 1180;
export const runDetailDefaultTimelineWidth = 320;
export const runDetailMinTimelineWidth = 260;
export const runDetailMaxTimelineWidth = 480;
export const runDetailCollapsedTimelineWidth = 42;
export const runDetailTimelineResizeStep = 16;
export const runDetailInspectorWidthStorageKey = "elsa-studio-run-detail-inspector-width";
export const runDetailInspectorCollapsedStorageKey = "elsa-studio-run-detail-inspector-collapsed";
export const runDetailInspectorMaximizedStorageKey = "elsa-studio-run-detail-inspector-maximized";
export const runDetailTimelineWidthStorageKey = "elsa-studio-run-detail-timeline-width";
export const runDetailTimelineCollapsedStorageKey = "elsa-studio-run-detail-timeline-collapsed";

export function getRunDetailLayoutMode(containerWidth: number): RunDetailLayoutMode {
  if (containerWidth >= runDetailDesktopMinWidth) return "desktop";
  if (containerWidth >= runDetailMediumMinWidth) return "medium";
  return "narrow";
}

export function getRunDetailInspectorMaxWidth(containerWidth: number, timelineWidth = 0): number {
  if (!Number.isFinite(containerWidth)) return runDetailMinInspectorWidth;

  const resizeHandles = timelineWidth > 0 ? 2 : 1;
  return Math.max(
    runDetailMinInspectorWidth,
    Math.floor(Math.min(
      runDetailMaxInspectorWidth,
      containerWidth / 2,
      containerWidth - timelineWidth - runDetailResizeHandleWidth * resizeHandles - runDetailCanvasMinWidth
    ))
  );
}

export function clampRunDetailInspectorWidth(value: number, containerWidth: number, timelineWidth = 0): number {
  const width = Number.isFinite(value) ? Math.round(value) : runDetailDefaultInspectorWidth;
  return Math.min(
    getRunDetailInspectorMaxWidth(containerWidth, timelineWidth),
    Math.max(runDetailMinInspectorWidth, width)
  );
}

export function getRunDetailTimelineMaxWidth(containerWidth: number, inspectorWidth = runDetailMinInspectorWidth): number {
  if (!Number.isFinite(containerWidth)) return runDetailMinTimelineWidth;

  return Math.max(
    runDetailMinTimelineWidth,
    Math.floor(Math.min(
      runDetailMaxTimelineWidth,
      containerWidth - runDetailCanvasMinWidth - inspectorWidth - runDetailResizeHandleWidth * 2
    ))
  );
}

export function clampRunDetailTimelineWidth(value: number, containerWidth: number, inspectorWidth = runDetailMinInspectorWidth): number {
  const width = Number.isFinite(value) ? Math.round(value) : runDetailDefaultTimelineWidth;
  return Math.min(
    getRunDetailTimelineMaxWidth(containerWidth, inspectorWidth),
    Math.max(runDetailMinTimelineWidth, width)
  );
}

export function useRunDetailLayout({ selectedActivityId }: { selectedActivityId?: string | null }) {
  const [containerElement, setContainerElement] = useState<HTMLDivElement | null>(null);
  const [containerWidth, setContainerWidth] = useState(0);
  const [preferredInspectorWidth, setPreferredInspectorWidth] = useState(() => readStoredNumber(
    runDetailInspectorWidthStorageKey,
    runDetailDefaultInspectorWidth,
    runDetailMinInspectorWidth,
    runDetailMaxInspectorWidth
  ));
  const [preferredTimelineWidth, setPreferredTimelineWidth] = useState(() => readStoredNumber(
    runDetailTimelineWidthStorageKey,
    runDetailDefaultTimelineWidth,
    runDetailMinTimelineWidth,
    runDetailMaxTimelineWidth
  ));
  const [inspectorCollapsed, setInspectorCollapsed] = useState(() => readStoredBoolean(
    runDetailInspectorCollapsedStorageKey,
    false
  ));
  const [timelineCollapsed, setTimelineCollapsed] = useState(() => readStoredBoolean(
    runDetailTimelineCollapsedStorageKey,
    false
  ));
  const [inspectorMaximized, setInspectorMaximized] = useState(() => readStoredBoolean(
    runDetailInspectorMaximizedStorageKey,
    false
  ));
  const [mediumDrawerOpen, setMediumDrawerOpen] = useState(false);
  const [narrowView, setNarrowView] = useState<RunDetailNarrowView>("canvas");
  const [layoutIntent, setLayoutIntent] = useState<RunDetailLayoutIntent>("automatic");
  const containerRef: RefCallback<HTMLDivElement> = useCallback(element => setContainerElement(element), []);
  // Keep the inspector usable during the first render/SSR-style environments until the
  // container has produced a real measurement; ResizeObserver will select the final mode.
  const mode = containerElement && containerWidth > 0 ? getRunDetailLayoutMode(containerWidth) : "desktop";
  const timelineDocked = mode === "desktop" && containerWidth >= runDetailTimelineDockMinWidth;
  const timelineMaxWidth = getRunDetailTimelineMaxWidth(containerWidth,
    inspectorCollapsed ? runDetailCollapsedInspectorWidth : preferredInspectorWidth);
  const timelineWidth = timelineCollapsed
    ? runDetailCollapsedTimelineWidth
    : Math.min(timelineMaxWidth, Math.max(runDetailMinTimelineWidth, preferredTimelineWidth));
  const inspectorMaxWidth = getRunDetailInspectorMaxWidth(containerWidth, timelineDocked ? timelineWidth : 0);
  const inspectorWidth = mode === "desktop"
    ? clampRunDetailInspectorWidth(preferredInspectorWidth, containerWidth, timelineDocked ? timelineWidth : 0)
    : preferredInspectorWidth;

  useEffect(() => {
    if (!containerElement) return;

    const measure = () => setContainerWidth(normalizeContainerWidth(containerElement.getBoundingClientRect().width));
    measure();

    if (typeof ResizeObserver === "undefined") {
      window.addEventListener("resize", measure);
      return () => window.removeEventListener("resize", measure);
    }

    const observer = new ResizeObserver(entries => {
      const entry = entries[0];
      setContainerWidth(normalizeContainerWidth(entry?.contentRect.width ?? containerElement.getBoundingClientRect().width));
    });
    observer.observe(containerElement);
    return () => observer.disconnect();
  }, [containerElement]);

  useEffect(() => {
    writeStoredValue(runDetailInspectorWidthStorageKey, String(preferredInspectorWidth));
  }, [preferredInspectorWidth]);

  useEffect(() => {
    writeStoredValue(runDetailInspectorCollapsedStorageKey, String(inspectorCollapsed));
  }, [inspectorCollapsed]);

  useEffect(() => {
    writeStoredValue(runDetailTimelineWidthStorageKey, String(preferredTimelineWidth));
  }, [preferredTimelineWidth]);

  useEffect(() => {
    writeStoredValue(runDetailTimelineCollapsedStorageKey, String(timelineCollapsed));
  }, [timelineCollapsed]);

  useEffect(() => {
    writeStoredValue(runDetailInspectorMaximizedStorageKey, String(inspectorMaximized));
  }, [inspectorMaximized]);

  const showInspectorForSelection = useCallback(() => {
    if (mode === "medium") {
      setMediumDrawerOpen(true);
    } else if (mode === "narrow") {
      setNarrowView("inspector");
    }
  }, [mode]);

  useEffect(() => {
    if (layoutIntent === "inspector" || (layoutIntent === "automatic" && selectedActivityId)) {
      showInspectorForSelection();
      return;
    }

    setMediumDrawerOpen(false);
    setNarrowView("canvas");
  }, [layoutIntent, selectedActivityId, showInspectorForSelection]);

  useEffect(() => {
    if (mode === "desktop") {
      setMediumDrawerOpen(false);
      return;
    }

    setInspectorMaximized(false);
  }, [mode]);

  useEffect(() => {
    if (!inspectorMaximized) return;

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") setInspectorMaximized(false);
    };
    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [inspectorMaximized]);

  const closeInspector = useCallback(() => {
    setLayoutIntent("canvas");
    setMediumDrawerOpen(false);
    if (mode === "narrow") setNarrowView("canvas");
  }, [mode]);

  const toggleInspectorCollapsed = useCallback(() => {
    setInspectorMaximized(false);
    const collapsed = !inspectorCollapsed;
    setLayoutIntent(collapsed ? "canvas" : "inspector");
    setInspectorCollapsed(collapsed);
  }, [inspectorCollapsed]);

  const openInspector = useCallback(() => {
    setLayoutIntent("inspector");
    setInspectorCollapsed(false);
    showInspectorForSelection();
  }, [showInspectorForSelection]);

  const showCanvasForSelection = useCallback(() => {
    setLayoutIntent("canvas");
    setInspectorMaximized(false);
    setInspectorCollapsed(false);
    setMediumDrawerOpen(false);
    setNarrowView("canvas");
  }, []);

  const toggleInspectorMaximized = useCallback(() => {
    if (mode !== "desktop") return;
    setInspectorCollapsed(false);
    setInspectorMaximized(current => !current);
  }, [mode]);

  const resizeInspector = useCallback((delta: number) => {
    if (mode !== "desktop") return;
    setInspectorMaximized(false);
    setInspectorCollapsed(false);
    setPreferredInspectorWidth(current => clampRunDetailInspectorWidth(current + delta, containerWidth, timelineDocked ? timelineWidth : 0));
  }, [containerWidth, mode, timelineDocked, timelineWidth]);

  const startInspectorResize = useCallback((event: ReactPointerEvent<HTMLDivElement>) => {
    if (mode !== "desktop") return;

    event.preventDefault();
    setInspectorMaximized(false);
    setInspectorCollapsed(false);
    const startX = event.clientX;
    const startWidth = inspectorWidth;
    document.body.classList.add("wf-side-panel-resizing");

    const handlePointerMove = (moveEvent: PointerEvent) => {
      setPreferredInspectorWidth(clampRunDetailInspectorWidth(startWidth + startX - moveEvent.clientX, containerWidth,
        timelineDocked ? timelineWidth : 0));
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
  }, [containerWidth, inspectorWidth, mode, timelineDocked, timelineWidth]);

  const handleInspectorResizeKeyDown = useCallback((event: ReactKeyboardEvent<HTMLDivElement>) => {
    if (mode !== "desktop") return;

    if (event.key === "ArrowLeft") {
      event.preventDefault();
      resizeInspector(runDetailInspectorResizeStep);
    } else if (event.key === "ArrowRight") {
      event.preventDefault();
      resizeInspector(-runDetailInspectorResizeStep);
    } else if (event.key === "Home") {
      event.preventDefault();
      setPreferredInspectorWidth(runDetailMinInspectorWidth);
    } else if (event.key === "End") {
      event.preventDefault();
      setPreferredInspectorWidth(inspectorMaxWidth);
    }
  }, [inspectorMaxWidth, mode, resizeInspector]);

  const resizeTimeline = useCallback((delta: number) => {
    if (!timelineDocked) return;
    setTimelineCollapsed(false);
    setPreferredTimelineWidth(current => clampRunDetailTimelineWidth(current + delta, containerWidth,
      inspectorCollapsed ? runDetailCollapsedInspectorWidth : preferredInspectorWidth));
  }, [containerWidth, inspectorCollapsed, preferredInspectorWidth, timelineDocked]);

  const startTimelineResize = useCallback((event: ReactPointerEvent<HTMLDivElement>) => {
    if (!timelineDocked || timelineCollapsed) return;

    event.preventDefault();
    const startX = event.clientX;
    const startWidth = timelineWidth;
    document.body.classList.add("wf-side-panel-resizing");

    const handlePointerMove = (moveEvent: PointerEvent) => {
      setPreferredTimelineWidth(clampRunDetailTimelineWidth(startWidth + moveEvent.clientX - startX, containerWidth,
        inspectorCollapsed ? runDetailCollapsedInspectorWidth : preferredInspectorWidth));
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
  }, [containerWidth, inspectorCollapsed, preferredInspectorWidth, timelineCollapsed, timelineDocked, timelineWidth]);

  const handleTimelineResizeKeyDown = useCallback((event: ReactKeyboardEvent<HTMLDivElement>) => {
    if (!timelineDocked || timelineCollapsed) return;

    if (event.key === "ArrowLeft") {
      event.preventDefault();
      resizeTimeline(-runDetailTimelineResizeStep);
    } else if (event.key === "ArrowRight") {
      event.preventDefault();
      resizeTimeline(runDetailTimelineResizeStep);
    } else if (event.key === "Home") {
      event.preventDefault();
      setPreferredTimelineWidth(runDetailMinTimelineWidth);
    } else if (event.key === "End") {
      event.preventDefault();
      setPreferredTimelineWidth(getRunDetailTimelineMaxWidth(containerWidth,
        inspectorCollapsed ? runDetailCollapsedInspectorWidth : preferredInspectorWidth));
    }
  }, [containerWidth, inspectorCollapsed, preferredInspectorWidth, resizeTimeline, timelineCollapsed, timelineDocked]);

  const toggleTimelineCollapsed = useCallback(() => setTimelineCollapsed(current => !current), []);
  const expandTimeline = useCallback(() => setTimelineCollapsed(false), []);

  const effectiveInspectorMaximized = mode === "desktop" && inspectorMaximized;
  const inspectorExpanded = mode === "desktop"
    ? !inspectorCollapsed
    : mode === "medium"
      ? mediumDrawerOpen
      : narrowView === "inspector";
  const canResizeInspector = mode === "desktop" && !inspectorCollapsed && !effectiveInspectorMaximized;
  const workbenchClassName = [
    "wf-instance-detail-workbench",
    `run-layout-${mode}`,
    timelineDocked ? "timeline-docked" : "",
    timelineDocked && timelineCollapsed ? "timeline-collapsed" : "",
    mode === "desktop" && inspectorCollapsed ? "inspector-collapsed" : "",
    effectiveInspectorMaximized ? "inspector-maximized" : "",
    mode === "medium" && mediumDrawerOpen ? "inspector-drawer-open" : "",
    mode === "narrow" ? `narrow-${narrowView}-view` : ""
  ].filter(Boolean).join(" ");
  const workbenchStyle = useMemo(() => ({
    "--wf-run-inspector-width": `${mode === "desktop" && inspectorCollapsed ? runDetailCollapsedInspectorWidth : inspectorWidth}px`,
    "--wf-run-timeline-width": `${timelineWidth}px`
  }) as CSSProperties, [inspectorCollapsed, inspectorWidth, mode, timelineWidth]);

  return {
    containerRef,
    containerWidth,
    layoutIntent,
    mode,
    timelineDocked,
    timelineCollapsed,
    timelineWidth,
    timelineMaxWidth,
    inspectorWidth,
    inspectorMaxWidth,
    inspectorCollapsed,
    inspectorMaximized: effectiveInspectorMaximized,
    inspectorExpanded,
    canResizeInspector,
    mediumDrawerOpen,
    narrowView,
    workbenchClassName,
    workbenchStyle,
    setNarrowView,
    showInspectorForSelection,
    closeInspector,
    showCanvasForSelection,
    toggleInspectorCollapsed,
    openInspector,
    toggleInspectorMaximized,
    startInspectorResize,
    handleInspectorResizeKeyDown,
    toggleTimelineCollapsed,
    expandTimeline,
    startTimelineResize,
    handleTimelineResizeKeyDown
  };
}

function normalizeContainerWidth(value: number) {
  return Number.isFinite(value) ? Math.max(0, value) : 0;
}
