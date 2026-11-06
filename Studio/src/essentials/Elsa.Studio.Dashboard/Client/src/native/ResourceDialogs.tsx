import React, { useEffect, useId, useRef, useState } from "react";
import { StudioButton } from "@elsa-workflows/studio-ui";
import { useHomeDialogFocus } from "../useHomeDialogFocus";
import type { DashboardApi } from "./api";
import type { AudienceCandidate, AudienceGrant, DashboardResource, DashboardTile, DefinitionRevision, QueryValue, Visualization } from "./types";
import { errorMessage, accessLost } from "./types";
import { useRequestLifetime } from "./useRequestLifetime";
import { DashboardDependencyReview } from "./DashboardDependencyReview";
export { DashboardDependencyReview } from "./DashboardDependencyReview";

export function NativeDialog({ title, onClose, children }: { title: string; onClose(): void; children: React.ReactNode }) {
  const ref = useRef<HTMLElement>(null);
  const id = useId();
  useHomeDialogFocus(ref, onClose);
  return <div className="named-dialog-backdrop"><section ref={ref} role="dialog" aria-modal="true" aria-labelledby={id} className="named-dialog" tabIndex={-1}><div className="named-dialog-heading"><h3 id={id}>{title}</h3><StudioButton type="button" onClick={onClose} aria-label={`Close ${title}`}>Close</StudioButton></div>{children}</section></div>;
}

