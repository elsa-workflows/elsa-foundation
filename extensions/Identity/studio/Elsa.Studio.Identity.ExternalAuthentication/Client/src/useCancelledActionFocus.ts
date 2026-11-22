import { useEffect, useRef } from "react";

/** Restore the opener after the page re-enables it; an asynchronous assessment may have blurred it before the dialog opened. */
export function useCancelledActionFocus(busy: boolean, context: object) {
  const pending = useRef<{ context: object; element: HTMLElement } | null>(null);
  useEffect(() => {
    if (busy) return;
    const target = pending.current; pending.current = null;
    if (target?.context === context && target.element.isConnected) target.element.focus();
  }, [busy, context]);
  return (element: Element | null) => {
    if (element instanceof HTMLElement) pending.current = { context, element };
  };
}
