import { useLayoutEffect, useRef, type RefObject } from "react";

const focusableSelector = [
  "button",
  "input:not([type='hidden'])",
  "textarea",
  "select",
  "summary",
  "[href]",
  "[contenteditable='true']",
  "[tabindex]"
].map(selector => `${selector}:not(:disabled)`).join(",");

function getFocusableElements(dialog: HTMLElement | null) {
  return [...(dialog?.querySelectorAll<HTMLElement>(focusableSelector) ?? [])]
    .filter(element => (!element.hasAttribute("tabindex") || element.tabIndex >= 0)
      && !element.closest("[hidden], [aria-hidden='true']"))
    .filter(element => {
      for (let ancestor = element.parentElement; ancestor && ancestor !== dialog; ancestor = ancestor.parentElement) {
        if (ancestor.tagName === "DETAILS" && !ancestor.hasAttribute("open")
          && !ancestor.querySelector(":scope > summary")?.contains(element)) return false;
      }
      return true;
    });
}

export function useDialogFocus(
  dialogRef: RefObject<HTMLElement | null>,
  onEscape: (() => void) | null,
  focusDialogOnOpen = true,
  restoreFocusFallback?: () => void,
  active = true
) {
  const onEscapeRef = useRef(onEscape);
  const restoreFocusFallbackRef = useRef(restoreFocusFallback);
  const previouslyFocusedRef = useRef<HTMLElement | null>(null);
  const capturedPreviousFocusRef = useRef(false);
  onEscapeRef.current = onEscape;
  restoreFocusFallbackRef.current = restoreFocusFallback;
  if (!capturedPreviousFocusRef.current && typeof document !== "undefined") {
    previouslyFocusedRef.current = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    capturedPreviousFocusRef.current = true;
  }
  useLayoutEffect(() => {
    // A replaced parent dialog must relinquish keyboard containment to its child.
    // Keep its original opener in the refs so resuming does not lose final focus restoration.
    if (!active) return;
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    if (focusDialogOnOpen) {
      const dialog = dialogRef.current;
      if (!dialog?.contains(document.activeElement)) {
        const focusable = getFocusableElements(dialog);
        const preferred = dialog?.querySelector<HTMLElement>("[data-dialog-initial-focus]");
        const first = preferred && focusable.includes(preferred) ? preferred : focusable[0];
        if (first) first.focus();
        else dialog?.focus();
      }
    }
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.defaultPrevented) return;
      if (event.key === "Escape") {
        event.preventDefault();
        onEscapeRef.current?.();
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
      window.removeEventListener("keydown", handleKeyDown);
      document.body.style.overflow = previousOverflow;
      const previouslyFocused = previouslyFocusedRef.current;
      if (previouslyFocused?.isConnected) previouslyFocused.focus();
      else restoreFocusFallbackRef.current?.();
    };
  }, [dialogRef, focusDialogOnOpen, active]);
}
