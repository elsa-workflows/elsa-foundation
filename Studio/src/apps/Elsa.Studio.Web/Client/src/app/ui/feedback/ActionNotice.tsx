import React, { useCallback, useEffect, useRef, useState } from "react";

export const defaultStudioActionNoticeDurationMs = 8_000;

export interface StudioActionNoticeProps {
  message: string;
  actionLabel?: string;
  onAction?(): void;
  durationMs?: number;
  onDismiss?(): void;
  className?: string;
}

/**
 * A compact, surface-local notice for short-lived feedback and precise reversible actions.
 * The caller controls placement; this component owns announcement, lifetime and interaction pause.
 */
export function StudioActionNotice({
  message,
  actionLabel,
  onAction,
  durationMs = defaultStudioActionNoticeDurationMs,
  onDismiss,
  className
}: StudioActionNoticeProps) {
  const [visible, setVisible] = useState(true);
  const timeoutRef = useRef<number | null>(null);
  const remainingMsRef = useRef(durationMs);
  const deadlineRef = useRef(0);
  const hoveredRef = useRef(false);
  const focusedRef = useRef(false);
  const onDismissRef = useRef(onDismiss);
  onDismissRef.current = onDismiss;

  const clearTimer = useCallback(() => {
    if (timeoutRef.current == null) return;
    window.clearTimeout(timeoutRef.current);
    timeoutRef.current = null;
  }, []);

  const dismiss = useCallback(() => {
    clearTimer();
    remainingMsRef.current = 0;
    setVisible(false);
    onDismissRef.current?.();
  }, [clearTimer]);

  const startTimer = useCallback((delayMs: number) => {
    clearTimer();
    const remainingMs = Math.max(0, delayMs);
    remainingMsRef.current = remainingMs;
    deadlineRef.current = Date.now() + remainingMs;
    timeoutRef.current = window.setTimeout(dismiss, remainingMs);
  }, [clearTimer, dismiss]);

  const pauseTimer = useCallback(() => {
    if (timeoutRef.current == null) return;
    remainingMsRef.current = Math.max(0, deadlineRef.current - Date.now());
    clearTimer();
  }, [clearTimer]);

  const resumeTimer = useCallback(() => {
    if (hoveredRef.current || focusedRef.current || timeoutRef.current != null) return;
    startTimer(remainingMsRef.current);
  }, [startTimer]);

  useEffect(() => {
    hoveredRef.current = false;
    focusedRef.current = false;
    setVisible(true);
    startTimer(Number.isFinite(durationMs) ? durationMs : defaultStudioActionNoticeDurationMs);
    return clearTimer;
  }, [clearTimer, durationMs, message, startTimer]);

  if (!visible) return null;

  const handleAction = () => {
    onAction?.();
    dismiss();
  };

  return (
    <div
      className={className ? `studio-action-notice ${className}` : "studio-action-notice"}
      role="status"
      aria-live="polite"
      aria-atomic="true"
      onMouseEnter={() => {
        hoveredRef.current = true;
        pauseTimer();
      }}
      onMouseLeave={() => {
        hoveredRef.current = false;
        resumeTimer();
      }}
      onFocusCapture={() => {
        focusedRef.current = true;
        pauseTimer();
      }}
      onBlurCapture={event => {
        if (event.currentTarget.contains(event.relatedTarget as Node | null)) return;
        focusedRef.current = false;
        resumeTimer();
      }}
    >
      <span className="studio-action-notice-message">{message}</span>
      {actionLabel && onAction ? (
        <button type="button" className="studio-action-notice-action" onClick={handleAction}>
          {actionLabel}
        </button>
      ) : null}
      <button
        type="button"
        className="studio-action-notice-dismiss"
        aria-label="Dismiss notification"
        title="Dismiss"
        onClick={dismiss}
      >
        <span aria-hidden="true">&times;</span>
      </button>
    </div>
  );
}
