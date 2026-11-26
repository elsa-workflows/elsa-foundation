import React, { useCallback, useEffect, useLayoutEffect, useRef, useState, type RefCallback } from "react";

export const studioMinPaletteWidth = 180;
export const studioMaxPaletteWidth = 460;
export const studioDefaultPaletteWidth = 260;
export const studioMinInspectorWidth = 260;
export const studioMaxInspectorWidth = 560;
export const studioDefaultInspectorWidth = 320;
export const studioCollapsedSidePanelWidth = 42;
export const studioSidePanelResizeStep = 16;

export interface StudioSidePanelLayoutStorageKeys {
  paletteWidth: string;
  inspectorWidth: string;
  paletteCollapsed: string;
  inspectorCollapsed: string;
  maximized: string;
}

export interface StudioSidePanelLayoutOptions {
  palettePresent?: boolean;
  panelSelectors?: { palette: string; inspector: string };
  resizingBodyClassName?: string;
}

export interface StudioAdaptiveSidePanelLayout {
  paletteWidth: number;
  inspectorWidth: number;
  paletteMinimumWidth: number;
  inspectorMinimumWidth: number;
  paletteCollapsed: boolean;
  inspectorCollapsed: boolean;
  autoCollapsePalette: boolean;
  phoneLayout: boolean;
}

export type StudioSidePanel = "palette" | "inspector";

/** Resolves panel widths without changing the saved preferences when the available space changes. */
export function resolveStudioAdaptiveSidePanelLayout({
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
  maximizedSidePanel: StudioSidePanel | null;
  responsiveExpandedSide: StudioSidePanel | null;
  resizePriority?: StudioSidePanel | null;
  palettePresent?: boolean;
}): StudioAdaptiveSidePanelLayout {
  const measured = Number.isFinite(containerWidth) && containerWidth > 0;
  const compact = measured && containerWidth <= 900;
  const resizeHandleCount = palettePresent ? 2 : 1;
  const canvasMinimumWidth = compact ? 260 : 360;
  const initialPanelSpace = measured
    ? Math.max(0, containerWidth - canvasMinimumWidth - 10 * resizeHandleCount)
    : Number.POSITIVE_INFINITY;
  const effectiveMaximizedSidePanel = !palettePresent && maximizedSidePanel === "palette" ? null : maximizedSidePanel;
  const wantsPalette = palettePresent && !paletteCollapsed && effectiveMaximizedSidePanel !== "inspector";
  const wantsInspector = !inspectorCollapsed && effectiveMaximizedSidePanel !== "palette";
  const canFitBothPanels = !palettePresent || !measured || initialPanelSpace >= studioMinPaletteWidth + studioMinInspectorWidth;
  const autoCollapsePalette = measured && wantsPalette && wantsInspector && !canFitBothPanels;
  const phoneLayout = measured && containerWidth <= 600 && !effectiveMaximizedSidePanel
    && (autoCollapsePalette || (!palettePresent && wantsInspector && !paletteCollapsed));
  const forcePaletteOpen = autoCollapsePalette && responsiveExpandedSide === "palette";
  const effectivePaletteCollapsed = !wantsPalette || (autoCollapsePalette && !forcePaletteOpen);
  const effectiveInspectorCollapsed = !wantsInspector || (autoCollapsePalette && forcePaletteOpen)
    || (phoneLayout && responsiveExpandedSide !== "inspector");

  if (!measured || effectiveMaximizedSidePanel) {
    return {
      paletteWidth: !palettePresent ? 0 : effectivePaletteCollapsed ? studioCollapsedSidePanelWidth : preferredPaletteWidth,
      inspectorWidth: effectiveInspectorCollapsed ? studioCollapsedSidePanelWidth : preferredInspectorWidth,
      paletteMinimumWidth: !palettePresent ? 0 : Math.min(studioMinPaletteWidth, effectivePaletteCollapsed ? studioCollapsedSidePanelWidth : preferredPaletteWidth),
      inspectorMinimumWidth: Math.min(studioMinInspectorWidth, effectiveInspectorCollapsed ? studioCollapsedSidePanelWidth : preferredInspectorWidth),
      paletteCollapsed: effectivePaletteCollapsed,
      inspectorCollapsed: effectiveInspectorCollapsed,
      autoCollapsePalette,
      phoneLayout
    };
  }

  const expandedPanelMinimumWidth = (!palettePresent || effectivePaletteCollapsed ? 0 : studioMinPaletteWidth)
    + (effectiveInspectorCollapsed ? 0 : studioMinInspectorWidth);
  const collapsedWidth = (palettePresent && effectivePaletteCollapsed ? studioCollapsedSidePanelWidth : 0)
    + (effectiveInspectorCollapsed ? studioCollapsedSidePanelWidth : 0);
  const effectiveCanvasMinimumWidth = compact
    ? Math.min(260, Math.max(0, containerWidth - expandedPanelMinimumWidth - collapsedWidth - 10 * resizeHandleCount))
    : 360;
  const panelSpace = Math.max(0, containerWidth - effectiveCanvasMinimumWidth - 10 * resizeHandleCount);
  const expandableSpace = Math.max(0, panelSpace - collapsedWidth);
  const widths = effectivePaletteCollapsed || effectiveInspectorCollapsed
    ? {
        paletteWidth: !palettePresent ? 0 : effectivePaletteCollapsed
          ? studioCollapsedSidePanelWidth
          : Math.min(preferredPaletteWidth, expandableSpace),
        inspectorWidth: effectiveInspectorCollapsed
          ? studioCollapsedSidePanelWidth
          : Math.min(preferredInspectorWidth, expandableSpace)
      }
    : allocatePanelWidths(preferredPaletteWidth, preferredInspectorWidth, expandableSpace, resizePriority);

  return {
    ...widths,
    paletteMinimumWidth: Math.min(studioMinPaletteWidth, widths.paletteWidth),
    inspectorMinimumWidth: Math.min(studioMinInspectorWidth, widths.inspectorWidth),
    paletteCollapsed: effectivePaletteCollapsed,
    inspectorCollapsed: effectiveInspectorCollapsed,
    autoCollapsePalette,
    phoneLayout
  };
}

