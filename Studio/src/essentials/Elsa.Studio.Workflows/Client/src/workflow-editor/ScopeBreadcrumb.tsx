import React from "react";
import { ChevronRight } from "lucide-react";
import type { ScopeFrame } from "../workflowAdapter";

// Breadcrumb trail over the scope frame path, shared by the editor and the run viewer. Unlabelled
// frames are descent hops planSlotNavigation tucks under the next crumb (entering a slot through its
// single container child); the visible crumb navigates to the full hop chain, so hiding them keeps
// the trail one-entry-per-slot.
export function ScopeBreadcrumb({ frames, onNavigate, className }: {
  frames: ScopeFrame[];
  onNavigate(frames: ScopeFrame[]): void;
  className?: string;
}) {
  return (
    <div className={className ? `wf-breadcrumb ${className}` : "wf-breadcrumb"}>
      <button type="button" onClick={() => onNavigate([])}>Root</button>
      {frames.map((frame, index) => frame.label ? (
        <React.Fragment key={`${frame.ownerNodeId}-${frame.slotId}-${index}`}>
          <ChevronRight size={13} />
          <button type="button" onClick={() => onNavigate(frames.slice(0, index + 1))}>{frame.label}</button>
        </React.Fragment>
      ) : null)}
    </div>
  );
}
