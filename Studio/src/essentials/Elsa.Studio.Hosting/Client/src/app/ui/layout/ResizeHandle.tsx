import type { KeyboardEventHandler, PointerEventHandler } from "react";

export type StudioResizeOrientation = "horizontal" | "vertical";

export function StudioResizeHandle({
  orientation,
  label,
  controls,
  value,
  min,
  max,
  tabIndex = 0,
  className,
  onPointerDown,
  onPointerMove,
  onPointerUp,
  onPointerCancel,
  onLostPointerCapture,
  onKeyDown
}: {
  orientation: StudioResizeOrientation;
  label: string;
  controls: string;
  value: number;
  min: number;
  max: number;
  tabIndex?: number;
  className?: string;
  onPointerDown?: PointerEventHandler<HTMLDivElement>;
  onPointerMove?: PointerEventHandler<HTMLDivElement>;
  onPointerUp?: PointerEventHandler<HTMLDivElement>;
  onPointerCancel?: PointerEventHandler<HTMLDivElement>;
  onLostPointerCapture?: PointerEventHandler<HTMLDivElement>;
  onKeyDown?: KeyboardEventHandler<HTMLDivElement>;
}) {
  return <div
    className={["studio-resize-handle", orientation, className].filter(Boolean).join(" ")}
    role="separator"
    aria-label={label}
    aria-controls={controls}
    aria-orientation={orientation}
    aria-valuemin={min}
    aria-valuemax={max}
    aria-valuenow={value}
    tabIndex={tabIndex}
    onPointerDown={onPointerDown}
    onPointerMove={onPointerMove}
    onPointerUp={onPointerUp}
    onPointerCancel={onPointerCancel}
    onLostPointerCapture={onLostPointerCapture}
    onKeyDown={onKeyDown}
  />;
}