import { useCallback, useEffect, useLayoutEffect, useRef, useState } from "react";

const compactNavigationQuery = "(max-width: 800px)";

/** Switches navigation without remounting the editor, keeping navigation and edited content in reach. */
export function useCompactNavigation() {
  const [compact, setCompact] = useState(() => typeof window.matchMedia === "function" && window.matchMedia(compactNavigationQuery).matches);
  const navigationElement = useRef<HTMLElement | null>(null);
  const navigationRef = useCallback((element: HTMLElement | null) => { navigationElement.current = element; }, []);
  const selectRef = useRef<HTMLSelectElement>(null);
  const [contentElement, setContentElement] = useState<HTMLDivElement | null>(null);
  const restoreFocus = useRef(false);
  const revealFocusedContent = useCallback(() => {
    const focused = document.activeElement;
    if (focused instanceof HTMLElement && contentElement?.contains(focused) && focused.getClientRects().length) {
      focused.scrollIntoView?.({ block: "nearest", inline: "nearest" });
    }
  }, [contentElement]);

  useEffect(() => {
    if (!contentElement) return;
    const observer = typeof ResizeObserver === "function" ? new ResizeObserver(revealFocusedContent) : null;
    observer?.observe(contentElement);
    window.addEventListener("resize", revealFocusedContent);
    return () => {
      observer?.disconnect();
      window.removeEventListener("resize", revealFocusedContent);
    };
  }, [contentElement, revealFocusedContent]);

  useEffect(() => {
    if (typeof window.matchMedia !== "function") return;
    const media = window.matchMedia(compactNavigationQuery);
    const change = () => {
      const focused = document.activeElement;
      restoreFocus.current = focused === selectRef.current || Boolean(focused && navigationElement.current?.contains(focused));
      setCompact(media.matches);
    };
    media.addEventListener("change", change);
    // Account for resizing between render and subscription, without taking focus from content.
    if (media.matches !== compact) change();
    return () => media.removeEventListener("change", change);
  }, [compact]);

  useLayoutEffect(() => {
    if (!restoreFocus.current) {
      revealFocusedContent();
      return;
    }
    restoreFocus.current = false;
    if (compact) selectRef.current?.focus();
    else {
      const navigation = navigationElement.current;
      (navigation?.querySelector<HTMLElement>('[aria-current="page"], button.active')
        ?? navigation?.querySelector<HTMLElement>('a[href], button:not(:disabled)'))?.focus();
    }
  }, [compact, revealFocusedContent]);

  return { compact, navigationRef, selectRef, contentRef: setContentElement };
}
