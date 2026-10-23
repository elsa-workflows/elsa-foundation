import { useEffect, useRef, type RefObject } from "react";

const focusableSelector = [
  "button:not(:disabled)",
  "input:not(:disabled)",
  "textarea:not(:disabled)",
  "select:not(:disabled)",
  "[href]",
  "[tabindex]:not([tabindex='-1'])"
].join(",");

function getFocusableElements(dialog: HTMLElement | null) {
  return [...(dialog?.querySelectorAll<HTMLElement>(focusableSelector) ?? [])]
    .filter(element => !element.closest("[hidden], [aria-hidden='true']"));
}

function isUsableFocusTarget(element: HTMLElement | null): element is HTMLElement {
  return Boolean(element && element.isConnected && element !== document.body && element !== document.documentElement
    && !element.matches(":disabled") && !element.closest("[hidden], [inert], [aria-hidden='true']"));
}

export function useHomeDialogFocus(
  dialogRef: RefObject<HTMLElement | null>,
  onEscape: (() => void) | null,
  fallbackFocusRef?: RefObject<HTMLElement | null>
) {
  const onEscapeRef = useRef(onEscape);
  onEscapeRef.current = onEscape;
  useEffect(() => {
    if (!onEscape) return;
    const previouslyFocused = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    const activeDialog = dialogRef.current;
    const fallbackFocus = fallbackFocusRef?.current ?? null;
    const frame = requestAnimationFrame(() => {
      const dialog = dialogRef.current;
      if (dialog?.contains(document.activeElement)) return;
      const first = getFocusableElements(dialog)[0];
      if (first) first.focus();
      else dialog?.focus();
    });
    const handleKeyDown = (event: KeyboardEvent) => {
      if (!onEscapeRef.current) return;
      if (event.key === "Escape" && onEscapeRef.current) {
        event.preventDefault();
        onEscapeRef.current();
        return;
      }
      if (event.key !== "Tab") return;
      const dialog = dialogRef.current;
      const focusable = getFocusableElements(dialog);
      if (focusable.length === 0) {
        event.preventDefault();
        dialogRef.current?.focus();
        return;
      }
      const first = focusable[0];
      const last = focusable[focusable.length - 1];
      if (document.activeElement === dialog || !dialog?.contains(document.activeElement)) {
        event.preventDefault();
        (event.shiftKey ? last : first).focus();
        return;
      }
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    };

    window.addEventListener("keydown", handleKeyDown);
    return () => {
      cancelAnimationFrame(frame);
      window.removeEventListener("keydown", handleKeyDown);
      const currentlyFocused = document.activeElement instanceof HTMLElement ? document.activeElement : null;
      if (isUsableFocusTarget(currentlyFocused) && currentlyFocused !== previouslyFocused && !activeDialog?.contains(currentlyFocused)) return;
      const restoreTarget = isUsableFocusTarget(previouslyFocused) ? previouslyFocused : fallbackFocus;
      if (isUsableFocusTarget(restoreTarget)) restoreTarget.focus();
    };
  }, [dialogRef, fallbackFocusRef, onEscape]);
}
