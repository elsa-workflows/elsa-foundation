import { useEffect, useRef, useState } from "react";
import { StudioButton, StudioPage, StudioPageHeader } from "@elsa-workflows/studio-ui";
import { useStudioWorkspaceWhenActive, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { createWorkflowTag, getWorkflowTag, updateWorkflowTag, workflowOperationKey, type WorkflowTag, type WorkflowTagInput } from "../api/workflowOrganization";
import { getNotifications } from "./notifications";
import "./workflowTags.css";

const newTag = (): WorkflowTagInput => ({ name: "", description: "", mode: "marker", cardinality: "single", values: [], deprecated: false });

export function WorkflowTagEditor({ context, tag, onBack, onCreated }: {
  context: StudioEndpointContext; tag?: WorkflowTag; onBack(): void; onCreated(tag: WorkflowTag): void;
}) {
  const whenActive = useStudioWorkspaceWhenActive();
  const active = useRef(true);
  useEffect(() => { active.current = true; return () => { active.current = false; }; }, []);
  const editingId = tag?.id;
  const serverValueIds = useRef(new Set(tag?.values.map(value => value.id) ?? []));
  const [draft, setDraft] = useState<WorkflowTagInput>(() => tag ? { ...tag, values: tag.values.map(value => ({ ...value })) } : newTag());
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [status, setStatus] = useState("");
  // Queue one completion while a workspace tab is hidden, including its busy reset.
  const complete = (action: () => void) => whenActive(() => {
    if (!active.current) return;
    action();
    setBusy(false);
  });
  const save = async () => {
    if (busy) return;
    setBusy(true); setError(""); setStatus("");
    const input = { ...draft, name: draft.name.trim(), values: draft.mode === "marker" ? [] : (draft.values ?? []).map(value => ({ ...value, id: serverValueIds.current.has(value.id) ? value.id : "" })), cardinality: draft.mode === "marker" ? "single" as const : draft.cardinality };
    try {
      const tag = editingId ? await updateWorkflowTag(context, editingId, input) : await createWorkflowTag(context, input);
      complete(() => {
        serverValueIds.current = new Set(tag.values.map(value => value.id));
        setDraft(tag); getNotifications().notify({ level: "success", title: `Saved tag ${tag.name}` });
        if (!editingId) onCreated(tag);
      });
    } catch (caught) { complete(() => setError(`Couldn't save the tag. ${caught instanceof Error ? caught.message : "Check your access and values, then retry."} Reload the tag after a conflict before changing it again.`)); }
  };
  const reload = async () => {
    if (!editingId) return;
    setBusy(true); setError(""); setStatus("");
    try {
      const current = await getWorkflowTag(context, editingId);
      complete(() => {
        serverValueIds.current = new Set(current.values.map(value => value.id));
        setDraft(current); setStatus("Loaded the current tag. Review its values before saving.");
      });
    } catch (caught) { complete(() => setError(caught instanceof Error ? caught.message : "Couldn't reload the selected tag.")); }
  };
  return <StudioPage className="wf-tag-page" aria-labelledby="workflow-tag-title">
    <StudioPageHeader title={editingId ? "Edit workflow tag" : "Create workflow tag"} titleId="workflow-tag-title"
      description={editingId ? "Update this tag and its values." : "Create a reusable label or controlled classification for workflows."}
      actions={<StudioButton onClick={onBack} disabled={busy}>Back to tags</StudioButton>} />
    <form className="wf-tag-form" onSubmit={event => { event.preventDefault(); void save(); }}>
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
            <StudioButton type="button" disabled={busy || (draft.values?.length ?? 0) >= 100} onClick={() => setDraft(current => ({ ...current, values: [...(current.values ?? []), { id: workflowOperationKey(), name: "", deprecated: false }] }))}>Add controlled value</StudioButton>
          </fieldset>
        </> : null}
        <label className="wf-library-check"><input type="checkbox" checked={draft.deprecated ?? false} disabled={busy} onChange={event => setDraft(current => ({ ...current, deprecated: event.target.checked }))} /> Retire this tag</label>
        <p className="wf-library-help">Retired tags and values remain on existing workflows and in filters. They cannot be newly assigned. A rename preserves identity.</p>
        {editingId ? <StudioButton type="button" disabled={busy} onClick={() => void reload()}>Reload tag</StudioButton> : null}
        {error ? <p role="alert">{error}</p> : null}
        {status ? <p role="status">{status}</p> : null}
      <div className="studio-page-actions">
        <StudioButton type="button" onClick={onBack} disabled={busy}>Cancel</StudioButton>
        <StudioButton variant="primary" type="submit" disabled={busy || !draft.name.trim()}>{busy ? "Saving…" : editingId ? "Save tag" : "Create tag"}</StudioButton>
      </div>
    </form>
  </StudioPage>;
}
