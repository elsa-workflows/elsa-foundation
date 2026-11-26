import React, { useCallback, useEffect, useId, useLayoutEffect, useRef, useState } from "react";
import { ChevronDown } from "lucide-react";

/** Sidebar disclosure and dismissible top-navigation menu share the same named destinations. */
export function NavigationGroup({ label, top, rail, path, query, active, open, onToggle, children }: {
  label: string;
  top: boolean;
  rail: boolean;
  path: string;
  query: string;
  active: boolean;
  open: boolean;
  onToggle(open: boolean): void;
  children: React.ReactNode;
}) {
  const panelId = useId();
  const section = useRef<HTMLElement>(null);
  const trigger = useRef<HTMLButtonElement>(null);
  const [menuOpen, setMenuOpen] = useState(false);
  const [collapsedAtPath, setCollapsedAtPath] = useState<string | null>(null);
  const expanded = top
    ? menuOpen
    : rail || Boolean(query) || open || (active && collapsedAtPath !== path);
  const dismiss = useCallback((returnFocus = false) => {
    setMenuOpen(false);
    if (returnFocus) trigger.current?.focus();
  }, []);
  const previousPath = useRef(path);
  useLayoutEffect(() => {
    if (previousPath.current === path) return;
    previousPath.current = path;
    setCollapsedAtPath(null);
    setMenuOpen(false);
  }, [path]);
  useEffect(() => {
    if (!top || !expanded) return;
    const keydown = (event: KeyboardEvent) => {
      if (event.defaultPrevented || event.key !== "Escape") return;
      event.preventDefault();
      dismiss(true);
    };
    const pointerdown = (event: PointerEvent) => {
      if (event.target instanceof Node && section.current?.contains(event.target)) return;
      dismiss();
    };
    window.addEventListener("keydown", keydown);
    window.addEventListener("pointerdown", pointerdown);
    return () => {
      window.removeEventListener("keydown", keydown);
      window.removeEventListener("pointerdown", pointerdown);
    };
  }, [dismiss, expanded, top]);

  return <nav ref={section} className="nav-section nav-section-group" aria-label={label} onClick={event => {
    if (!top || !(event.target instanceof Element) || !event.target.closest("a")) return;
    if (event.defaultPrevented || (event.button === 0 && !event.metaKey && !event.ctrlKey && !event.shiftKey && !event.altKey)) dismiss();
  }} onBlur={event => {
    if (!top || (event.relatedTarget instanceof Node && event.currentTarget.contains(event.relatedTarget))) return;
    dismiss();
  }}>
    <button ref={trigger} type="button" className="nav-group-toggle" aria-expanded={expanded} aria-controls={panelId} onClick={() => {
      if (top) {
        if (expanded) dismiss();
        else { setMenuOpen(true); }
      } else {
        setCollapsedAtPath(expanded ? path : null);
        onToggle(!expanded);
      }
    }}><span>{label}</span><ChevronDown size={13} aria-hidden="true" /></button>
    <div id={panelId} className="nav-group-panel" hidden={!expanded}>{children}</div>
  </nav>;
}
