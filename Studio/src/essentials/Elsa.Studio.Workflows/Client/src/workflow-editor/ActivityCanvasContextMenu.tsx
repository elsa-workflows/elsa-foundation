import { useEffect, useRef } from "react";

export interface ActivityCanvasContextMenuProps {
  x: number;
  y: number;
  onDuplicate(): void;
  onClose(): void;
}

export function ActivityCanvasContextMenu({ x, y, onDuplicate, onClose }: ActivityCanvasContextMenuProps) {
  const menuRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    const menu = menuRef.current;
    const firstItem = menu?.querySelector<HTMLButtonElement>("[role='menuitem']");
    firstItem?.focus();

    const onPointerDown = (event: PointerEvent) => {
      if (menu && !menu.contains(event.target as Node)) onClose();
    };
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        event.preventDefault();
        onClose();
      }
    };
    window.addEventListener("pointerdown", onPointerDown);
    window.addEventListener("keydown", onKeyDown);
    return () => {
      window.removeEventListener("pointerdown", onPointerDown);
      window.removeEventListener("keydown", onKeyDown);
    };
  }, [onClose]);

  return (
    <div
      ref={menuRef}
      className="wf-connect-menu"
      role="menu"
      aria-label="Activity actions"
      style={{ left: x, top: y, width: 200 }}
      onMouseDown={event => event.stopPropagation()}
      onClick={event => event.stopPropagation()}
    >
      <div className="wf-connect-menu-list">
        <button
          type="button"
          role="menuitem"
          onClick={() => {
            onDuplicate();
            onClose();
          }}
        >
          <strong>Duplicate</strong>
          <small>Ctrl/Cmd+D</small>
        </button>
      </div>
    </div>
  );
}
