import React, { useId, useMemo, useRef, useState } from "react";
import { AnchoredPopover } from "../overlay/AnchoredPopover";
import { enabledMenuIndexes, focusIndexedItem, stepEnabledIndex } from "./menuNavigation";
import { type DisclosureControlProps, type OverflowMenuItem } from "./types";

export interface OverflowMenuProps extends DisclosureControlProps {
  items: OverflowMenuItem[];
  ariaLabel?: string;
  defaultOpen?: boolean;
}

function isAction(item: OverflowMenuItem): item is Extract<OverflowMenuItem, { id: string }> {
  return !("type" in item);
}

/**
 * Secondary-action menu. Destructive items are moved last and separated from
 * the rest so callers cannot accidentally bury a core action among them.
 */
export function orderOverflowMenuItems(items: OverflowMenuItem[]): OverflowMenuItem[] {
  const actions = items.filter(isAction);
  const ordinary = actions.filter(item => !item.destructive);
  const destructive = actions.filter(item => item.destructive);
  if (destructive.length === 0) return ordinary;
  if (ordinary.length === 0) return destructive;
  return [...ordinary, { type: "separator" }, ...destructive];
}

export function OverflowMenu({
  items,
  ariaLabel = "More actions",
  defaultOpen = false
}: OverflowMenuProps) {
  const menuId = useId();
  const triggerRef = useRef<HTMLButtonElement>(null);
  const [open, setOpen] = useState(defaultOpen);
  const [activeIndex, setActiveIndex] = useState(0);
  const ordered = useMemo(() => orderOverflowMenuItems(items), [items]);
  const actionIndexes = useMemo(
    () => enabledMenuIndexes(ordered.map(item => !isAction(item) || Boolean(item.disabled))),
    [ordered]
  );

  function close() {
    setOpen(false);
    triggerRef.current?.focus();
  }

  function openAt(index: number) {
    setActiveIndex(index);
    setOpen(true);
  }

  function focusMenuItem(index: number) {
    setActiveIndex(index);
    focusIndexedItem(menuId, index);
  }

  function activate(item: Extract<OverflowMenuItem, { id: string }>) {
    if (item.disabled) return;
    item.onSelect();
    close();
  }

  return (
    <div className="studio-overflow-menu">
      <button
        ref={triggerRef}
        type="button"
        className="studio-overflow-menu-trigger"
        aria-label={ariaLabel}
        aria-haspopup="menu"
        aria-expanded={open}
        aria-controls={open ? menuId : undefined}
        onClick={() => {
          if (open) close();
          else openAt(actionIndexes[0] ?? 0);
        }}
        onKeyDown={event => {
          if (event.key === "ArrowDown") {
            event.preventDefault();
            if (open) focusMenuItem(actionIndexes[0] ?? 0);
            else openAt(actionIndexes[0] ?? 0);
          } else if (event.key === "ArrowUp") {
            event.preventDefault();
            const lastIndex = actionIndexes[actionIndexes.length - 1] ?? 0;
            if (open) focusMenuItem(lastIndex);
            else openAt(lastIndex);
          }
        }}
      >
        <span aria-hidden="true">⋯</span>
      </button>
      <AnchoredPopover
        id={menuId}
        anchorRef={triggerRef}
        open={open}
        className="studio-overflow-menu-popover"
        minWidth={176}
        onDismiss={close}
        onPositioned={() => focusIndexedItem(menuId, activeIndex)}
      >
        <div
          className="studio-overflow-menu-list"
          role="menu"
          aria-label={ariaLabel}
          onKeyDown={event => {
            if (event.key === "Escape") {
              event.preventDefault();
              close();
            } else if (event.key === "ArrowDown") {
              event.preventDefault();
              focusMenuItem(stepEnabledIndex(actionIndexes, activeIndex, 1));
            } else if (event.key === "ArrowUp") {
              event.preventDefault();
              focusMenuItem(stepEnabledIndex(actionIndexes, activeIndex, -1));
            } else if (event.key === "Home") {
              event.preventDefault();
              focusMenuItem(actionIndexes[0] ?? 0);
            } else if (event.key === "End") {
              event.preventDefault();
              focusMenuItem(actionIndexes[actionIndexes.length - 1] ?? 0);
            }
          }}
        >
          {ordered.map((item, index) => {
            if (!isAction(item)) {
              return <div key={`separator-${index}`} className="studio-overflow-menu-separator" role="separator" />;
            }
            return (
              <button
                key={item.id}
                id={`${menuId}-item-${index}`}
                type="button"
                role="menuitem"
                className="studio-overflow-menu-item"
                data-destructive={item.destructive ? "true" : undefined}
                disabled={item.disabled}
                tabIndex={index === activeIndex ? 0 : -1}
                onClick={() => activate(item)}
              >
                {item.label}
              </button>
            );
          })}
        </div>
      </AnchoredPopover>
    </div>
  );
}
