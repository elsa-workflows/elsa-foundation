import { Boxes } from "lucide-react";
import type { ActivityCatalogItem } from "../workflowTypes";
import type { CreateWorkflowKind } from "./editorTypes";
import { findRootKindActivity } from "./editorHelpers";
import { WorkflowRootButtonCards } from "./WorkflowRootCards";

// Empty-state panel shown on the canvas when the user is inside a slot (frames.length > 0) whose
// resolved slot holds zero activities. Instead of a blank grid it offers the two common container
// choices (Flowchart / Sequence) plus a "Browse all activities…" affordance that opens the full
// activity picker, and reminds the user that palette drag & drop also works. Picking a card or a
// browse-menu entry commits through the canvas's normal add path, which writes into the current slot.
export function SlotEmptyState({ slotLabel, catalog, onPickActivity, onBrowseAll }: {
  slotLabel: string;
  catalog: ActivityCatalogItem[];
  onPickActivity(activity: ActivityCatalogItem): void;
  onBrowseAll(anchor: { clientX: number; clientY: number }): void;
}) {
  const pickRootKind = (kind: CreateWorkflowKind) => {
    const activity = findRootKindActivity(catalog, kind);
    if (activity) onPickActivity(activity);
  };

  return (
    <div className="wf-slot-empty" role="group" aria-label={`Fill ${slotLabel}`}>
      <div className="wf-slot-empty-card">
        <p className="wf-slot-empty-title">This slot is empty</p>
        <p className="wf-slot-empty-hint">Choose a container for <strong>{slotLabel}</strong>, or pick any activity.</p>
        <WorkflowRootButtonCards onPick={pickRootKind} />
        <button
          type="button"
          className="wf-slot-empty-browse"
          onClick={event => onBrowseAll({ clientX: event.clientX, clientY: event.clientY })}
        >
          <Boxes size={15} /> Browse all activities…
        </button>
        <p className="wf-slot-empty-drag-hint">Tip: you can also drag activities from the palette onto the canvas.</p>
      </div>
    </div>
  );
}
