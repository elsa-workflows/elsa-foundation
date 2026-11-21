import React, { useCallback, useEffect, useId, useRef, useState } from "react";
import { enabledMenuIndexes, focusIndexedItem, stepEnabledIndex } from "../disclosure/menuNavigation";
import { StudioButton } from "../forms/Button";
import { AnchoredPopover } from "../overlay/AnchoredPopover";

export interface StudioRefreshContribution {
  id: string;
  label: string;
  onRefresh(): void | Promise<void>;
  refreshing?: boolean;
  disabled?: boolean;
  autoRefresh?: boolean;
  active?: boolean;
  buttonRef?: React.Ref<HTMLButtonElement>;
}

const intervals = [
  { label: "Manual", delay: 0 },
  { label: "Every 15 seconds", delay: 15_000 },
  { label: "Every 30 seconds", delay: 30_000 },
  { label: "Every minute", delay: 60_000 },
  { label: "Every 5 minutes", delay: 300_000 }
];
const menuIndexes = enabledMenuIndexes(intervals.map(() => false));

export function StudioRefreshControl({ refresh }: { refresh: StudioRefreshContribution }) {
  const menuId = useId();
  const intervalId = useId();
  const errorId = useId();
  const triggerRef = useRef<HTMLButtonElement>(null);
  const latestRefresh = useRef(refresh);
  latestRefresh.current = refresh;
  const mountedRef = useRef(false);
  const pendingRef = useRef(false);
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const [pending, setPending] = useState(false);
  const [completion, setCompletion] = useState(0);
  const [interval, setInterval] = useState(0);
  const [visible, setVisible] = useState(() => typeof document === "undefined" || !document.hidden);
  const [error, setError] = useState(false);
  const [open, setOpen] = useState(false);
  const [activeIndex, setActiveIndex] = useState(0);
  const autoEnabled = refresh.autoRefresh === true;
  const busy = pending || Boolean(refresh.refreshing);
  const selectedIndex = intervals.findIndex(option => option.delay === interval);

  const clearTimer = useCallback(() => {
    if (timerRef.current !== null) {
      clearTimeout(timerRef.current);
      timerRef.current = null;
    }
  }, []);

  useEffect(() => {
    mountedRef.current = true;
    const onVisibilityChange = () => {
      if (document.hidden) clearTimer();
      setVisible(!document.hidden);
    };
    document.addEventListener("visibilitychange", onVisibilityChange);
    return () => {
      mountedRef.current = false;
      clearTimer();
      document.removeEventListener("visibilitychange", onVisibilityChange);
    };
  }, [clearTimer]);

  const runRefresh = useCallback(async (automatic: boolean) => {
    const current = latestRefresh.current;
    if (!mountedRef.current || pendingRef.current || current.refreshing || current.disabled) return;
    if (automatic && (current.autoRefresh !== true || current.active === false || document.hidden)) return;
    clearTimer();
    pendingRef.current = true;
    setPending(true);
    setError(false);
    try {
      await current.onRefresh();
    } catch {
      if (mountedRef.current) {
        setError(true);
        setInterval(0);
      }
    } finally {
      if (mountedRef.current) {
        pendingRef.current = false;
        setPending(false);
        setCompletion(value => value + 1);
      }
    }
  }, [clearTimer]);

  useEffect(() => {
    if (!autoEnabled) setInterval(0);
    if (!autoEnabled || refresh.disabled || refresh.active === false) setOpen(false);
  }, [autoEnabled, refresh.disabled, refresh.active]);

  useEffect(() => {
    if (!autoEnabled || interval === 0 || busy || refresh.disabled || refresh.active === false || !visible) return;
    timerRef.current = setTimeout(() => {
      timerRef.current = null;
      void runRefresh(true);
    }, interval);
    return clearTimer;
  }, [autoEnabled, interval, busy, completion, refresh.disabled, refresh.active, visible, clearTimer, runRefresh]);

  function close() {
    setOpen(false);
    triggerRef.current?.focus();
  }

  function focusItem(index: number) {
    setActiveIndex(index);
    focusIndexedItem(menuId, index);
  }

  function openAt(index: number) {
    setActiveIndex(index);
    setOpen(true);
  }

  return (
    <div className="studio-refresh-control" data-studio-slot="studio.page-header.refresh">
      <div className="studio-refresh-buttons" data-split={autoEnabled ? "true" : undefined}>
        <StudioButton
          ref={refresh.buttonRef}
          className="studio-refresh-main"
          aria-label={refresh.label}
          aria-busy={busy}
          aria-describedby={[autoEnabled && interval > 0 ? intervalId : "", error ? errorId : ""].filter(Boolean).join(" ") || undefined}
          disabled={refresh.disabled || busy}
          onClick={() => void runRefresh(false)}
        >
          {busy ? "Refreshing…" : "Refresh"}
        </StudioButton>
        {autoEnabled && (
          <StudioButton
            ref={triggerRef}
            className="studio-refresh-trigger"
            aria-label={`${refresh.label}: refresh interval`}
            aria-haspopup="menu"
            aria-expanded={open}
            aria-controls={open ? menuId : undefined}
            disabled={refresh.disabled}
            onClick={() => open ? close() : openAt(selectedIndex)}
            onKeyDown={event => {
              if (event.key === "ArrowDown" || event.key === "ArrowUp") {
                event.preventDefault();
                const index = event.key === "ArrowUp" ? menuIndexes[menuIndexes.length - 1] : selectedIndex;
                if (open) focusItem(index);
                else openAt(index);
              }
            }}
          >
            <span aria-hidden="true">▾</span>
          </StudioButton>
        )}
        {autoEnabled && interval > 0 && (
          <span id={intervalId} className="studio-refresh-interval">{intervals[selectedIndex].label}</span>
        )}
      </div>
      {error && (
        <span id={errorId} className="studio-refresh-error" role="alert">
          Refresh failed. Try again or choose an interval to re-enable automatic refresh.
        </span>
      )}
      <AnchoredPopover
        anchorRef={triggerRef}
        open={open && autoEnabled}
        className="studio-refresh-popover"
        minWidth={192}
        onDismiss={reason => reason === "anchor-hidden" ? setOpen(false) : close()}
        onPositioned={() => focusIndexedItem(menuId, activeIndex)}
      >
        <div
          id={menuId}
          className="studio-refresh-menu"
          role="menu"
          aria-label={`${refresh.label}: refresh interval`}
          onKeyDown={event => {
            if (event.key === "Escape") {
              event.preventDefault();
              event.stopPropagation();
              close();
            } else if (event.key === "Tab") {
              close();
            } else if (event.key === "ArrowDown" || event.key === "ArrowUp") {
              event.preventDefault();
              focusItem(stepEnabledIndex(menuIndexes, activeIndex, event.key === "ArrowDown" ? 1 : -1));
            } else if (event.key === "Home" || event.key === "End") {
              event.preventDefault();
              focusItem(event.key === "Home" ? menuIndexes[0] : menuIndexes[menuIndexes.length - 1]);
            }
          }}
        >
          {intervals.map((option, index) => (
            <button
              key={option.delay}
              id={`${menuId}-item-${index}`}
              type="button"
              role="menuitemradio"
              className="studio-refresh-menu-item"
              aria-checked={interval === option.delay}
              tabIndex={activeIndex === index ? 0 : -1}
              onFocus={() => setActiveIndex(index)}
              onClick={() => {
                if (interval !== option.delay) clearTimer();
                setInterval(option.delay);
                setError(false);
                close();
              }}
            >
              <span className="studio-refresh-menu-check" aria-hidden="true">{interval === option.delay ? "✓" : ""}</span>
              {option.label}
            </button>
          ))}
        </div>
      </AnchoredPopover>
    </div>
  );
}