function allocatePanelWidths(preferredPaletteWidth: number, preferredInspectorWidth: number, availableWidth: number, resizePriority?: StudioSidePanel | null) {
  const preferredTotal = preferredPaletteWidth + preferredInspectorWidth;
  if (preferredTotal <= availableWidth) return { paletteWidth: preferredPaletteWidth, inspectorWidth: preferredInspectorWidth };

  const minimumTotal = studioMinPaletteWidth + studioMinInspectorWidth;
  if (availableWidth < minimumTotal) {
    const paletteWidth = Math.min(preferredPaletteWidth, availableWidth);
    return { paletteWidth, inspectorWidth: Math.min(preferredInspectorWidth, Math.max(0, availableWidth - paletteWidth)) };
  }
  if (resizePriority === "palette") {
    const paletteWidth = Math.min(preferredPaletteWidth, availableWidth - studioMinInspectorWidth);
    return { paletteWidth, inspectorWidth: Math.min(preferredInspectorWidth, availableWidth - paletteWidth) };
  }
  if (resizePriority === "inspector") {
    const inspectorWidth = Math.min(preferredInspectorWidth, availableWidth - studioMinPaletteWidth);
    return { paletteWidth: Math.min(preferredPaletteWidth, availableWidth - inspectorWidth), inspectorWidth };
  }

  const paletteFlex = Math.max(0, preferredPaletteWidth - studioMinPaletteWidth);
  const inspectorFlex = Math.max(0, preferredInspectorWidth - studioMinInspectorWidth);
  const flexTotal = paletteFlex + inspectorFlex;
  const availableFlex = availableWidth - minimumTotal;
  if (flexTotal === 0) return { paletteWidth: studioMinPaletteWidth, inspectorWidth: studioMinInspectorWidth };
  const paletteFlexWidth = Math.floor(paletteFlex * availableFlex / flexTotal);
  return {
    paletteWidth: studioMinPaletteWidth + paletteFlexWidth,
    inspectorWidth: studioMinInspectorWidth + Math.min(inspectorFlex, availableFlex - paletteFlexWidth)
  };
}