export function ShareDialog({ api, resource, onClose, onSaved }: { api: DashboardApi; resource: DashboardResource; onClose(): void; onSaved(resource: DashboardResource): void }) {
  const lifetime = useRequestLifetime();
  const [searched, setSearched] = useState(false);
  const [searching, setSearching] = useState(false);
  const [candidateCursor, setCandidateCursor] = useState<string | null>();
  const [appliedCandidateSearch, setAppliedCandidateSearch] = useState({ query: "", kind: "user" });
  const [grants, setGrants] = useState<AudienceGrant[]>([]);
  const [candidates, setCandidates] = useState<AudienceCandidate[]>([]);
  const [search, setSearch] = useState("");
  const [audienceKind, setAudienceKind] = useState("user");
  const [error, setError] = useState<string>();
  const [loading, setLoading] = useState(true);
  const [audienceLoaded, setAudienceLoaded] = useState(false);
  const [loadAttempt, setLoadAttempt] = useState(0);
  const [saving, setSaving] = useState(false);
  const [dependenciesReady, setDependenciesReady] = useState(resource.kind !== "dashboard");
  useEffect(() => { const request = lifetime.begin("load"); setLoading(true); setAudienceLoaded(false); setError(undefined); void api.audience(resource, request.signal).then(value => { if (request.current()) { setGrants(value.items); setAudienceLoaded(true); } }).catch(cause => { if (request.current()) setError(errorMessage(cause)); }).finally(() => { if (request.current()) setLoading(false); request.finish(); }); return () => lifetime.cancel("load"); }, [api, resource, lifetime, loadAttempt]);
  const searchAudience = async (query: string, kind: string, cursor?: string | null) => {
    const request = lifetime.begin("search"); setError(undefined); setSearching(true); setSearched(false);
    if (!cursor) { setCandidates([]); setCandidateCursor(null); }
    try {
      const page = await api.candidates(query, kind, request.signal, cursor);
      if (!request.current()) return;
      setCandidates(current => [...(cursor ? current : []), ...page.items].filter((item, index, all) => all.findIndex(candidate => candidate.id === item.id && candidate.kind === item.kind) === index));
      setCandidateCursor(page.continuationToken); setAppliedCandidateSearch({ query, kind }); setSearched(true);
    } catch (cause) { if (request.current()) { setError(errorMessage(cause)); if (accessLost(cause)) { setCandidates([]); setCandidateCursor(null); } } }
    finally { if (request.current()) setSearching(false); request.finish(); }
  };
  return <NativeDialog title={`Share ${resource.name}`} onClose={onClose}><p>Viewers see data using their own access. Sharing a dashboard does not share its questions automatically.</p>
    {resource.kind === "dashboard" ? <DashboardDependencyReview api={api} resource={resource} mode="share" onReadyChange={setDependenciesReady} /> : null}
    {error ? <p role="alert">{error}</p> : null}{loading ? <p role="status">Loading current audience…</p> : !audienceLoaded ? <><p role="status">The current audience is unknown. Load it successfully before changing sharing.</p><StudioButton onClick={() => setLoadAttempt(value => value + 1)}>Retry current audience</StudioButton></> : <>
      <form onSubmit={event => { event.preventDefault(); void searchAudience(search, audienceKind); }}><label>Audience kind<select value={audienceKind} onChange={event => { lifetime.cancel("search"); setAudienceKind(event.target.value); setCandidates([]); setCandidateCursor(null); setSearched(false); setSearching(false); }}><option value="user">Users</option><option value="role">Roles</option></select></label><label>Find authorized users or roles<input value={search} onChange={event => setSearch(event.target.value)} /></label><StudioButton type="submit" disabled={searching || saving}>{searching ? "Searching…" : "Search audience"}</StudioButton></form>
      {searched && !candidates.length && !candidateCursor ? <p role="status">No authorized audience entries matched this search.</p> : null}
      {candidates.length ? <ul className="named-picker-list">{candidates.map(candidate => <li key={`${candidate.kind}:${candidate.id}`}><span>{candidate.displayName || candidate.name || candidate.id} ({candidate.kind})</span><StudioButton type="button" disabled={grants.some(grant => grant.id === candidate.id && grant.kind === candidate.kind)} onClick={() => setGrants(current => [...current, { kind: candidate.kind, id: candidate.id, access: "viewer" }])}>Add audience</StudioButton></li>)}</ul> : null}
      {candidateCursor ? <StudioButton type="button" disabled={searching || saving} onClick={() => void searchAudience(appliedCandidateSearch.query, appliedCandidateSearch.kind, candidateCursor)}>More audience entries</StudioButton> : null}
      <h4>Current audience</h4>{grants.length ? <ul className="named-picker-list">{grants.map((grant, index) => <li key={`${grant.kind}:${grant.id}`}><span>{grant.id} ({grant.kind})</span><label>Access for {grant.id}<select value={grant.access} onChange={event => setGrants(current => current.map((item, at) => at === index ? { ...item, access: event.target.value as AudienceGrant["access"] } : item))}><option value="viewer">Can view</option><option value="editor">Can edit content</option></select></label><StudioButton type="button" onClick={() => setGrants(current => current.filter((_, at) => at !== index))}>Remove {grant.id}</StudioButton></li>)}</ul> : <p>Personal. No audience grants.</p>}
      <p className="named-help">Only the owner manages sharing and deletion. Editors cannot change the audience.</p><StudioButton type="button" variant="primary" disabled={saving || !dependenciesReady} onClick={() => { if (saving || !dependenciesReady) return; const request = lifetime.begin("save"); setSaving(true); setError(undefined); void api.share(resource, grants, request.signal).then(value => { if (request.current()) onSaved(value); }).catch(cause => { if (request.current()) { setError(errorMessage(cause)); if (accessLost(cause)) { setGrants([]); setCandidates([]); } } }).finally(() => { if (request.current()) setSaving(false); request.finish(); }); }}>{saving ? "Saving audience…" : "Save audience"}</StudioButton>
    </>}
  </NativeDialog>;
}

