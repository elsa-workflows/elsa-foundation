import React, { useEffect, useLayoutEffect, useRef, useState } from "react";

/** A single results surface prevents matching top-navigation menus from covering each other. */
export function NavigationSearch({ query, path, searchInput, children }: {
  query: string;
  path: string;
  searchInput: React.RefObject<HTMLInputElement | null>;
  children: React.ReactNode;
}) {
  const results = useRef<HTMLElement>(null);
  const [searchTop, setSearchTop] = useState(0);
  const [dismissedQuery, setDismissedQuery] = useState<string | null>(null);
  const open = query !== dismissedQuery;
  const previousPath = useRef(path);
  useLayoutEffect(() => {
    setDismissedQuery(null);
  }, [query]);
  useLayoutEffect(() => {
    if (previousPath.current === path) return;
    previousPath.current = path;
    setDismissedQuery(query);
  }, [path, query]);
  useLayoutEffect(() => {
    if (!open) return;
    const sidebar = results.current?.closest(".sidebar");
    if (!sidebar) return;
    const place = () => setSearchTop(Math.max(0, Math.ceil(sidebar.getBoundingClientRect().bottom)));
    place();
    const observer = typeof ResizeObserver === "undefined" ? undefined : new ResizeObserver(place);
    observer?.observe(sidebar);
    window.addEventListener("resize", place);
    window.addEventListener("scroll", place, true);
    return () => {
      observer?.disconnect();
      window.removeEventListener("resize", place);
      window.removeEventListener("scroll", place, true);
    };
  }, [open]);
  useEffect(() => {
    if (!open) return;
    const onKey = (event: KeyboardEvent) => {
      if (event.defaultPrevented || event.key !== "Escape") return;
      event.preventDefault();
      setDismissedQuery(query);
      searchInput.current?.focus();
    };
    const insideSearch = (target: EventTarget | null) => target instanceof Node &&
      Boolean(results.current?.contains(target) || searchInput.current?.contains(target));
    const onPointer = (event: PointerEvent) => {
      if (insideSearch(event.target)) return;
      setDismissedQuery(query);
    };
    const onFocusOut = (event: FocusEvent) => {
      if (insideSearch(event.target) && !insideSearch(event.relatedTarget)) setDismissedQuery(query);
    };
    window.addEventListener("keydown", onKey);
    window.addEventListener("pointerdown", onPointer);
    document.addEventListener("focusout", onFocusOut);
    return () => {
      window.removeEventListener("keydown", onKey);
      window.removeEventListener("pointerdown", onPointer);
      document.removeEventListener("focusout", onFocusOut);
    };
  }, [open, query, searchInput]);
  return <nav ref={results} className="nav-section nav-search-results" style={{ "--studio-nav-search-top": `${searchTop}px` } as React.CSSProperties} aria-label="Search results" hidden={!open}>{children}</nav>;
}
