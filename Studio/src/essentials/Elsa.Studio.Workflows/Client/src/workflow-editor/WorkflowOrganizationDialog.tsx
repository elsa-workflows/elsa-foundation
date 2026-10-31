import { useEffect, useId, useMemo, useRef, useState } from "react";
import { StudioButton } from "@elsa-workflows/studio-ui";
import { FolderTree, RefreshCw, Search, Tags } from "lucide-react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import {
  bulkEditWorkflowTags, getWorkflowOrganization, getWorkflowTag, listWorkflowTags, saveWorkflowOrganization,
  workflowOperationKey, workflowTagAssignmentKey,
  type WorkflowOrganization, type WorkflowTag, type WorkflowTagAssignment, type WorkflowTagBulkOutcome
} from "../api/workflowOrganization";
import { MoveWorkflowDefinitionsDialog } from "./MoveWorkflowDefinitionsDialog";
import { useDialogFocus } from "./useDialogFocus";
import "./workflowOrganization.css";
import "./workflowOrganizationDialog.css";

export function WorkflowOrganizationDialog({ context, definitionId, name, onClose, onSaved }: {
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
  const [moveOpen, setMoveOpen] = useState(false);
  const operationKey = useRef(workflowOperationKey());
  useDialogFocus(ref, busy ? null : onClose, true, undefined, !moveOpen);
  useEffect(() => {
    let active = true;
    setLoading(true); setError(""); setOrganization(null);
    void getWorkflowOrganization(context, definitionId).then(value => {
      if (!active) return;
      setOrganization(value); setTags(value.tags); operationKey.current = workflowOperationKey();
    }).catch(caught => { if (active) setError(`Couldn't load workflow organization. ${errorMessage(caught)}`); })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, [context, definitionId, refresh]);
  const changed = assignmentSignature(tags) !== assignmentSignature(organization?.tags ?? []);
  const save = async () => {
    if (!organization || busy || organization.permissions?.canAssignTags !== true) return;
    setBusy(true); setError("");
    try { await saveWorkflowOrganization(context, organization, tags, operationKey.current); onSaved(); onClose(); }
    catch (caught) { setError(`Couldn't save tags. ${errorMessage(caught)} Your changes are retained. Reload organization to review a concurrent change.`); }
    finally { setBusy(false); }
  };
  if (moveOpen) return <MoveWorkflowDefinitionsDialog context={context} definitionIds={[definitionId]} onClose={() => setMoveOpen(false)} onMoved={() => { onSaved(); setRefresh(value => value + 1); }} />;
  return <div className="wf-dialog-backdrop" role="presentation"><section ref={ref} className="wf-dialog wf-organization-dialog" role="dialog" aria-modal="true" aria-labelledby={titleId} tabIndex={-1}>
    <form onSubmit={event => { event.preventDefault(); void save(); }}>
      <header className="wf-organization-heading"><h3 id={titleId}>Organize {name}</h3><p>Choose where this workflow belongs and how to find it.</p></header>
      <div className="wf-organization-scroll">
        {loading ? <p role="status">Loading organization…</p> : null}
        {organization ? <>
          <section className="wf-organization-folder" aria-label="Home folder"><div><h4><FolderTree size={16} aria-hidden="true" />Home folder</h4><p>{organization.folderBreadcrumb.map(folder => folder.name).join(" / ") || "Unfiled"}</p></div>
            {organization.permissions?.canManageFolders === true ? <StudioButton disabled={busy || changed} onClick={() => setMoveOpen(true)}>Move to folder</StudioButton> : null}
          </section>
          {changed ? <p className="wf-library-help">Save or reload your tag changes before moving this workflow.</p> : null}
          <WorkflowTagAssignmentsEditor context={context} assignments={tags} disabled={busy || organization.permissions?.canAssignTags !== true} onChange={next => { setTags(next); operationKey.current = workflowOperationKey(); }} />
          {organization.permissions?.canAssignTags !== true ? <p className="wf-library-help">You can view organization. Tag changes require additional permission.</p> : null}
        </> : null}
        {error ? <p role="alert">{error}</p> : null}
      </div>
      <footer className="wf-organization-footer">{!loading ? <StudioButton variant="ghost" size="sm" disabled={busy} onClick={() => setRefresh(value => value + 1)}><RefreshCw size={14} aria-hidden="true" />Reload organization</StudioButton> : <span />}<div className="wf-organization-actions"><StudioButton disabled={busy} onClick={onClose}>Close</StudioButton><StudioButton variant="primary" type="submit" disabled={busy || !changed || organization?.permissions?.canAssignTags !== true}>{busy ? "Saving…" : "Save tags"}</StudioButton></div></footer>
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
  const [action, setAction] = useState<"add" | "remove">("add");
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [preview, setPreview] = useState(false);
  const [error, setError] = useState("");
  const [outcomes, setOutcomes] = useState<WorkflowTagBulkOutcome[] | null>(null);
  const [refresh, setRefresh] = useState(0);
  const keyRef = useRef(workflowOperationKey());
  const targetKey = JSON.stringify(definitionIds);
  useDialogFocus(ref, busy ? null : onClose);
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
    if (!preview || !canEdit || busy) return;
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
        <label>Change<select aria-label="Bulk tag change" disabled={busy || Boolean(outcomes)} value={action} onChange={event => { setAction(event.target.value as "add" | "remove"); changed(); }}><option value="add">Add tags</option><option value="remove">Remove tags</option></select></label>
        <WorkflowTagAssignmentsEditor context={context} assignments={assignments} disabled={busy || !canEdit || Boolean(outcomes)} allowRetired={action === "remove"} onChange={next => { setAssignments(next); changed(); }} />
        {preview ? <div className="wf-library-preview" role="region" aria-label="Bulk tag preview"><p>{action === "add" ? "Add" : "Remove"} {assignments.length} tag assignment{assignments.length === 1 ? "" : "s"} {action === "add" ? "to" : "from"} these {targets.length} explicitly selected workflows.</p><ul>{targets.map(target => <li key={target.definitionId}><code>{target.definitionId}</code></li>)}</ul><p>Each workflow is checked against its inspected revision. Other tags are retained.</p></div> : null}
        {outcomes ? <div role="status" aria-label="Bulk tag results"><p>Bulk tag results</p><ul>{outcomes.map(outcome => <li key={outcome.definitionId}><code>{outcome.definitionId}</code>: {outcome.status}{outcome.message ? ` — ${outcome.message}` : ""}</li>)}</ul><button type="button" disabled={busy} onClick={() => { setRefresh(value => value + 1); setOutcomes(null); setPreview(false); keyRef.current = workflowOperationKey(); }}>Reload targets for another edit</button></div> : null}
        {error ? <p role="alert">{error}</p> : null}
      </div>
      <div className="wf-dialog-actions"><button type="button" disabled={busy} onClick={onClose}>Close</button>{preview && !outcomes ? <button type="button" disabled={busy} onClick={() => setPreview(false)}>Back to changes</button> : null}<button type="submit" disabled={busy || loading || !canEdit || !assignments.length || Boolean(outcomes)}>{busy ? "Applying…" : preview ? "Apply tag changes" : "Preview changes"}</button></div>
    </form>
  </section></div>;
}

function WorkflowTagAssignmentsEditor({ context, assignments, disabled, allowRetired = false, onChange }: {
  context: StudioEndpointContext; assignments: WorkflowTagAssignment[]; disabled: boolean; allowRetired?: boolean; onChange(next: WorkflowTagAssignment[]): void;
}) {
  const [catalog, setCatalog] = useState<WorkflowTag[]>([]);
  const [cursor, setCursor] = useState<string | null>(null);
  const [search, setSearch] = useState("");
  const [term, setTerm] = useState("");
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const generationRef = useRef(0);
  useEffect(() => {
    const generation = ++generationRef.current;
    setLoading(true); setError("");
    void listWorkflowTags(context, term).then(page => {
      if (generation !== generationRef.current) return;
      setCatalog(current => [...new Map([...current, ...page.items].map(tag => [tag.id, tag])).values()]); setCursor(page.nextContinuationToken ?? null);
    }).catch(caught => { if (generation === generationRef.current) setError(errorMessage(caught)); })
      .finally(() => { if (generation === generationRef.current) setLoading(false); });
    return () => { generationRef.current += 1; };
  }, [context, term]);
  const missingTagIds = !loading ? JSON.stringify([...new Set(assignments.map(assignment => assignment.tagId))].filter(id => !catalog.some(tag => tag.id === id))) : "[]";
  useEffect(() => {
    const ids = JSON.parse(missingTagIds) as string[];
    if (!ids.length) return;
    let active = true;
    void Promise.allSettled(ids.map(id => getWorkflowTag(context, id))).then(results => {
      if (!active) return;
      const found = results.flatMap(result => result.status === "fulfilled" ? [result.value] : []);
      if (found.length) setCatalog(current => [...new Map([...current, ...found].map(tag => [tag.id, tag])).values()]);
    });
    return () => { active = false; };
  }, [context, missingTagIds]);
  const selected = useMemo(() => new Set(assignments.map(workflowTagAssignmentKey)), [assignments]);
  const toggle = (assignment: WorkflowTagAssignment, enabled: boolean, single: boolean) => {
    const key = workflowTagAssignmentKey(assignment);
    onChange(enabled ? [...assignments.filter(item => !single || item.tagId !== assignment.tagId), assignment] : assignments.filter(item => workflowTagAssignmentKey(item) !== key));
  };
  const loadMore = async () => {
    if (!cursor || loading) return;
    const generation = generationRef.current;
    setLoading(true);
    try { const page = await listWorkflowTags(context, term, cursor); if (generation !== generationRef.current) return; setCatalog(current => [...new Map([...current, ...page.items].map(tag => [tag.id, tag])).values()]); setCursor(page.nextContinuationToken ?? null); }
    catch (caught) { if (generation === generationRef.current) setError(errorMessage(caught)); }
    finally { if (generation === generationRef.current) setLoading(false); }
  };
  return <fieldset className="wf-tag-assignments" disabled={disabled}><legend><Tags size={16} aria-hidden="true" />Tags</legend>
    <div className="wf-tag-search"><label>Find tag<input type="search" placeholder="Search tags…" aria-label="Find workflow tags" value={search} onChange={event => setSearch(event.target.value)} enterKeyHint="search" onKeyDown={event => {
      if (event.key === "Enter" && !event.nativeEvent.isComposing) {
        event.preventDefault();
        if (!loading) setTerm(search);
      }
    }} /></label><StudioButton disabled={loading} onClick={() => setTerm(search)}><Search size={16} aria-hidden="true" />Find</StudioButton></div>
    {catalog.map(tag => tag.mode === "marker" ? <label className="wf-library-check" key={tag.id}><input type="checkbox" checked={selected.has(workflowTagAssignmentKey({ tagId: tag.id }))} disabled={!allowRetired && tag.deprecated && !selected.has(workflowTagAssignmentKey({ tagId: tag.id }))} onChange={event => toggle({ tagId: tag.id }, event.target.checked, true)} />{tag.name}{tag.deprecated ? " (retired)" : ""}</label> :
      <fieldset className="wf-library-values" key={tag.id}><legend>{tag.name}{tag.deprecated ? " (retired)" : ""}{tag.cardinality === "single" && !allowRetired ? " · choose one" : ""}</legend>{tag.values.map(value => {
        const assignment = { tagId: tag.id, valueId: value.id };
        const checked = selected.has(workflowTagAssignmentKey(assignment));
        return <label key={value.id}><input type="checkbox" checked={checked} disabled={!allowRetired && (tag.deprecated || value.deprecated) && !checked} onChange={event => toggle(assignment, event.target.checked, tag.cardinality === "single" && !allowRetired)} />{value.name}{value.deprecated ? " (retired)" : ""}</label>;
      })}</fieldset>)}
    {assignments.filter(assignment => !catalog.some(tag => tag.id === assignment.tagId && (!assignment.valueId || tag.values.some(value => value.id === assignment.valueId)))).map(assignment => <label key={workflowTagAssignmentKey(assignment)} className="wf-library-check"><input type="checkbox" checked onChange={() => toggle(assignment, false, false)} />Unavailable tag/value: {assignment.tagId}{assignment.valueId ? ` / ${assignment.valueId}` : ""}</label>)}
    {loading ? <p role="status">Loading tags…</p> : null}
    {!loading && !catalog.length ? <div className="wf-tag-empty"><Tags size={24} aria-hidden="true" /><p>{term ? "No matching tags" : "No tags available"}</p><span>{term ? "Try another search to find a tag." : "A tag manager can create tags from the workflow library."}</span></div> : null}
    {cursor ? <StudioButton disabled={loading} onClick={() => void loadMore()}>Load more tags</StudioButton> : null}
    {error ? <p role="alert">Couldn't read tags. {error}</p> : null}
  </fieldset>;
}

function assignmentSignature(assignments: WorkflowTagAssignment[]) { return JSON.stringify(assignments.map(workflowTagAssignmentKey).sort()); }
function errorMessage(error: unknown) { return error instanceof Error ? error.message : "The server could not complete this request."; }
