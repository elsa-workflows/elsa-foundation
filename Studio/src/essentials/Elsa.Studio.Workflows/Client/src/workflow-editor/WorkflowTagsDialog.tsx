import { useEffect, useRef, useState } from "react";
import { StudioAlert, StudioButton, StudioDialog, StudioDialogFooter, StudioField } from "@elsa-workflows/studio-ui";
import { RefreshCw } from "lucide-react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import {
  bulkEditWorkflowTags, getWorkflowOrganization, saveWorkflowOrganization,
  workflowOperationKey, workflowTagAssignmentKey,
  type WorkflowOrganization, type WorkflowTagAssignment, type WorkflowTagBulkOutcome
} from "../api/workflowOrganization";
import { WorkflowTagAssignmentsEditor } from "./WorkflowTagAssignmentsEditor";
import "./workflowOrganization.css";
import "./workflowOrganizationDialog.css";

export function WorkflowTagsDialog({ context, definitionId, name, onClose, onSaved }: {
  context: StudioEndpointContext; definitionId: string; name: string; onClose(): void; onSaved(): void;
}) {
  const [organization, setOrganization] = useState<WorkflowOrganization | null>(null);
  const [tags, setTags] = useState<WorkflowTagAssignment[]>([]);
  const [busy, setBusy] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [refresh, setRefresh] = useState(0);
  const [creatingTag, setCreatingTag] = useState(false);
  const operationKey = useRef(workflowOperationKey());
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
  return <StudioDialog
    open
    title={<>Tags for {name}</>}
    description="Add labels to make this workflow easier to find."
    className="wf-tokens wf-organization-dialog"
    onClose={onClose}
    dismissDisabled={busy || creatingTag}
    onEscapeKeyDown={keepExpandedTagSearchOpen}
    dismissOnOutsideClick={false}
    onSubmit={event => { event.preventDefault(); void save(); }}
    footer={<StudioDialogFooter
      leading={error ? <StudioButton disabled={busy || creatingTag} onClick={() => setRefresh(value => value + 1)}><RefreshCw size={14} aria-hidden="true" />Reload tags</StudioButton> : undefined}
      cancelLabel={organization?.permissions?.canAssignTags === false ? "Close" : "Cancel"}
      onCancel={onClose}
      cancelDisabled={busy || creatingTag}
      confirmLabel={busy ? "Saving…" : "Save tags"}
      confirmDisabled={busy || creatingTag || !changed || organization?.permissions?.canAssignTags !== true}
    />}
  >
    <div className="wf-organization-scroll">
      {loading ? <p role="status">Loading tags…</p> : null}
      {organization ? <>
        <WorkflowTagAssignmentsEditor context={context} assignments={tags} disabled={busy || organization.permissions?.canAssignTags !== true} onBusyChange={setCreatingTag} onChange={next => { setTags(next); operationKey.current = workflowOperationKey(); }} />
        {organization.permissions?.canAssignTags !== true ? <p className="wf-library-help">You can view these tags. Editing requires additional permission.</p> : null}
      </> : null}
      {error ? <StudioAlert tone="danger">{error}</StudioAlert> : null}
    </div>
  </StudioDialog>;
}

export function WorkflowBulkTagsDialog({ context, definitionIds, onClose, onSaved }: {
  context: StudioEndpointContext; definitionIds: string[]; onClose(): void; onSaved(): void;
}) {
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
  return <StudioDialog
    open
    title={<>Edit tags on {definitionIds.length} workflows</>}
    className="wf-tokens wf-organization-dialog"
    onClose={onClose}
    dismissDisabled={busy || creatingTag}
    onEscapeKeyDown={keepExpandedTagSearchOpen}
    dismissOnOutsideClick={false}
    onSubmit={event => { event.preventDefault(); if (preview) void apply(); else setPreview(true); }}
    footer={<StudioDialogFooter
      extra={preview && !outcomes ? <StudioButton disabled={busy} onClick={() => setPreview(false)}>Back to changes</StudioButton> : null}
      cancelLabel={outcomes ? "Close" : "Cancel"}
      onCancel={onClose}
      cancelDisabled={busy || creatingTag}
      confirmLabel={busy ? "Applying…" : preview ? "Apply tag changes" : "Preview changes"}
      confirmDisabled={busy || creatingTag || loading || !canEdit || !assignments.length || Boolean(outcomes)}
    />}
  >
    <div className="wf-organization-scroll">
      {loading ? <p role="status">Checking selected workflows and current revisions…</p> : null}
      {!loading && !canEdit ? <p role="alert">Every selected workflow must be available and permit tag changes. Close this dialog and adjust your selection, or reload after an access change.</p> : null}
      <StudioField label="Change"><select aria-label="Bulk tag change" disabled={busy || creatingTag || Boolean(outcomes)} value={action} onChange={event => { setAction(event.target.value as "add" | "remove"); changed(); }}><option value="add">Add tags</option><option value="remove">Remove tags</option></select></StudioField>
      <WorkflowTagAssignmentsEditor context={context} assignments={assignments} disabled={busy || !canEdit || Boolean(outcomes)} allowRetired={action === "remove"} onBusyChange={setCreatingTag} onChange={next => { setAssignments(next); changed(); }} />
      {preview ? <div className="wf-library-preview" role="region" aria-label="Bulk tag preview"><p>{action === "add" ? "Add" : "Remove"} {assignments.length} tag assignment{assignments.length === 1 ? "" : "s"} {action === "add" ? "to" : "from"} these {targets.length} explicitly selected workflows.</p><ul>{targets.map(target => <li key={target.definitionId}><code>{target.definitionId}</code></li>)}</ul><p>Each workflow is checked against its inspected revision. Other tags are retained.</p></div> : null}
      {outcomes ? <div role="status" aria-label="Bulk tag results"><p>Bulk tag results</p><ul>{outcomes.map(outcome => <li key={outcome.definitionId}><code>{outcome.definitionId}</code>: {outcome.status}{outcome.message ? ` — ${outcome.message}` : ""}</li>)}</ul><button type="button" disabled={busy} onClick={() => { setRefresh(value => value + 1); setOutcomes(null); setPreview(false); keyRef.current = workflowOperationKey(); }}>Reload targets for another edit</button></div> : null}
      {error ? <StudioAlert tone="danger">{error}</StudioAlert> : null}
    </div>
  </StudioDialog>;
}


function keepExpandedTagSearchOpen(event: KeyboardEvent) {
  if (event.target instanceof HTMLElement && event.target.matches('[role="combobox"][aria-expanded="true"]')) event.preventDefault();
}

function assignmentSignature(assignments: WorkflowTagAssignment[]) { return JSON.stringify(assignments.map(workflowTagAssignmentKey).sort()); }
function errorMessage(error: unknown) { return error instanceof Error ? error.message : "The server could not complete this request."; }
