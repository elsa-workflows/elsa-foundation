import { useId, useRef, useState } from "react";
import type { ActivityCatalogItem, WorkflowDraft } from "../workflowTypes";
import { createActivityNode, getChildSlots, replaceSlotActivities, resolveScope, sequenceStructureKind, updateScopeActivities, type ScopeFrame } from "../workflowAdapter";
import { activityDisplayNameMaxLength, indexActivityPresentation, resolveActivityLabel, updateActivityPresentation } from "../activityPresentation";
import { getWorkflowStartEnabled } from "../workflowStart";
import { createNodeId } from "./editorHelpers";
import { useDialogFocus } from "./useDialogFocus";

// Private S001 experiment. This dialog is removed before the prototype PR is finalized;
// T002/T003 own the production refactoring planner and equivalent editor integration.
export function BlockPrototypeDialog({ draft, frames, catalog, sequenceActivity, initialSelection, disabled, onApply, onClose }: {
  draft: WorkflowDraft;
  frames: ScopeFrame[];
  catalog: Map<string, ActivityCatalogItem>;
  sequenceActivity: ActivityCatalogItem;
  initialSelection: string[];
  disabled: boolean;
  onApply(next: WorkflowDraft, blockId: string): void;
  onClose(): void;
}) {
  const id = useId();
  const ref = useRef<HTMLElement>(null);
  useDialogFocus(ref, onClose);
  const scope = resolveScope(draft.state.rootActivity, frames, catalog);
  const activities = scope?.slot.activities ?? [];
  const [name, setName] = useState("Prepare shipment");
  const [selection, setSelection] = useState(() => new Set(initialSelection.length ? initialSelection : activities.slice(0, 2).map(node => node.nodeId)));
  const [error, setError] = useState("");
  const presentation = indexActivityPresentation(draft.activityPresentation);
  const apply = () => {
    setError("");
    if (disabled) return;
    if (!draft.state.rootActivity || scope?.owner.structure?.kind !== sequenceStructureKind || scope.slot.cardinality !== "many") {
      setError("This prototype supports native Sequence steps."); return;
    }
    const indexes = activities.flatMap((node, index) => selection.has(node.nodeId) ? [index] : []);
    if (!indexes.length || indexes.length !== selection.size || indexes.some((index, offset) => index !== indexes[0] + offset)) {
      setError("Select one contiguous set of steps."); return;
    }
    const selected = indexes.map(index => activities[index]);
    if (selected.some(node => getWorkflowStartEnabled(node, catalog.get(node.activityVersionId)))) {
      setError("A workflow start cannot be extracted by this prototype."); return;
    }
    const blockId = createNodeId(sequenceActivity);
    const empty = createActivityNode(sequenceActivity, blockId);
    const slot = getChildSlots(empty, catalog).find(candidate => candidate.mode === "sequence" && candidate.cardinality === "many");
    if (!slot) { setError("The authorized Sequence body is unavailable."); return; }
    const block = replaceSlotActivities(empty, slot, selected);
    const nextActivities = [...activities.slice(0, indexes[0]), block, ...activities.slice(indexes.at(-1)! + 1)];
    const firstPosition = draft.layout.find(record => record.nodeId === selected[0].nodeId);
    onApply({
      ...draft,
      state: { ...draft.state, rootActivity: updateScopeActivities(draft.state.rootActivity, frames, nextActivities, catalog) },
      layout: [...draft.layout, { nodeId: blockId, x: firstPosition?.x ?? 0, y: firstPosition?.y ?? 0 }],
      activityPresentation: updateActivityPresentation(draft.activityPresentation ?? [], blockId, { displayName: name.trim() })
    }, blockId);
  };
  return <div className="wf-dialog-backdrop"><section ref={ref} tabIndex={-1} className="wf-dialog" role="dialog" aria-modal="true" aria-labelledby={`${id}-title`}>
    <form onSubmit={event => { event.preventDefault(); if (name.trim()) apply(); }}>
      <h3 id={`${id}-title`}>Extract into block</h3>
      <p>Select contiguous steps. Their identities and existing bindings stay with the embedded body.</p>
      <label className="wf-form-field">Block name<input required maxLength={activityDisplayNameMaxLength} value={name} onChange={event => setName(event.target.value)} /></label>
      <fieldset disabled={disabled}><legend>Steps to extract</legend>
        {activities.map(node => <label key={node.nodeId} className="wf-form-field">
          <span><input type="checkbox" checked={selection.has(node.nodeId)} onChange={event => setSelection(previous => {
            const next = new Set(previous); if (event.target.checked) next.add(node.nodeId); else next.delete(node.nodeId); return next;
          })} /> {resolveActivityLabel(presentation.get(node.nodeId), catalog.get(node.activityVersionId), node.activityVersionId)}</span>
        </label>)}
      </fieldset>
      {error ? <p role="alert">{error}</p> : null}
      <div className="wf-dialog-actions"><button type="button" onClick={onClose}>Cancel</button><button type="submit" className="wf-primary-button" disabled={disabled || !name.trim()}>Extract</button></div>
    </form>
  </section></div>;
}
