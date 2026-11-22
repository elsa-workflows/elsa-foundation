import React, { useId, useLayoutEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { Check, ChevronDown } from "lucide-react";
import { StudioButton } from "../forms/Button";
import { AnchoredPopover } from "../overlay/AnchoredPopover";
import { enabledMenuIndexes, focusIndexedItem, stepEnabledIndex } from "./menuNavigation";
import { type DisclosureControlProps, type OverflowMenuItem, type OverflowMenuAction } from "./types";

export interface OverflowMenuProps extends DisclosureControlProps {
  items: OverflowMenuItem[];
  ariaLabel?: string;
  defaultOpen?: boolean;
  menuLabel?: string;
  triggerIcon?: ReactNode;
  triggerTitle?: string;
  popoverClassName?: string;
  minWidth?: number;
  maxHeight?: number;
}

interface SplitAction {
  label: ReactNode;
  groupLabel: string;
  title?: string;
  onSelect(): void;
}

export function StudioSplitButton(props: OverflowMenuProps & { primaryAction: SplitAction; disabled?: boolean }) {
  return <ActionMenu {...props} />;
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

export function OverflowMenu(props: OverflowMenuProps) {
  return <ActionMenu {...props} />;
}

function ActionMenu({
  items,
  ariaLabel = "More actions",
  menuLabel = ariaLabel,
  triggerIcon,
  triggerTitle = ariaLabel,
  popoverClassName,
  minWidth = 176,
  maxHeight,
  defaultOpen = false,
  primaryAction,
  disabled = false
}: OverflowMenuProps & { primaryAction?: SplitAction; disabled?: boolean }) {
  const menuId = useId();
  const triggerRef = useRef<HTMLButtonElement>(null);
  const primaryRef = useRef<HTMLButtonElement>(null);
  const previousDisabled = useRef(disabled);
  const returnFocus = useRef<HTMLButtonElement | null>(null);
  const [open, setOpen] = useState(defaultOpen);
  const [activeIndex, setActiveIndex] = useState(0);
  const ordered = useMemo(() => orderOverflowMenuItems(items), [items]);
  const actionIndexes = enabledMenuIndexes(ordered.map(item => !isAction(item) || Boolean(item.disabled)));
  const groups: { label?: string; entries: { item: OverflowMenuItem; index: number }[] }[] = [];
  ordered.forEach((item, index) => {
    const label = isAction(item) ? item.group : undefined;
    if (!groups.length || groups[groups.length - 1].label !== label) groups.push({ label, entries: [] });
    groups[groups.length - 1].entries.push({ item, index });
  });

  // Preserve the initiating control across a busy command without stealing focus from a dialog.
  useLayoutEffect(() => {
    if (previousDisabled.current === disabled) return;
    previousDisabled.current = disabled;
    if (disabled) {
      if (open) {
        returnFocus.current = triggerRef.current;
        setOpen(false);
      }
      return;
    }
    const target = returnFocus.current;
    returnFocus.current = null;
    if (!target) return;
    const frame = requestAnimationFrame(() => {
      if (target.isConnected && !target.disabled && document.activeElement === document.body) target.focus();
    });
    return () => cancelAnimationFrame(frame);
  }, [disabled, open]);

  function close(restoreFocus = true) {
    setOpen(false);
    if (restoreFocus) triggerRef.current?.focus();
  }

  function focusMenuItem(index: number) {
    setActiveIndex(index);
    focusIndexedItem(menuId, index);
  }

  function openAt(index: number) {
    if (open) focusMenuItem(index);
    else {
      setActiveIndex(index);
      setOpen(true);
    }
  }

  function activate(item: OverflowMenuAction, button: HTMLButtonElement) {
    if (item.disabled || disabled) return;
    if (item.selection?.type !== "checkbox") {
      returnFocus.current = document.activeElement === button ? triggerRef.current : null;
      close();
    }
    item.onSelect();
  }

  function renderItem(item: OverflowMenuItem, index: number) {
    if (!isAction(item)) {
      return <div key={index} className="studio-overflow-menu-separator" role="separator" />;
    }
    return (
      <button key={item.id} id={`${menuId}-item-${index}`} type="button"
        role={item.selection ? `menuitem${item.selection.type}` : "menuitem"}
        aria-checked={item.selection?.checked} className="studio-overflow-menu-item"
        data-destructive={item.destructive || undefined} disabled={item.disabled} title={item.title}
        tabIndex={index === activeIndex ? 0 : -1} onFocus={() => setActiveIndex(index)}
        onClick={event => activate(item, event.currentTarget)}>
        {item.selection ? <Check size={15} aria-hidden="true" style={{ visibility: item.selection.checked ? "visible" : "hidden" }} /> : item.icon}
        {item.label}{item.detail && <span className="studio-menu-detail">{item.detail}</span>}
      </button>
    );
  }

  return (
    <div className={primaryAction ? "studio-split-button" : "studio-overflow-menu"}
      role={primaryAction ? "group" : undefined} aria-label={primaryAction?.groupLabel}>
      {primaryAction && <StudioButton ref={primaryRef} size="sm" variant="primary"
        className="studio-split-button-primary" title={primaryAction.title} disabled={disabled}
        onClick={event => {
          returnFocus.current = document.activeElement === event.currentTarget ? primaryRef.current : null;
          primaryAction.onSelect();
        }}>{primaryAction.label}</StudioButton>}
      <StudioButton ref={triggerRef} size="sm" variant={primaryAction ? "primary" : "default"}
        className={primaryAction ? "studio-split-button-trigger" : "studio-overflow-menu-trigger"}
        disabled={disabled} aria-label={ariaLabel} title={triggerTitle} aria-haspopup="menu" aria-expanded={open}
        aria-controls={open ? menuId : undefined}
        onClick={() => {
          if (open) close();
          else {
            const checked = ordered.findIndex(item => isAction(item) && !item.disabled && item.selection?.type === "radio" && item.selection.checked);
            openAt(checked >= 0 ? checked : actionIndexes[0] ?? 0);
          }
        }}
        onKeyDown={event => {
          if (event.key !== "ArrowDown" && event.key !== "ArrowUp") return;
          event.preventDefault();
          openAt((event.key === "ArrowUp" ? actionIndexes.at(-1) : actionIndexes[0]) ?? 0);
        }}>
        {primaryAction ? <ChevronDown size={15} aria-hidden="true" /> : triggerIcon ?? <span aria-hidden="true">⋯</span>}
      </StudioButton>
      <AnchoredPopover anchorRef={triggerRef} open={open && !disabled}
        className={["studio-overflow-menu-popover", popoverClassName].filter(Boolean).join(" ")}
        minWidth={minWidth} maxHeight={maxHeight ?? 240} onDismiss={() => close(false)}
        onPositioned={() => focusIndexedItem(menuId, activeIndex)}>
        <div id={menuId} className="studio-overflow-menu-list" role="menu" aria-label={menuLabel}
          onKeyDown={event => {
            if (event.key === "Escape" || event.key === "Tab") {
              if (event.key === "Escape") event.preventDefault();
              close();
              return;
            }
            if (event.key === "Enter" || event.key === " ") {
              if (event.target instanceof HTMLButtonElement) {
                event.preventDefault();
                event.target.click();
              }
              return;
            }
            if (!["ArrowDown", "ArrowUp", "Home", "End"].includes(event.key)) return;
            event.preventDefault();
            if (event.key === "Home") focusMenuItem(actionIndexes[0] ?? 0);
            else if (event.key === "End") focusMenuItem(actionIndexes.at(-1) ?? 0);
            else focusMenuItem(stepEnabledIndex(actionIndexes, activeIndex, event.key === "ArrowDown" ? 1 : -1));
          }}>
          {groups.map(({ label, entries }, groupIndex) => {
            const buttons = entries.map(({ item, index }) => renderItem(item, index));
            return label ? <div key={groupIndex} role="group" aria-label={label}>
              <span className="studio-menu-heading" aria-hidden="true">{label}</span>{buttons}
            </div> : <React.Fragment key={groupIndex}>{buttons}</React.Fragment>;
          })}
        </div>
      </AnchoredPopover>
    </div>
  );
}
