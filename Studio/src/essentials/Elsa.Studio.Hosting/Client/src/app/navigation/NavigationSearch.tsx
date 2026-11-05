import React, { useEffect, useLayoutEffect, useRef, useState } from "react";

/** A single results surface prevents matching top-navigation menus from covering each other. */
export function NavigationSearch({ query, path, searchInput, children }: {
  query: string;
  path: string;
  searchInput: React.RefObject<HTMLInputElement | null>;
  children: React.ReactNode;
}) {
  const results = useRef<HTMLElement>(null);
  const [dismissedQuery, setDismissedQuery] = useState<string | null>(null);
  const open = query !== dismissedQuery;
  const previousPath = useRef(path);
  useLayoutEffect(() => {
    if (previousPath.current === path) return;
    previousPath.current = path;
    setDismissedQuery(query);
  }, [path, query]);
  useEffect(() => {
    if (!open) return;
    const onKey = (event: KeyboardEvent) => {
      if (event.defaultPrevented || event.key !== "Escape") return;
      event.preventDefault();
      setDismissedQuery(query);
      searchInput.current?.focus();
    };
    const onPointer = (event: PointerEvent) => {
      if (event.target instanceof Node && (results.current?.contains(event.target) || searchInput.current?.contains(event.target))) return;
      setDismissedQuery(query);
    };
    window.addEventListener("keydown", onKey);
    window.addEventListener("pointerdown", onPointer);
    return () => { window.removeEventListener("keydown", onKey); window.removeEventListener("pointerdown", onPointer); };
  }, [open, query, searchInput]);
  return <nav ref={results} className="nav-section nav-search-results" aria-label="Search results" hidden={!open} onBlur={event => {
    if (event.relatedTarget instanceof Node && results.current?.contains(event.relatedTarget)) return;
    setDismissedQuery(query);
  }}>{children}</nav>;
}
