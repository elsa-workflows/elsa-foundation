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

export function useDialogFocus(
  dialogRef: RefObject<HTMLElement | null>,
  onEscape: (() => void) | null,
  focusDialogOnOpen = true
) {
  const onEscapeRef = useRef(onEscape);
  onEscapeRef.current = onEscape;
  useEffect(() => {
    const previouslyFocused = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    const frame = focusDialogOnOpen ? requestAnimationFrame(() => {
      const dialog = dialogRef.current;
      if (dialog?.contains(document.activeElement)) return;
      const first = getFocusableElements(dialog)[0];
      if (first) first.focus();
      else dialog?.focus();
    }) : null;
    const handleKeyDown = (event: KeyboardEvent) => {
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
      if (frame != null) cancelAnimationFrame(frame);
      window.removeEventListener("keydown", handleKeyDown);
      document.body.style.overflow = previousOverflow;
      previouslyFocused?.focus();
    };
  }, [dialogRef, focusDialogOnOpen]);
}