export function HistoryDialog({ api, resource, onClose, onRestored }: { api: DashboardApi; resource: DashboardResource; onClose(): void; onRestored(resource: DashboardResource): void }) {
  const lifetime = useRequestLifetime();
  const [items, setItems] = useState<DefinitionRevision[]>();
  const [error, setError] = useState<string>();
  const [selected, setSelected] = useState<DashboardResource>();
  const [busy, setBusy] = useState(false);
  useEffect(() => { const request = lifetime.begin("load"); void api.history(resource, request.signal).then(value => { if (request.current()) setItems(value.items); }).catch(cause => { if (request.current()) setError(errorMessage(cause)); }).finally(request.finish); return () => lifetime.cancel("load"); }, [api, resource, lifetime]);
  return <NativeDialog title="Definition history" onClose={onClose}><p>History stores definitions, not historical query results. Restoring creates a new validated revision and preserves the current audience.</p>{error ? <p role="alert">{error}</p> : null}{!items ? <p role="status">Loading history…</p> : <ul className="named-picker-list">{items.map(item => <li key={item.revision}><span>{item.name} — <time dateTime={item.updatedAt}>{new Date(item.updatedAt).toLocaleString()}</time></span><StudioButton type="button" disabled={busy} onClick={() => { const request = lifetime.begin("inspect"); setError(undefined); setSelected(undefined); void api.revision(resource, item.revision, request.signal).then(value => { if (request.current()) setSelected(value); }).catch(cause => { if (request.current()) setError(errorMessage(cause)); }).finally(request.finish); }}>Inspect revision {item.revision}</StudioButton>{resource.capabilities.canEdit ? <StudioButton type="button" disabled={busy} onClick={() => { if (busy) return; const request = lifetime.begin("restore"); setBusy(true); setError(undefined); void api.restore(resource, item.revision, request.signal).then(value => { if (request.current()) onRestored(value); }).catch(cause => { if (request.current()) { setError(errorMessage(cause)); if (accessLost(cause)) { setItems([]); setSelected(undefined); } } }).finally(() => { if (request.current()) setBusy(false); request.finish(); }); }}>Restore revision {item.revision}</StudioButton> : null}</li>)}</ul>}{selected ? <section><h4>{selected.name}: revision {selected.revision}</h4><p>{selected.description}</p>{selected.definitionState === "withheld" ? <p>Definition withheld under current access.</p> : <pre className="named-definition-inspection">{JSON.stringify(selected.definition ?? selected.tiles, null, 2)}</pre>}</section> : null}</NativeDialog>;
}

export function TileOptionsDialog({ api, tile, onClose, onApply }: { api: DashboardApi; tile: DashboardTile; onClose(): void; onApply(tile: DashboardTile): void }) {
  const lifetime = useRequestLifetime();
  const [question, setQuestion] = useState<DashboardResource>();
  const [presentation, setPresentation] = useState<Visualization>(tile.presentation ?? { kind: "table", y: [] });
  const [parameters, setParameters] = useState<Record<string, QueryValue>>(tile.parameters ?? {});
  const [error, setError] = useState<string>();
  useEffect(() => { const request = lifetime.begin("load"); void api.get("question", tile.questionId!, request.signal).then(resource => { if (!request.current()) return; setQuestion(resource); if (!tile.presentation && resource.definition) setPresentation(resource.definition.visualization); }).catch(cause => { if (request.current()) setError(errorMessage(cause)); }).finally(request.finish); return () => lifetime.cancel("load"); }, [api, tile, lifetime]);
  const definition = question?.definition;
  const columns = definition ? [...definition.query.fields, ...definition.query.groupBy, ...definition.query.metrics.flatMap(metric => definition.query.comparison ? [`current.${metric.alias}`, `baseline.${metric.alias}`, `delta.${metric.alias}`] : [metric.alias])] : [];
  return <NativeDialog title="Tile presentation and parameters" onClose={onClose}><p>These settings affect this tile only. The saved question stays unchanged. Save the dashboard layout to persist them.</p>{error ? <p role="alert">{error}</p> : !question ? <p role="status">Loading current question…</p> : !definition ? <p role="status">Question settings are unavailable under your current access. The existing overrides remain preserved.</p> : <>
    <label>Tile visualization<select value={presentation.kind} onChange={event => setPresentation({ ...presentation, kind: event.target.value as Visualization["kind"] })}>{["table", "bar", "line", "list", "metric"].map(item => <option key={item}>{item}</option>)}</select></label>
    <label>Tile label<input value={presentation.label ?? ""} onChange={event => setPresentation({ ...presentation, label: event.target.value })} /></label>
    <label>Category / x axis<select value={presentation.x ?? ""} onChange={event => setPresentation({ ...presentation, x: event.target.value || undefined })}><option value="">None</option>{columns.map(item => <option key={item}>{item}</option>)}</select></label>
    <label>Value columns<select multiple value={presentation.y} onChange={event => setPresentation({ ...presentation, y: [...event.target.selectedOptions].map(item => item.value) })}>{columns.map(item => <option key={item}>{item}</option>)}</select></label>
    {definition.query.parameters.map(parameter => <fieldset key={parameter.name}><legend>{parameter.name} ({parameter.type})</legend><label><input type="checkbox" checked={Object.hasOwn(parameters, parameter.name)} onChange={event => setParameters(current => { const next = { ...current }; if (!event.target.checked) delete next[parameter.name]; else next[parameter.name] = parameter.default ?? { type: parameter.type, literal: "" }; return next; })} />Override the saved default</label>{Object.hasOwn(parameters, parameter.name) ? <><label>Value kind<select value={parameters[parameter.name].type === "null" ? "null" : "value"} onChange={event => setParameters(current => ({ ...current, [parameter.name]: event.target.value === "null" ? { type: "null" } : { type: parameter.type, literal: "" } }))}><option value="value">Typed value</option>{parameter.nullable ? <option value="null">Null</option> : null}</select></label>{parameters[parameter.name].type !== "null" ? <label>Override value for {parameter.name}<input value={parameters[parameter.name].literal ?? ""} onChange={event => setParameters(current => ({ ...current, [parameter.name]: { type: parameter.type, literal: event.target.value } }))} /></label> : null}</> : <p>Uses the saved question default.</p>}</fieldset>)}
    <StudioButton variant="primary" onClick={() => onApply({ ...tile, presentation, parameters })}>Use tile settings</StudioButton>
  </>}</NativeDialog>;
}

