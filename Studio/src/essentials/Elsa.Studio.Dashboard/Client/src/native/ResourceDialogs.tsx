import React, { useEffect, useId, useRef, useState } from "react";
import { StudioButton } from "@elsa-workflows/studio-ui";
import { useHomeDialogFocus } from "../useHomeDialogFocus";
import type { DashboardApi } from "./api";
import type { AudienceCandidate, AudienceGrant, DashboardResource, DashboardTile, DefinitionRevision, QueryValue, Visualization } from "./types";
import { errorMessage } from "./types";

export function NativeDialog({ title, onClose, children }: { title: string; onClose(): void; children: React.ReactNode }) {
  const ref = useRef<HTMLElement>(null);
  const id = useId();
  useHomeDialogFocus(ref, onClose);
  return <div className="named-dialog-backdrop"><section ref={ref} role="dialog" aria-modal="true" aria-labelledby={id} className="named-dialog" tabIndex={-1}><div className="named-dialog-heading"><h3 id={id}>{title}</h3><StudioButton type="button" onClick={onClose} aria-label={`Close ${title}`}>Close</StudioButton></div>{children}</section></div>;
}

export function ShareDialog({ api, resource, onClose, onSaved }: { api: DashboardApi; resource: DashboardResource; onClose(): void; onSaved(resource: DashboardResource): void }) {
  const [grants, setGrants] = useState<AudienceGrant[]>([]);
  const [candidates, setCandidates] = useState<AudienceCandidate[]>([]);
  const [search, setSearch] = useState("");
  const [audienceKind, setAudienceKind] = useState("user");
  const [error, setError] = useState<string>();
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  useEffect(() => { const controller = new AbortController(); void api.audience(resource, controller.signal).then(value => { setGrants(value.items); setLoading(false); }).catch(cause => { if (!controller.signal.aborted) { setError(errorMessage(cause)); setLoading(false); } }); return () => controller.abort(); }, [api, resource]);
  return <NativeDialog title={`Share ${resource.name}`} onClose={onClose}><p>Viewers see data using their own access. Sharing a dashboard does not share its questions automatically.</p>
    {error ? <p role="alert">{error}</p> : null}{loading ? <p role="status">Loading current audience…</p> : <>
      <form onSubmit={event => { event.preventDefault(); setError(undefined); void api.candidates(search, audienceKind).then(value => setCandidates(value.items)).catch(cause => setError(errorMessage(cause))); }}><label>Audience kind<select value={audienceKind} onChange={event => { setAudienceKind(event.target.value); setCandidates([]); }}><option value="user">Users</option><option value="role">Roles</option></select></label><label>Find authorized users or roles<input value={search} onChange={event => setSearch(event.target.value)} /></label><StudioButton type="submit">Search audience</StudioButton></form>
      {candidates.length ? <ul className="named-picker-list">{candidates.map(candidate => <li key={`${candidate.kind}:${candidate.id}`}><span>{candidate.displayName || candidate.name || candidate.id} ({candidate.kind})</span><StudioButton type="button" disabled={grants.some(grant => grant.id === candidate.id && grant.kind === candidate.kind)} onClick={() => setGrants(current => [...current, { kind: candidate.kind, id: candidate.id, access: "viewer" }])}>Add audience</StudioButton></li>)}</ul> : null}
      <h4>Current audience</h4>{grants.length ? <ul className="named-picker-list">{grants.map((grant, index) => <li key={`${grant.kind}:${grant.id}`}><span>{grant.id} ({grant.kind})</span><label>Access for {grant.id}<select value={grant.access} onChange={event => setGrants(current => current.map((item, at) => at === index ? { ...item, access: event.target.value as AudienceGrant["access"] } : item))}><option value="viewer">Can view</option><option value="editor">Can edit content</option></select></label><StudioButton type="button" onClick={() => setGrants(current => current.filter((_, at) => at !== index))}>Remove {grant.id}</StudioButton></li>)}</ul> : <p>Personal. No audience grants.</p>}
      <p className="named-help">Only the owner manages sharing and deletion. Editors cannot change the audience.</p><StudioButton type="button" variant="primary" disabled={saving} onClick={() => { setSaving(true); setError(undefined); void api.share(resource, grants).then(onSaved).catch(cause => setError(errorMessage(cause))).finally(() => setSaving(false)); }}>{saving ? "Saving audience…" : "Save audience"}</StudioButton>
    </>}
  </NativeDialog>;
}

