import { useEffect, useId, useRef, useState, type KeyboardEvent as ReactKeyboardEvent } from "react";
import { Controls } from "@xyflow/react";
import { AnchoredPopover } from "@elsa-workflows/studio-ui";
import { Check, LocateFixed, LocateOff, ScanSearch } from "lucide-react";
import { getReadOnlyFitViewOptions, READ_ONLY_CONTROLS_ORIENTATION } from "./readOnlyMinimap";
import { runCanvasFocusModeOptions, type RunCanvasFocusMode } from "./runCanvasFocus";

function FocusModeIcon({ mode }: { mode: RunCanvasFocusMode }) {
  if (mode === "zoom") return <ScanSearch size={14} aria-hidden="true" />;
  if (mode === "off") return <LocateOff size={14} aria-hidden="true" />;
  return <LocateFixed size={14} aria-hidden="true" />;
}

/**
 * Zoom controls for read-only run and inspection canvases: one row along the bottom-left, so they
 * stay below the graph that the first view and Fit View place above the controls row (#4196).
 * Fit View uses the same padding as the canvas's own auto-fit; pass `miniMap={false}` on a canvas that never shows one.
 */
export function ReadOnlyFlowControls({
  nodeCount,
  miniMap,
  showInteractive = true,
  focusMode,
  onFocusModeChange
}: {
  nodeCount: number;
  miniMap?: boolean;
  showInteractive?: boolean;
  focusMode?: RunCanvasFocusMode;
  onFocusModeChange?(mode: RunCanvasFocusMode): void;
}) {
  const [menuOpen, setMenuOpen] = useState(false);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const menuRef = useRef<HTMLDivElement>(null);
  const menuId = useId();

  useEffect(() => {
    if (!menuOpen) return;
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key !== "Escape") return;
      event.preventDefault();
      setMenuOpen(false);
      triggerRef.current?.focus();
    };
    document.addEventListener("keydown", handleKeyDown);
    return () => document.removeEventListener("keydown", handleKeyDown);
  }, [menuOpen]);

  const currentOption = focusMode
    ? runCanvasFocusModeOptions.find(option => option.value === focusMode) ?? runCanvasFocusModeOptions[0]
    : null;

  const handleMenuKeyDown = (event: ReactKeyboardEvent<HTMLDivElement>) => {
    if (event.key !== "ArrowDown" && event.key !== "ArrowUp" && event.key !== "Home" && event.key !== "End") return;
    const items = [...(menuRef.current?.querySelectorAll<HTMLButtonElement>("[role='menuitemradio']") ?? [])];
    if (items.length === 0) return;
    event.preventDefault();
    const currentIndex = items.findIndex(item => item === document.activeElement);
    const nextIndex = event.key === "Home"
      ? 0
      : event.key === "End"
        ? items.length - 1
        : event.key === "ArrowDown"
          ? (currentIndex + 1 + items.length) % items.length
          : (currentIndex - 1 + items.length) % items.length;
    items[nextIndex]?.focus();
  };

  return (
    <Controls orientation={READ_ONLY_CONTROLS_ORIENTATION} showInteractive={showInteractive} fitViewOptions={getReadOnlyFitViewOptions(nodeCount, miniMap)}>
      {focusMode && onFocusModeChange && currentOption ? (
        <>
          <button
            ref={triggerRef}
            type="button"
            className={`react-flow__controls-button wf-canvas-focus-mode-trigger${menuOpen ? " is-open" : ""}${focusMode === "off" ? " is-off" : ""}`}
            aria-label={`Canvas focus behavior: ${currentOption.label}`}
            title={`Selection focus: ${currentOption.label}`}
            aria-haspopup="menu"
            aria-expanded={menuOpen}
            aria-controls={menuOpen ? menuId : undefined}
            onClick={() => setMenuOpen(open => !open)}
          >
            <FocusModeIcon mode={focusMode} />
          </button>
          <AnchoredPopover
            id={menuId}
            anchorRef={triggerRef}
            open={menuOpen}
            className="wf-canvas-focus-popover"
            role="menu"
            aria-label="Canvas focus behavior"
            minWidth={248}
            maxHeight={320}
            onDismiss={() => setMenuOpen(false)}
            onKeyDown={handleMenuKeyDown}
          >
            <div ref={menuRef} className="wf-canvas-focus-menu">
              <div className="wf-canvas-focus-menu-title">Selection focus</div>
              {runCanvasFocusModeOptions.map(option => {
                const selected = option.value === focusMode;
                return (
                  <button
                    key={option.value}
                    type="button"
                    role="menuitemradio"
                    aria-checked={selected}
                    className={`wf-canvas-focus-option${selected ? " is-selected" : ""}`}
                    onClick={() => {
                      onFocusModeChange(option.value);
                      setMenuOpen(false);
                      triggerRef.current?.focus();
                    }}
                  >
                    <span className="wf-canvas-focus-option-icon"><FocusModeIcon mode={option.value} /></span>
                    <span className="wf-canvas-focus-option-copy">
                      <span className="wf-canvas-focus-option-label">{option.label}</span>
                      <span className="wf-canvas-focus-option-description">{option.description}</span>
                    </span>
                    <span className="wf-canvas-focus-option-check" aria-hidden="true">
                      {selected ? <Check size={14} /> : null}
                    </span>
                  </button>
                );
              })}
            </div>
          </AnchoredPopover>
        </>
      ) : null}
    </Controls>
  );
}
