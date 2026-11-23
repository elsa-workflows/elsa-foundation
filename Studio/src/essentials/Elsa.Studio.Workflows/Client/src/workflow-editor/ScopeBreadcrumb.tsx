import React from "react";
import { Home } from "lucide-react";
import { StudioBreadcrumbs } from "@elsa-workflows/studio-ui";
import type { ScopeFrame } from "../workflowAdapter";
import "./ScopeBreadcrumb.css";

// Breadcrumb trail over the scope frame path, shared by the editor and the run viewer. Unlabelled
// frames are descent hops planSlotNavigation tucks under the next crumb (entering a slot through its
// single container child); the visible crumb navigates to the full hop chain, so hiding them keeps
// the trail one-entry-per-slot.
export function ScopeBreadcrumb({ frames, onNavigate, className }: {
  frames: ScopeFrame[];
  onNavigate(frames: ScopeFrame[]): void;
  className?: string;
}) {
  const crumbs = [
    { label: <><Home className="wf-scope-root-icon" size={16} aria-hidden="true" /><span className="wf-scope-root-label">Root</span></>, onClick: () => onNavigate([]) },
    ...frames.flatMap((frame, index) => frame.label
      ? [{ label: frame.label, onClick: () => onNavigate(frames.slice(0, index + 1)) }]
      : [])
  ];
  const current = crumbs[crumbs.length - 1];
  return <StudioBreadcrumbs
    aria-label="Graph scope"
    className={className ? `wf-breadcrumb ${className}` : "wf-breadcrumb"}
    items={crumbs.slice(0, -1)}
    current={<button className="studio-breadcrumb-action" type="button" onClick={current.onClick}>{current.label}</button>}
  />;
}
