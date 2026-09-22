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

export const runDetailDesktopMinWidth = 830;
export const runDetailMediumMinWidth = 480;
export const runDetailDefaultInspectorWidth = 400;
export const runDetailMinInspectorWidth = 340;
export const runDetailMaxInspectorWidth = 640;
export const runDetailCanvasMinWidth = 480;
export const runDetailResizeHandleWidth = 10;
export const runDetailCollapsedInspectorWidth = 42;
export const runDetailInspectorResizeStep = 16;
export const runDetailInspectorWidthStorageKey = "elsa-studio-run-detail-inspector-width";
export const runDetailInspectorCollapsedStorageKey = "elsa-studio-run-detail-inspector-collapsed";
export const runDetailInspectorMaximizedStorageKey = "elsa-studio-run-detail-inspector-maximized";

export function getRunDetailLayoutMode(containerWidth: number): RunDetailLayoutMode {
  if (containerWidth >= runDetailDesktopMinWidth) return "desktop";
  if (containerWidth >= runDetailMediumMinWidth) return "medium";
  return "narrow";
}

export function getRunDetailInspectorMaxWidth(containerWidth: number): number {
  if (!Number.isFinite(containerWidth)) return runDetailMinInspectorWidth;

  return Math.max(
    runDetailMinInspectorWidth,
    Math.floor(Math.min(
      runDetailMaxInspectorWidth,
      containerWidth / 2,
      containerWidth - runDetailResizeHandleWidth - runDetailCanvasMinWidth
    ))
  );
}

export function clampRunDetailInspectorWidth(value: number, containerWidth: number): number {
  const width = Number.isFinite(value) ? Math.round(value) : runDetailDefaultInspectorWidth;
  return Math.min(
    getRunDetailInspectorMaxWidth(containerWidth),
    Math.max(runDetailMinInspectorWidth, width)
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
  const [inspectorCollapsed, setInspectorCollapsed] = useState(() => readStoredBoolean(
    runDetailInspectorCollapsedStorageKey,
    false
  ));
  const [inspectorMaximized, setInspectorMaximized] = useState(() => readStoredBoolean(
    runDetailInspectorMaximizedStorageKey,
    false
  ));
  const [mediumDrawerOpen, setMediumDrawerOpen] = useState(false);
  const [narrowView, setNarrowView] = useState<RunDetailNarrowView>("canvas");
  const containerRef: RefCallback<HTMLDivElement> = useCallback(element => setContainerElement(element), []);
  // Keep the inspector usable during the first render/SSR-style environments until the
  // container has produced a real measurement; ResizeObserver will select the final mode.
  const mode = containerElement && containerWidth > 0 ? getRunDetailLayoutMode(containerWidth) : "desktop";
  const inspectorMaxWidth = getRunDetailInspectorMaxWidth(containerWidth);
  const inspectorWidth = mode === "desktop"
    ? clampRunDetailInspectorWidth(preferredInspectorWidth, containerWidth)
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
    if (selectedActivityId) {
      showInspectorForSelection();
      return;
    }

    setMediumDrawerOpen(false);
    setNarrowView("canvas");
  }, [selectedActivityId, showInspectorForSelection]);

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
    setMediumDrawerOpen(false);
    if (mode === "narrow") setNarrowView("canvas");
  }, [mode]);

  const toggleInspectorCollapsed = useCallback(() => {
    setInspectorMaximized(false);
    setInspectorCollapsed(current => !current);
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
    setPreferredInspectorWidth(current => clampRunDetailInspectorWidth(current + delta, containerWidth));
  }, [containerWidth, mode]);

  const startInspectorResize = useCallback((event: ReactPointerEvent<HTMLDivElement>) => {
    if (mode !== "desktop") return;

    event.preventDefault();
    setInspectorMaximized(false);
    setInspectorCollapsed(false);
    const startX = event.clientX;
    const startWidth = inspectorWidth;
    document.body.classList.add("wf-side-panel-resizing");

    const handlePointerMove = (moveEvent: PointerEvent) => {
      setPreferredInspectorWidth(clampRunDetailInspectorWidth(startWidth + startX - moveEvent.clientX, containerWidth));
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
  }, [containerWidth, inspectorWidth, mode]);

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
    mode === "desktop" && inspectorCollapsed ? "inspector-collapsed" : "",
    effectiveInspectorMaximized ? "inspector-maximized" : "",
    mode === "medium" && mediumDrawerOpen ? "inspector-drawer-open" : "",
    mode === "narrow" ? `narrow-${narrowView}-view` : ""
  ].filter(Boolean).join(" ");
  const workbenchStyle = useMemo(() => ({
    "--wf-run-inspector-width": `${mode === "desktop" && inspectorCollapsed ? runDetailCollapsedInspectorWidth : inspectorWidth}px`
  }) as CSSProperties, [inspectorCollapsed, inspectorWidth, mode]);

  return {
    containerRef,
    containerWidth,
    mode,
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
    toggleInspectorCollapsed,
    toggleInspectorMaximized,
    startInspectorResize,
    handleInspectorResizeKeyDown
  };
}

function normalizeContainerWidth(value: number) {
  return Number.isFinite(value) ? Math.max(0, value) : 0;
}