export function useStudioSidePanelLayout(storageKeys: StudioSidePanelLayoutStorageKeys, options: StudioSidePanelLayoutOptions = {}) {
  const palettePresent = options.palettePresent ?? true;
  const panelSelectors = options.panelSelectors ?? {
    palette: '[data-studio-side-panel="palette"]',
    inspector: '[data-studio-side-panel="inspector"]'
  };
  const resizingBodyClassName = options.resizingBodyClassName ?? "studio-side-panel-resizing";
  const [containerElement, setContainerElement] = useState<HTMLDivElement | null>(null);
  const [containerWidth, setContainerWidth] = useState(0);
  const lastMeasuredWidth = useRef(0);
  const [paletteWidth, setPaletteWidth] = useState(() => readStoredNumber(storageKeys.paletteWidth, studioDefaultPaletteWidth, studioMinPaletteWidth, studioMaxPaletteWidth));
  const [inspectorWidth, setInspectorWidth] = useState(() => readStoredNumber(storageKeys.inspectorWidth, studioDefaultInspectorWidth, studioMinInspectorWidth, studioMaxInspectorWidth));
  const [paletteCollapsed, setPaletteCollapsed] = useState(() => readStoredBoolean(storageKeys.paletteCollapsed, false));
  const [inspectorCollapsed, setInspectorCollapsed] = useState(() => readStoredBoolean(storageKeys.inspectorCollapsed, false));
  const [maximizedSidePanel, setMaximizedSidePanel] = useState<StudioSidePanel | null>(() => readStoredMaximizedSide(storageKeys.maximized));
  const [responsiveExpandedSide, setResponsiveExpandedSide] = useState<StudioSidePanel | null>(null);
  const [resizePriority, setResizePriority] = useState<StudioSidePanel | null>(null);
  const containerRef: RefCallback<HTMLDivElement> = useCallback(element => setContainerElement(element), []);
  const adaptiveLayout = resolveStudioAdaptiveSidePanelLayout({
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
    const applyWidth = (measuredWidth: number) => {
      const width = Number.isFinite(measuredWidth) ? Math.round(measuredWidth) : 0;
      if (width === lastMeasuredWidth.current) return;
      lastMeasuredWidth.current = width;
      if (width > 0 && width <= 600) {
        const focused = document.activeElement;
        if (focused && !containerElement.classList.contains("inspector-collapsed") && containerElement.querySelector(panelSelectors.inspector)?.contains(focused)) setResponsiveExpandedSide("inspector");
        else if (focused && !containerElement.classList.contains("palette-collapsed") && containerElement.querySelector(panelSelectors.palette)?.contains(focused)) setResponsiveExpandedSide("palette");
      }
      setContainerWidth(width);
    };
    const measure = () => applyWidth(containerElement.getBoundingClientRect().width);
    measure();
    if (typeof ResizeObserver === "undefined") {
      window.addEventListener("resize", measure);
      return () => window.removeEventListener("resize", measure);
    }
    const observer = new ResizeObserver(entries => {
      const entry = entries[0];
      applyWidth(entry?.contentRect.width ?? containerElement.getBoundingClientRect().width);
    });
    observer.observe(containerElement);
    return () => observer.disconnect();
  }, [containerElement, panelSelectors.inspector, panelSelectors.palette]);

  useEffect(() => {
    if (containerWidth > 0 && !adaptiveLayout.autoCollapsePalette && !adaptiveLayout.phoneLayout && !maximizedSidePanel && responsiveExpandedSide) setResponsiveExpandedSide(null);
  }, [containerWidth, adaptiveLayout.autoCollapsePalette, adaptiveLayout.phoneLayout, maximizedSidePanel, responsiveExpandedSide]);
  useEffect(() => writeStoredValue(storageKeys.paletteWidth, String(paletteWidth)), [paletteWidth, storageKeys.paletteWidth]);
  useEffect(() => writeStoredValue(storageKeys.inspectorWidth, String(inspectorWidth)), [inspectorWidth, storageKeys.inspectorWidth]);
  useEffect(() => writeStoredValue(storageKeys.paletteCollapsed, String(paletteCollapsed)), [paletteCollapsed, storageKeys.paletteCollapsed]);
  useEffect(() => writeStoredValue(storageKeys.inspectorCollapsed, String(inspectorCollapsed)), [inspectorCollapsed, storageKeys.inspectorCollapsed]);
  useEffect(() => writeStoredValue(storageKeys.maximized, maximizedSidePanel), [maximizedSidePanel, storageKeys.maximized]);
  useEffect(() => {
    if (!maximizedSidePanel) return;
    const handleKeyDown = (event: KeyboardEvent) => { if (event.key === "Escape") setMaximizedSidePanel(null); };
    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [maximizedSidePanel]);

  const toggleSidePanelCollapsed = useCallback((side: StudioSidePanel) => {
    if (!palettePresent && adaptiveLayout.phoneLayout && !inspectorCollapsed) {
      setResponsiveExpandedSide(adaptiveLayout.inspectorCollapsed ? "inspector" : null);
      setMaximizedSidePanel(null);
      return;
    }
    if (adaptiveLayout.autoCollapsePalette && !paletteCollapsed && !inspectorCollapsed) {
      const sideIsAutoCollapsed = side === "palette" ? adaptiveLayout.paletteCollapsed : adaptiveLayout.inspectorCollapsed;
      if (sideIsAutoCollapsed) setResponsiveExpandedSide(side);
      else if (side === "palette" || containerWidth <= 600) setResponsiveExpandedSide(null);
      else {
        setResponsiveExpandedSide(null);
        setInspectorCollapsed(true);
      }
      setMaximizedSidePanel(null);
      return;
    }
    setResponsiveExpandedSide(containerWidth > 0 && containerWidth <= 600 ? side : null);
    setMaximizedSidePanel(current => current === side ? null : current);
    if (side === "palette") setPaletteCollapsed(current => !current);
    else setInspectorCollapsed(current => !current);
  }, [adaptiveLayout.autoCollapsePalette, adaptiveLayout.inspectorCollapsed, adaptiveLayout.paletteCollapsed, adaptiveLayout.phoneLayout, palettePresent, containerWidth, inspectorCollapsed, paletteCollapsed]);

  const toggleSidePanelMaximized = useCallback((side: StudioSidePanel) => {
    setResponsiveExpandedSide(side);
    if (side === "palette") setPaletteCollapsed(false);
    else setInspectorCollapsed(false);
    setMaximizedSidePanel(current => current === side ? null : side);
  }, []);

  const resizeSidePanel = useCallback((side: StudioSidePanel, delta: number) => {
    setMaximizedSidePanel(null);
    setResizePriority(side);
    if (side === "palette") {
      setPaletteCollapsed(false);
      setPaletteWidth(clamp(adaptiveLayout.paletteWidth + delta, studioMinPaletteWidth, studioMaxPaletteWidth));
    } else {
      setInspectorCollapsed(false);
      setInspectorWidth(clamp(adaptiveLayout.inspectorWidth + delta, studioMinInspectorWidth, studioMaxInspectorWidth));
    }
  }, [adaptiveLayout.inspectorWidth, adaptiveLayout.paletteWidth]);

  const startSidePanelResize = useCallback((side: StudioSidePanel, event: React.PointerEvent<HTMLDivElement>) => {
    event.preventDefault();
    setMaximizedSidePanel(null);
    if (side === "palette") setPaletteCollapsed(false);
    else setInspectorCollapsed(false);
    const startX = event.clientX;
    const startWidth = side === "palette" ? adaptiveLayout.paletteWidth : adaptiveLayout.inspectorWidth;
    const min = side === "palette" ? studioMinPaletteWidth : studioMinInspectorWidth;
    const max = side === "palette" ? studioMaxPaletteWidth : studioMaxInspectorWidth;
    document.body.classList.add(resizingBodyClassName);
    const handlePointerMove = (moveEvent: PointerEvent) => {
      const delta = side === "palette" ? moveEvent.clientX - startX : startX - moveEvent.clientX;
      const nextWidth = clamp(startWidth + delta, min, max);
      setResizePriority(side);
      if (side === "palette") setPaletteWidth(nextWidth);
      else setInspectorWidth(nextWidth);
    };
    const stopResize = () => {
      document.body.classList.remove(resizingBodyClassName);
      window.removeEventListener("pointermove", handlePointerMove);
      window.removeEventListener("pointerup", stopResize);
      window.removeEventListener("pointercancel", stopResize);
    };
    window.addEventListener("pointermove", handlePointerMove);
    window.addEventListener("pointerup", stopResize);
    window.addEventListener("pointercancel", stopResize);
  }, [adaptiveLayout.inspectorWidth, adaptiveLayout.paletteWidth, resizingBodyClassName]);

  const handleSidePanelResizeKeyDown = useCallback((side: StudioSidePanel, event: React.KeyboardEvent<HTMLDivElement>) => {
    if (event.key === "ArrowLeft") {
      event.preventDefault();
      resizeSidePanel(side, side === "palette" ? -studioSidePanelResizeStep : studioSidePanelResizeStep);
    } else if (event.key === "ArrowRight") {
      event.preventDefault();
      resizeSidePanel(side, side === "palette" ? studioSidePanelResizeStep : -studioSidePanelResizeStep);
    } else if (event.key === "Home") {
      event.preventDefault();
      setResizePriority(side);
      if (side === "palette") setPaletteWidth(studioMinPaletteWidth);
      else setInspectorWidth(studioMinInspectorWidth);
    } else if (event.key === "End") {
      event.preventDefault();
      setResizePriority(side);
      if (side === "palette") setPaletteWidth(studioMaxPaletteWidth);
      else setInspectorWidth(studioMaxInspectorWidth);
    }
  }, [resizeSidePanel]);

  const setInspectorPanelCollapsed = useCallback((collapsed: boolean) => {
    setResponsiveExpandedSide(collapsed ? null : "inspector");
    setInspectorCollapsed(collapsed);
    if (!collapsed) setMaximizedSidePanel(current => current === "palette" ? null : current);
  }, []);
  const effectiveMaximizedSidePanel = !palettePresent && maximizedSidePanel === "palette" ? null : maximizedSidePanel;
  const paletteExpanded = palettePresent && !adaptiveLayout.paletteCollapsed && effectiveMaximizedSidePanel !== "inspector";
  const inspectorExpanded = !adaptiveLayout.inspectorCollapsed && effectiveMaximizedSidePanel !== "palette";

  return {
    containerRef,
    paletteWidth: adaptiveLayout.paletteWidth,
    inspectorWidth: adaptiveLayout.inspectorWidth,
    paletteMinimumWidth: adaptiveLayout.paletteMinimumWidth,
    inspectorMinimumWidth: adaptiveLayout.inspectorMinimumWidth,
    paletteCollapsed: adaptiveLayout.paletteCollapsed,
    inspectorCollapsed: adaptiveLayout.inspectorCollapsed,
    maximizedSidePanel: effectiveMaximizedSidePanel,
    setInspectorCollapsed: setInspectorPanelCollapsed,
    paletteExpanded,
    inspectorExpanded,
    toggleSidePanelCollapsed,
    toggleSidePanelMaximized,
    startSidePanelResize,
    handleSidePanelResizeKeyDown
  };
}

function readStoredNumber(key: string, fallback: number, min: number, max: number) {
  const value = Number(getStorage()?.getItem(key));
  return Number.isFinite(value) && getStorage()?.getItem(key) != null ? clamp(value, min, max) : fallback;
}
function readStoredBoolean(key: string, fallback: boolean) {
  const value = getStorage()?.getItem(key);
  return value === "true" ? true : value === "false" ? false : fallback;
}
function readStoredMaximizedSide(key: string): StudioSidePanel | null {
  const value = getStorage()?.getItem(key);
  return value === "palette" || value === "inspector" ? value : null;
}
function writeStoredValue(key: string, value: string | null) {
  const storage = getStorage();
  if (!storage) return;
  try { if (value == null) storage.removeItem(key); else storage.setItem(key, value); } catch { /* Storage is optional in embedded contexts. */ }
}
function getStorage(): Storage | null {
  try { return typeof window === "undefined" ? null : window.localStorage; } catch { return null; }
}
function clamp(value: number, min: number, max: number) { return Math.min(max, Math.max(min, Math.round(value))); }

export function StudioSidePanelActions({
  side,
  panelLabel,
  collapsed,
  maximized,
  onToggleCollapsed,
  onToggleMaximized,
  className,
  buttonClassName
}: {
  side: StudioSidePanel;
  panelLabel: string;
  collapsed: boolean;
  maximized: boolean;
  onToggleCollapsed(): void;
  onToggleMaximized(): void;
  className?: string;
  buttonClassName?: string;
}) {
  const expandDirection = side === "palette" ? "right" : "left";
  const collapseDirection = side === "palette" ? "left" : "right";
  return <span className={["studio-side-panel-actions", className].filter(Boolean).join(" ")}>
    <button type="button" className={["studio-side-panel-action-button", buttonClassName].filter(Boolean).join(" ")}
      aria-label={collapsed ? `Expand ${panelLabel} panel` : `Collapse ${panelLabel} panel`} title={collapsed ? "Expand" : "Collapse"} onClick={onToggleCollapsed}>
      <Chevron direction={collapsed ? expandDirection : collapseDirection} />
    </button>
    {!collapsed ? <button type="button" className={["studio-side-panel-action-button", buttonClassName].filter(Boolean).join(" ")}
      aria-label={maximized ? `Restore ${panelLabel} panel` : `Maximize ${panelLabel} panel`} title={maximized ? "Restore" : "Maximize"} onClick={onToggleMaximized}>
      {maximized ? <svg aria-hidden="true" viewBox="0 0 16 16"><path d="M6 2H2v4m0-4 5 5m3 7h4v-4m0 4-5-5" /></svg> : <svg aria-hidden="true" viewBox="0 0 16 16"><path d="M2 2h12v12H2z" /></svg>}
    </button> : null}
  </span>;
}

export function StudioSidePanelResizeHandle({
  side,
  visible,
  value,
  min,
  max,
  panelLabel,
  onPointerDown,
  onKeyDown,
  className,
  spacerClassName
}: {
  side: StudioSidePanel;
  visible: boolean;
  value: number;
  min: number;
  max: number;
  panelLabel: string;
  onPointerDown(event: React.PointerEvent<HTMLDivElement>): void;
  onKeyDown(event: React.KeyboardEvent<HTMLDivElement>): void;
  className?: string;
  spacerClassName?: string;
}) {
  if (!visible) return <div className={["studio-side-resize-spacer", spacerClassName].filter(Boolean).join(" ")} aria-hidden="true" />;
  return <div className={["studio-side-resize-handle", side === "palette" ? "left" : "right", className].filter(Boolean).join(" ")}
    role="separator" aria-label={`Resize ${panelLabel} panel`} aria-orientation="vertical" aria-valuemin={min} aria-valuemax={max} aria-valuenow={value}
    tabIndex={0} onPointerDown={onPointerDown} onKeyDown={onKeyDown} />;
}

function Chevron({ direction }: { direction: "left" | "right" }) {
  return <svg aria-hidden="true" viewBox="0 0 16 16"><path d={direction === "left" ? "m10 3-5 5 5 5" : "m6 3 5 5-5 5"} /></svg>;
}