export function QuestionPickerDialog({ api, onClose, onPick, onCreate }: { api: DashboardApi; onClose(): void; onPick(question: DashboardResource): void; onCreate(): void }) {
  const lifetime = useRequestLifetime();
  const [items, setItems] = useState<DashboardResource[]>([]);
  const [cursors, setCursors] = useState<{ mine?: string | null; shared?: string | null }>({});
  const [search, setSearch] = useState("");
  const [appliedSearch, setAppliedSearch] = useState("");
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string>();
  const load = React.useCallback(async (query: string, next?: { mine?: string | null; shared?: string | null }) => {
    const request = lifetime.begin("questions"); setLoading(true); setError(undefined);
    if (!next) { setItems([]); setCursors({}); }
    try {
      const pages = await Promise.all((["mine", "shared"] as const).map(async ownership => !next || next[ownership]
        ? { ownership, page: await api.list("question", ownership, query, next?.[ownership], request.signal) }
        : { ownership, page: undefined }));
      if (!request.current()) return;
      const additions = pages.flatMap(item => item.page?.items ?? []);
      setItems(current => [...(next ? current : []), ...additions].filter((item, index, all) => all.findIndex(candidate => candidate.id === item.id) === index));
      setCursors(Object.fromEntries(pages.map(item => [item.ownership, item.page?.continuationToken ?? null])));
      setAppliedSearch(query);
    } catch (cause) {
      if (request.current()) { setError(errorMessage(cause)); if (accessLost(cause)) { setItems([]); setCursors({}); } }
    } finally { if (request.current()) setLoading(false); request.finish(); }
  }, [api, lifetime]);
  useEffect(() => { void load(""); return () => lifetime.cancel("questions"); }, [load, lifetime]);
  return <NativeDialog title="Add saved question" onClose={onClose}><p>Adding a reference does not share the question or copy another person's results. You can add the same question more than once.</p>
    <form onSubmit={event => { event.preventDefault(); void load(search); }}><label>Search accessible questions<input value={search} onChange={event => setSearch(event.target.value)} /></label><StudioButton type="submit" disabled={loading}>Search questions</StudioButton></form>
    {error ? <p role="alert">{error}</p> : null}{loading ? <p role="status">Loading accessible questions…</p> : !items.length ? <p role="status">{appliedSearch ? "No accessible questions match this search." : "No accessible saved questions. Create a question first or check your access."}</p> : null}
    {items.length ? <ul className="named-picker-list">{items.map(question => <li key={question.id}><span>{question.name}</span><StudioButton onClick={() => onPick(question)}>Add {question.name}</StudioButton></li>)}</ul> : null}
    {cursors.mine || cursors.shared ? <StudioButton disabled={loading} onClick={() => void load(appliedSearch, cursors)}>Load more questions</StudioButton> : null}
    {items.length ? <p>{items.length} accessible questions loaded.</p> : null}<StudioButton onClick={onCreate}>Create a question</StudioButton>
  </NativeDialog>;
}
