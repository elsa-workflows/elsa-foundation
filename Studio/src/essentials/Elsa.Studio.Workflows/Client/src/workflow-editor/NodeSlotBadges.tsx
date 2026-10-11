import React from "react";
import type { ChildSlot } from "../workflowAdapter";
import { WorkflowSlotNavigationContext } from "./contexts";

interface NodeSlotBadgesProps {
  ownerNodeId: string;
  ownerLabel: string;
  slots: ChildSlot[];
  onEnterSlot?(slot: ChildSlot): void;
}

/**
 * The slot-entry badges a canvas node renders for its container children — shared by the ordinary
 * activity node and the BPMN task/subprocess shape so both surfaces navigate identically.
 *
 * Navigation resolves from the context handler, with a per-node `onEnterSlot` taking precedence; see
 * `contexts.ts` for why the default is a context handler rather than a closure in node data. Falls back
 * to a static count badge when neither is available (the unsupported-designer placeholder), so a
 * container still reads as having slots even where they are not navigable.
 *
 * `ownerNodeId` must be the ACTIVITY node id. A BPMN canvas node is keyed by its element id, so its
 * caller passes `boundActivity.nodeId`: hosts look the owner up among the scope slot's activities, and
 * an element id resolves to nothing and blanks the canvas.
 */
export function NodeSlotBadges({ ownerNodeId, ownerLabel, slots, onEnterSlot }: NodeSlotBadgesProps) {
  const slotNavigation = React.useContext(WorkflowSlotNavigationContext);
  const enterSlot = onEnterSlot
    ?? (slotNavigation ? (slot: ChildSlot) => slotNavigation(ownerNodeId, ownerLabel, slot) : undefined);

  if (slots.length === 0) return null;
  if (!enterSlot) return <span className="wf-node-slot-badge">{slots.length} slot{slots.length === 1 ? "" : "s"}</span>;

  return (
    <span className="wf-node-slot-list">
      {slots.map(slot => (
        <button
          type="button"
          className="wf-node-slot-badge nodrag"
          key={slot.id}
          onClick={event => {
            event.stopPropagation();
            enterSlot(slot);
          }}
        >
          {slot.label}
        </button>
      ))}
    </span>
  );
}
