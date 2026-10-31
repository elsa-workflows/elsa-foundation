import { useId, useRef, useState } from "react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { createWorkflowTag, getWorkflowTag, updateWorkflowTag, workflowOperationKey, type WorkflowTag, type WorkflowTagInput } from "../api/workflowOrganization";
import { useDialogFocus } from "./useDialogFocus";

const newTag = (): WorkflowTagInput => ({ name: "", description: "", mode: "marker", cardinality: "single", values: [], deprecated: false });

export function WorkflowTagCatalogDialog({ context, tags, onClose, onSaved }: {
  context: StudioEndpointContext; tags: WorkflowTag[]; onClose(): void; onSaved(): void;
}) {
  const ref = useRef<HTMLElement>(null);
  const titleId = useId();
  const [editingId, setEditingId] = useState<string | null>(null);
  const [draft, setDraft] = useState<WorkflowTagInput>(newTag);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [status, setStatus] = useState("");
  useDialogFocus(ref, busy ? null : onClose);

  const choose = (id: string) => {
    const tag = tags.find(item => item.id === id);
    setEditingId(tag?.id ?? null);
    setDraft(tag ? { ...tag, values: tag.values.map(value => ({ ...value })) } : newTag());
    setError(""); setStatus("");
  };
  const save = async () => {
    if (busy) return;
    setBusy(true); setError(""); setStatus("");
    const input = { ...draft, name: draft.name.trim(), values: draft.mode === "marker" ? [] : draft.values, cardinality: draft.mode === "marker" ? "single" as const : draft.cardinality };
    try {
      const tag = editingId ? await updateWorkflowTag(context, editingId, input) : await createWorkflowTag(context, input);
      setEditingId(tag.id); setDraft(tag); setStatus(`Saved tag ${tag.name}`); onSaved();
    } catch (caught) { setError(`Couldn't save the tag. ${caught instanceof Error ? caught.message : "Check your access and values, then retry."} Reload the tag after a conflict before changing it again.`); }
    finally { setBusy(false); }
  };
  const reload = async () => {
    if (!editingId) return;
    setBusy(true); setError(""); setStatus("");
    try {
      const current = await getWorkflowTag(context, editingId);
      setDraft(current); onSaved(); setStatus("Loaded the current tag. Review its values before saving.");
    } catch (caught) { setError(caught instanceof Error ? caught.message : "Couldn't reload the selected tag."); }
    finally { setBusy(false); }
  };
  return <div className="wf-dialog-backdrop" role="presentation"><section ref={ref} className="wf-dialog wf-organization-dialog" role="dialog" aria-modal="true" aria-labelledby={titleId} tabIndex={-1}>
    <form onSubmit={event => { event.preventDefault(); void save(); }}>
      <h3 id={titleId}>Manage workflow tags</h3>
      <div className="wf-organization-scroll">
        <label>Tag<select aria-label="Tag to manage" value={editingId ?? ""} disabled={busy} onChange={event => choose(event.target.value)}><option value="">Create a new tag</option>{tags.map(tag => <option key={tag.id} value={tag.id}>{tag.name}{tag.deprecated ? " (retired)" : ""}</option>)}</select></label>
        <label>Name<input aria-label="Tag name" required maxLength={200} value={draft.name} disabled={busy} onChange={event => setDraft(current => ({ ...current, name: event.target.value }))} /></label>
        <label>Description<textarea aria-label="Tag description" value={draft.description ?? ""} maxLength={1000} disabled={busy} onChange={event => setDraft(current => ({ ...current, description: event.target.value }))} /></label>
        <label>Kind<select aria-label="Tag kind" value={draft.mode} disabled={busy || Boolean(editingId)} onChange={event => setDraft(current => ({ ...current, mode: event.target.value as WorkflowTag["mode"], values: [] }))}><option value="marker">Simple label</option><option value="controlled">Controlled values</option></select></label>
        {draft.mode === "controlled" ? <>
          <label>Values allowed<select aria-label="Tag cardinality" value={draft.cardinality} disabled={busy || Boolean(editingId)} onChange={event => setDraft(current => ({ ...current, cardinality: event.target.value as WorkflowTag["cardinality"] }))}><option value="single">One value per workflow</option><option value="multiple">Several values per workflow</option></select></label>
          <fieldset className="wf-tag-value-editor"><legend>Controlled values</legend>
            {(draft.values ?? []).map((value, index) => <div className="wf-library-control-row" key={value.id}>
              <label>Value name<input aria-label={`Value name ${index + 1}`} required maxLength={200} value={value.name} disabled={busy} onChange={event => setDraft(current => ({ ...current, values: current.values?.map((item, position) => position === index ? { ...item, name: event.target.value } : item) }))} /></label>
              <label className="wf-library-check"><input type="checkbox" checked={value.deprecated ?? false} disabled={busy} onChange={event => setDraft(current => ({ ...current, values: current.values?.map((item, position) => position === index ? { ...item, deprecated: event.target.checked } : item) }))} />Retired</label>
            </div>)}
            <button type="button" disabled={busy || (draft.values?.length ?? 0) >= 100} onClick={() => setDraft(current => ({ ...current, values: [...(current.values ?? []), { id: workflowOperationKey(), name: "", deprecated: false }] }))}>Add controlled value</button>
          </fieldset>
        </> : null}
        <label className="wf-library-check"><input type="checkbox" checked={draft.deprecated ?? false} disabled={busy} onChange={event => setDraft(current => ({ ...current, deprecated: event.target.checked }))} /> Retire this tag</label>
        <p className="wf-library-help">Retired tags and values remain on existing workflows and in filters. They cannot be newly assigned. A rename preserves identity.</p>
        {editingId ? <button type="button" disabled={busy} onClick={() => void reload()}>Reload selected tag</button> : null}
        {error ? <p role="alert">{error}</p> : null}
        {status ? <p role="status">{status}</p> : null}
      </div>
      <div className="wf-dialog-actions"><button type="button" disabled={busy} onClick={onClose}>Close</button><button type="submit" disabled={busy || !draft.name.trim()}>{busy ? "Saving…" : editingId ? "Save tag" : "Create tag"}</button></div>
    </form>
  </section></div>;
}
