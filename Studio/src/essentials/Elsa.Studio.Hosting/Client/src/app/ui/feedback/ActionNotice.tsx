import React, { useCallback, useEffect, useRef, useState } from "react";
import { useNoticeTimer } from "./useNoticeTimer";

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
  const onDismissRef = useRef(onDismiss);
  onDismissRef.current = onDismiss;

  const dismiss = useCallback(() => {
    setVisible(false);
    onDismissRef.current?.();
  }, []);

  // Once dismissed the timer is released (null never elapses) so onDismiss fires only once.
  const timerHandlers = useNoticeTimer({
    durationMs: visible ? (Number.isFinite(durationMs) ? durationMs : defaultStudioActionNoticeDurationMs) : null,
    onElapse: dismiss,
    resetKey: message
  });

  useEffect(() => {
    setVisible(true);
  }, [message]);

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
      {...timerHandlers}
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
