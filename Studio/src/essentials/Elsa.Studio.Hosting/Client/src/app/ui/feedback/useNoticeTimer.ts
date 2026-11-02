import React, { useCallback, useEffect, useRef } from "react";

export interface NoticeTimerOptions {
  /** Lifetime in milliseconds; `null` never elapses. */
  durationMs: number | null;
  onElapse(): void;
  /** Changing this (or `durationMs`) restarts the timer with the full duration. */
  resetKey?: unknown;
}

export interface NoticeTimerHandlers {
  onMouseEnter(): void;
  onMouseLeave(): void;
  onFocusCapture(): void;
  onBlurCapture(event: React.FocusEvent<HTMLElement>): void;
}

/**
 * Timed, pausable lifetime shared by surface-local notices and toasts.
 * Hovering or focusing inside the element pauses the timer; leaving resumes it with the remaining time.
 * Spread the returned handlers on the notice element.
 */
export function useNoticeTimer({ durationMs, onElapse, resetKey }: NoticeTimerOptions): NoticeTimerHandlers {
  const timeoutRef = useRef<number | null>(null);
  const remainingMsRef = useRef(durationMs ?? 0);
  const deadlineRef = useRef(0);
  const hoveredRef = useRef(false);
  const focusedRef = useRef(false);
  const activeRef = useRef(durationMs != null);
  const onElapseRef = useRef(onElapse);
  onElapseRef.current = onElapse;

  const clearTimer = useCallback(() => {
    if (timeoutRef.current == null) return;
    window.clearTimeout(timeoutRef.current);
    timeoutRef.current = null;
  }, []);

  const startTimer = useCallback((delayMs: number) => {
    clearTimer();
    const remainingMs = Math.max(0, delayMs);
    remainingMsRef.current = remainingMs;
    deadlineRef.current = Date.now() + remainingMs;
    timeoutRef.current = window.setTimeout(() => {
      timeoutRef.current = null;
      remainingMsRef.current = 0;
      onElapseRef.current();
    }, remainingMs);
  }, [clearTimer]);

  const pauseTimer = useCallback(() => {
    if (timeoutRef.current == null) return;
    remainingMsRef.current = Math.max(0, deadlineRef.current - Date.now());
    clearTimer();
  }, [clearTimer]);

  const resumeTimer = useCallback(() => {
    if (!activeRef.current || hoveredRef.current || focusedRef.current || timeoutRef.current != null) return;
    startTimer(remainingMsRef.current);
  }, [startTimer]);

  useEffect(() => {
    hoveredRef.current = false;
    focusedRef.current = false;
    activeRef.current = durationMs != null;
    if (durationMs == null) {
      clearTimer();
      return;
    }
    startTimer(durationMs);
    return clearTimer;
  }, [clearTimer, durationMs, resetKey, startTimer]);

  return {
    onMouseEnter: () => {
      hoveredRef.current = true;
      pauseTimer();
    },
    onMouseLeave: () => {
      hoveredRef.current = false;
      resumeTimer();
    },
    onFocusCapture: () => {
      focusedRef.current = true;
      pauseTimer();
    },
    onBlurCapture: event => {
      if (event.currentTarget.contains(event.relatedTarget as Node | null)) return;
      focusedRef.current = false;
      resumeTimer();
    }
  };
}
