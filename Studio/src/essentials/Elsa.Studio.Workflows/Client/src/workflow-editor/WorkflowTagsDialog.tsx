import { useEffect, useId, useRef, useState } from "react";
import { StudioButton, StudioDialogFooter } from "@elsa-workflows/studio-ui";
import { RefreshCw } from "lucide-react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import {
  bulkEditWorkflowTags, getWorkflowOrganization, saveWorkflowOrganization,
  workflowOperationKey, workflowTagAssignmentKey,
  type WorkflowOrganization, type WorkflowTagAssignment, type WorkflowTagBulkOutcome
} from "../api/workflowOrganization";
import { WorkflowTagAssignmentsEditor } from "./WorkflowTagAssignmentsEditor";
import { useDialogFocus } from "./useDialogFocus";
import "./workflowOrganization.css";
import "./workflowOrganizationDialog.css";

export function WorkflowTagsDialog({ context, definitionId, name, onClose, onSaved }: {
  context: StudioEndpointContext; definitionId: string; name: string; onClose(): void; onSaved(): void;
}) {
  const ref = useRef<HTMLElement>(null);
  const titleId = useId();
  const [organization, setOrganization] = useState<WorkflowOrganization | null>(null);
  const [tags, setTags] = useState<WorkflowTagAssignment[]>([]);
  const [busy, setBusy] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [refresh, setRefresh] = useState(0);
  const [creatingTag, setCreatingTag] = useState(false);
  const operationKey = useRef(workflowOperationKey());
  useDialogFocus(ref, busy || creatingTag ? null : onClose);
  useEffect(() => {
    let active = true;
    setLoading(true); setError(""); setOrganization(null);
    void getWorkflowOrganization(context, definitionId).then(value => {
      if (!active) return;
      setOrganization(value); setTags(value.tags); operationKey.current = workflowOperationKey();
    }).catch(caught => { if (active) setError(`Couldn't load workflow tags. ${errorMessage(caught)}`); })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, [context, definitionId, refresh]);
  const changed = assignmentSignature(tags) !== assignmentSignature(organization?.tags ?? []);
  const save = async () => {
    if (!organization || busy || creatingTag || organization.permissions?.canAssignTags !== true) return;
    setBusy(true); setError("");
    try { await saveWorkflowOrganization(context, organization, tags, operationKey.current); onSaved(); onClose(); }
    catch (caught) { setError(`Couldn't save tags. ${errorMessage(caught)} Your changes are retained. Reload tags to review a concurrent change.`); }
    finally { setBusy(false); }
  };
  return <div className="wf-dialog-backdrop" role="presentation"><section ref={ref} className="wf-dialog wf-organization-dialog" role="dialog" aria-modal="true" aria-labelledby={titleId} tabIndex={-1}>
    <form onSubmit={event => { event.preventDefault(); void save(); }}>
      <header className="wf-organization-heading"><h3 id={titleId}>Tags for {name}</h3><p>Add labels to make this workflow easier to find.</p></header>
      <div className="wf-organization-scroll">
        {loading ? <p role="status">Loading tags…</p> : null}
        {organization ? <>
          <WorkflowTagAssignmentsEditor context={context} assignments={tags} disabled={busy || organization.permissions?.canAssignTags !== true} onBusyChange={setCreatingTag} onChange={next => { setTags(next); operationKey.current = workflowOperationKey(); }} />
          {organization.permissions?.canAssignTags !== true ? <p className="wf-library-help">You can view these tags. Editing requires additional permission.</p> : null}
        </> : null}
        {error ? <p role="alert">{error}</p> : null}
      </div>
      <StudioDialogFooter
        className="wf-organization-footer"
        leading={error ? <StudioButton disabled={busy || creatingTag} onClick={() => setRefresh(value => value + 1)}><RefreshCw size={14} aria-hidden="true" />Reload tags</StudioButton> : undefined}
        cancelLabel="Close"
        onCancel={onClose}
        cancelDisabled={busy || creatingTag}
        confirmLabel={busy ? "Saving…" : "Save tags"}
        confirmDisabled={busy || creatingTag || !changed || organization?.permissions?.canAssignTags !== true}
      />
    </form>
  </section></div>;
}

export function WorkflowBulkTagsDialog({ context, definitionIds, onClose, onSaved }: {
  context: StudioEndpointContext; definitionIds: string[]; onClose(): void; onSaved(): void;
}) {
  const ref = useRef<HTMLElement>(null);
  const titleId = useId();
  const [targets, setTargets] = useState<WorkflowOrganization[]>([]);
  const [assignments, setAssignments] = useState<WorkflowTagAssignment[]>([]);
  const [creatingTag, setCreatingTag] = useState(false);
  const [action, setAction] = useState<"add" | "remove">("add");
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [preview, setPreview] = useState(false);
  const [error, setError] = useState("");
  const [outcomes, setOutcomes] = useState<WorkflowTagBulkOutcome[] | null>(null);
  const [refresh, setRefresh] = useState(0);
  const keyRef = useRef(workflowOperationKey());
  const targetKey = JSON.stringify(definitionIds);
  useDialogFocus(ref, busy || creatingTag ? null : onClose);
  useEffect(() => {
    let active = true;
    const ids = JSON.parse(targetKey) as string[];
    setLoading(true); setTargets([]); setError("");
    if (ids.length > 100) { setError("Choose at most 100 workflows for one bulk tag edit."); setLoading(false); return; }
    void Promise.all(ids.map(id => getWorkflowOrganization(context, id))).then(values => { if (active) setTargets(values); })
      .catch(caught => { if (active) setError(`Couldn't inspect every selected workflow. ${errorMessage(caught)}`); })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, [context, targetKey, refresh]);
  const canEdit = targets.length > 0 && targets.every(target => target.permissions?.canAssignTags === true);
  const changed = () => { setPreview(false); setOutcomes(null); keyRef.current = workflowOperationKey(); };
  const apply = async () => {
    if (!preview || !canEdit || busy || creatingTag) return;
    setBusy(true); setError("");
    try {
      const result = await bulkEditWorkflowTags(context, { targets: targets.map(target => ({ definitionId: target.definitionId, revision: target.revision })), add: action === "add" ? assignments : [], remove: action === "remove" ? assignments : [], operationKey: keyRef.current });
      setOutcomes(Array.isArray(result) ? result : result.outcomes ?? result.items ?? []);
      onSaved();
    } catch (caught) { setError(`Couldn't complete this bulk edit. ${errorMessage(caught)} Retry uses the same operation identity.`); }
    finally { setBusy(false); }
  };
  return <div className="wf-dialog-backdrop" role="presentation"><section ref={ref} className="wf-dialog wf-organization-dialog" role="dialog" aria-modal="true" aria-labelledby={titleId} tabIndex={-1}>
    <form onSubmit={event => { event.preventDefault(); if (preview) void apply(); else setPreview(true); }}>
      <h3 id={titleId}>Edit tags on {definitionIds.length} workflows</h3>
      <div className="wf-organization-scroll">
        {loading ? <p role="status">Checking selected workflows and current revisions…</p> : null}
        {!loading && !canEdit ? <p role="alert">Every selected workflow must be available and permit tag changes. Close this dialog and adjust your selection, or reload after an access change.</p> : null}
        <label>Change<select aria-label="Bulk tag change" disabled={busy || creatingTag || Boolean(outcomes)} value={action} onChange={event => { setAction(event.target.value as "add" | "remove"); changed(); }}><option value="add">Add tags</option><option value="remove">Remove tags</option></select></label>
        <WorkflowTagAssignmentsEditor context={context} assignments={assignments} disabled={busy || !canEdit || Boolean(outcomes)} allowRetired={action === "remove"} onBusyChange={setCreatingTag} onChange={next => { setAssignments(next); changed(); }} />
        {preview ? <div className="wf-library-preview" role="region" aria-label="Bulk tag preview"><p>{action === "add" ? "Add" : "Remove"} {assignments.length} tag assignment{assignments.length === 1 ? "" : "s"} {action === "add" ? "to" : "from"} these {targets.length} explicitly selected workflows.</p><ul>{targets.map(target => <li key={target.definitionId}><code>{target.definitionId}</code></li>)}</ul><p>Each workflow is checked against its inspected revision. Other tags are retained.</p></div> : null}
        {outcomes ? <div role="status" aria-label="Bulk tag results"><p>Bulk tag results</p><ul>{outcomes.map(outcome => <li key={outcome.definitionId}><code>{outcome.definitionId}</code>: {outcome.status}{outcome.message ? ` — ${outcome.message}` : ""}</li>)}</ul><button type="button" disabled={busy} onClick={() => { setRefresh(value => value + 1); setOutcomes(null); setPreview(false); keyRef.current = workflowOperationKey(); }}>Reload targets for another edit</button></div> : null}
        {error ? <p role="alert">{error}</p> : null}
      </div>
      <StudioDialogFooter
        className="wf-dialog-actions"
        extra={preview && !outcomes ? <StudioButton disabled={busy} onClick={() => setPreview(false)}>Back to changes</StudioButton> : null}
        cancelLabel="Close"
        onCancel={onClose}
        cancelDisabled={busy || creatingTag}
        confirmLabel={busy ? "Applying…" : preview ? "Apply tag changes" : "Preview changes"}
        confirmDisabled={busy || creatingTag || loading || !canEdit || !assignments.length || Boolean(outcomes)}
      />
    </form>
  </section></div>;
}


function assignmentSignature(assignments: WorkflowTagAssignment[]) { return JSON.stringify(assignments.map(workflowTagAssignmentKey).sort()); }
function errorMessage(error: unknown) { return error instanceof Error ? error.message : "The server could not complete this request."; }
