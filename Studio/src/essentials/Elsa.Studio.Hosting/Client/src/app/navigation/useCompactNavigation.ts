import { useCallback, useEffect, useLayoutEffect, useRef, useState } from "react";

const compactNavigationQuery = "(max-width: 800px)";

/** Switches navigation without remounting the editor, and carries navigation focus across resizing. */
export function useCompactNavigation() {
  const [compact, setCompact] = useState(() => typeof window.matchMedia === "function" && window.matchMedia(compactNavigationQuery).matches);
  const navigationElement = useRef<HTMLElement | null>(null);
  const navigationRef = useCallback((element: HTMLElement | null) => { navigationElement.current = element; }, []);
  const selectRef = useRef<HTMLSelectElement>(null);
  const restoreFocus = useRef(false);

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
    if (!restoreFocus.current) return;
    restoreFocus.current = false;
    if (compact) selectRef.current?.focus();
    else navigationElement.current?.querySelector<HTMLElement>('[aria-current="page"], button.active')?.focus();
  }, [compact]);

  return { compact, navigationRef, selectRef };
}
