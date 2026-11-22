import { useCallback, useEffect, useRef } from "react";
import { useStudioWorkspaceWhenActive } from "@elsa-workflows/studio-sdk";

/** Reveals a settled operation result without taking focus from another task or workspace tab. */
export function useActionFeedbackFocus(error: string, notice: string, pending: boolean, context: unknown) {
  const whenActive = useStudioWorkspaceWhenActive();
  const errorElement = useRef<HTMLElement | null>(null);
  const noticeElement = useRef<HTMLElement | null>(null);
  const errorRef = useCallback((element: HTMLElement | null) => { errorElement.current = element; }, []);
  const noticeRef = useCallback((element: HTMLElement | null) => { noticeElement.current = element; }, []);
  const message = error ? `error:${error}` : notice ? `notice:${notice}` : "";
  const latest = useRef({ context, message, pending }); latest.current = { context, message, pending };
  const delivered = useRef({ context, message: "" });

  useEffect(() => {
    if (delivered.current.context !== context) { delivered.current = { context, message: "" }; return; }
    if (!message) { delivered.current.message = ""; return; }
    if (pending || delivered.current.message === message) return;
    let cancelled = false; let frame: number | undefined;
    whenActive(() => {
      if (cancelled) return;
      frame = requestAnimationFrame(() => {
        const current = latest.current;
        if (cancelled || current.context !== context || current.message !== message || current.pending) return;
        const element = error ? errorElement.current : noticeElement.current;
        if (!element?.isConnected) return;
        delivered.current = { context, message };
        const focused = document.activeElement;
        // Pending/disabled actions commonly leave body focus; explicit user-selected focus wins.
        if (!focused || focused === document.body || focused === document.documentElement || !focused.isConnected || focused.matches(":disabled") || focused.closest('[role="dialog"][data-state="closed"]'))
          element.focus();
      });
    });
    return () => { cancelled = true; if (frame !== undefined) cancelAnimationFrame(frame); };
  }, [context, message, pending, error, whenActive]);

  return { errorRef, noticeRef };
}