export function HistoryDialog({ api, resource, onClose, onRestored }: { api: DashboardApi; resource: DashboardResource; onClose(): void; onRestored(resource: DashboardResource): void }) {
  const [items, setItems] = useState<DefinitionRevision[]>();
  const [error, setError] = useState<string>();
  const [selected, setSelected] = useState<DashboardResource>();
  const [busy, setBusy] = useState(false);
  useEffect(() => { const controller = new AbortController(); void api.history(resource, controller.signal).then(value => setItems(value.items)).catch(cause => { if (!controller.signal.aborted) setError(errorMessage(cause)); }); return () => controller.abort(); }, [api, resource]);
  return <NativeDialog title="Definition history" onClose={onClose}><p>History stores definitions, not historical query results. Restoring creates a new validated revision and preserves the current audience.</p>{error ? <p role="alert">{error}</p> : null}{!items ? <p role="status">Loading history…</p> : <ul className="named-picker-list">{items.map(item => <li key={item.revision}><span>{item.name} — <time dateTime={item.updatedAt}>{new Date(item.updatedAt).toLocaleString()}</time></span><StudioButton type="button" disabled={busy} onClick={() => { setError(undefined); void api.revision(resource, item.revision).then(setSelected).catch(cause => setError(errorMessage(cause))); }}>Inspect revision {item.revision}</StudioButton>{resource.capabilities.canEdit ? <StudioButton type="button" disabled={busy} onClick={() => { setBusy(true); setError(undefined); void api.restore(resource, item.revision).then(onRestored).catch(cause => setError(errorMessage(cause))).finally(() => setBusy(false)); }}>Restore revision {item.revision}</StudioButton> : null}</li>)}</ul>}{selected ? <section><h4>{selected.name}: revision {selected.revision}</h4><p>{selected.description}</p>{selected.definitionState === "withheld" ? <p>Definition withheld under current access.</p> : <pre className="named-definition-inspection">{JSON.stringify(selected.definition ?? selected.tiles, null, 2)}</pre>}</section> : null}</NativeDialog>;
}

export function TileOptionsDialog({ api, tile, onClose, onApply }: { api: DashboardApi; tile: DashboardTile; onClose(): void; onApply(tile: DashboardTile): void }) {
  const [question, setQuestion] = useState<DashboardResource>();
  const [presentation, setPresentation] = useState<Visualization>(tile.presentation ?? { kind: "table", y: [] });
  const [parameters, setParameters] = useState<Record<string, QueryValue>>(tile.parameters ?? {});
  const [error, setError] = useState<string>();
  useEffect(() => { const controller = new AbortController(); void api.get("question", tile.questionId!, controller.signal).then(resource => { if (controller.signal.aborted) return; setQuestion(resource); if (!tile.presentation && resource.definition) setPresentation(resource.definition.visualization); }).catch(cause => { if (!controller.signal.aborted) setError(errorMessage(cause)); }); return () => controller.abort(); }, [api, tile]);
  const definition = question?.definition;
  const columns = definition ? [...definition.query.fields, ...definition.query.groupBy, ...definition.query.metrics.flatMap(metric => definition.query.comparison ? [`current.${metric.alias}`, `baseline.${metric.alias}`, `delta.${metric.alias}`] : [metric.alias])] : [];
  return <NativeDialog title="Tile presentation and parameters" onClose={onClose}><p>These settings affect this tile only. The saved question stays unchanged. Save the dashboard layout to persist them.</p>{error ? <p role="alert">{error}</p> : !question ? <p role="status">Loading current question…</p> : !definition ? <p role="status">Question settings are unavailable under your current access. The existing overrides remain preserved.</p> : <>
    <label>Tile visualization<select value={presentation.kind} onChange={event => setPresentation({ ...presentation, kind: event.target.value as Visualization["kind"] })}>{["table", "bar", "line", "list", "metric"].map(item => <option key={item}>{item}</option>)}</select></label>
    <label>Tile label<input value={presentation.label ?? ""} onChange={event => setPresentation({ ...presentation, label: event.target.value })} /></label>
    <label>Category / x axis<select value={presentation.x ?? ""} onChange={event => setPresentation({ ...presentation, x: event.target.value || undefined })}><option value="">None</option>{columns.map(item => <option key={item}>{item}</option>)}</select></label>
    <label>Value columns<select multiple value={presentation.y} onChange={event => setPresentation({ ...presentation, y: [...event.target.selectedOptions].map(item => item.value) })}>{columns.map(item => <option key={item}>{item}</option>)}</select></label>
    {definition.query.parameters.map(parameter => <label key={parameter.name}>{parameter.name} ({parameter.type})<input value={parameters[parameter.name]?.literal ?? ""} placeholder={parameter.default?.literal ?? "Use saved default"} onChange={event => setParameters(current => { const next = { ...current }; if (event.target.value === "") delete next[parameter.name]; else next[parameter.name] = { type: parameter.type, literal: event.target.value }; return next; })} /></label>)}
    <StudioButton variant="primary" onClick={() => onApply({ ...tile, presentation, parameters })}>Use tile settings</StudioButton>
  </>}</NativeDialog>;
}
