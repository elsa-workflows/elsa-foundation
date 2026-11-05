import React, { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useAuthSession, usePermissions, type ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { SkeletonRows, StudioButton, StudioCollectionFooter, StudioCollectionToolbar, StudioPage, StudioPageHeader, StudioResultState, StudioSearchInput, StudioTableContainer } from "@elsa-workflows/studio-ui";
import { createHomeLayoutScope } from "../homeLayout";
import { DashboardApi } from "./api";
import { QueryControls } from "./QueryControls";
import { ResultView } from "./ResultView";
import { HistoryDialog, NativeDialog, ShareDialog, TileOptionsDialog } from "./ResourceDialogs";
import { accessLost, errorMessage, initialDefinition, resourcePath, type DashboardResource, type DashboardTile, type DatasetDeclaration, type QueryResult, type QueryRow, type QuestionDefinition } from "./types";
import "./native.css";

type RouteProps = { api: ElsaStudioModuleApi; kind: "question" | "dashboard"; view: "list" | "create" | "detail"; navigate(path: string): void };
const uuid = () => crypto.randomUUID();

/** The host can retain mounted tabs. Key all protected native state by actual authenticated/backend scope. */
export function NamedDashboardsRoute(props: RouteProps) {
  const session = useAuthSession();
  const [pathname, setPathname] = useState(() => window.location.pathname);
  useEffect(() => { const changed = () => setPathname(window.location.pathname); window.addEventListener("popstate", changed); return () => window.removeEventListener("popstate", changed); }, []);
  const scope = JSON.stringify(createHomeLayoutScope(props.api, session));
  const id = props.view === "detail" ? decodeURIComponent(pathname.split("/").pop() ?? "") : undefined;
  const native = useMemo(() => new DashboardApi(props.api.backend), [props.api.backend]);
  const key = `${scope}:${props.kind}:${props.view}:${id ?? ""}`;
  return props.view === "list" ? <ResourceCollection key={key} {...props} native={native} /> : <ResourceRoute key={key} {...props} native={native} id={id} />;
}

function ResourceCollection({ api, native, kind, navigate }: RouteProps & { native: DashboardApi }) {
  const permissions = usePermissions();
  const [ownership, setOwnership] = useState("mine");
  const [search, setSearch] = useState("");
  const [appliedSearch, setAppliedSearch] = useState("");
  const [items, setItems] = useState<DashboardResource[]>([]);
  const [cursor, setCursor] = useState<string | null>();
  const [error, setError] = useState<string>();
  const [loading, setLoading] = useState(true);
  const request = useRef<AbortController | undefined>(undefined);
  const canCreate = permissions.has("*") || permissions.has(`studio/${kind}s:create`);
  const title = kind === "question" ? "Questions" : "Dashboards";
  const load = useCallback(async (query: string, next?: string | null) => {
    request.current?.abort(); const controller = new AbortController(); request.current = controller;
    setLoading(true); setError(undefined);
    try { const page = await native.list(kind, ownership, query, next, controller.signal); if (controller.signal.aborted) return; setItems(current => next ? [...current, ...page.items] : page.items); setCursor(page.continuationToken); setAppliedSearch(query); }
    catch (cause) { if (!controller.signal.aborted) { setItems([]); setCursor(null); setError(errorMessage(cause)); } }
    finally { if (!controller.signal.aborted) setLoading(false); }
  }, [native, kind, ownership]);
  useEffect(() => { setItems([]); void load(""); return () => request.current?.abort(); }, [load]);
  return <StudioPage className="named-workspace"><StudioPageHeader headingLevel={2} title={title} description={kind === "question" ? "Save a question once. Reuse it with your current data access." : "Arrange saved questions into personal and shared views."} actions={<>{canCreate ? <StudioButton variant="primary" onClick={() => navigate(`${resourcePath(kind)}/new`)}>Create {kind}</StudioButton> : null}<StudioButton disabled={loading} onClick={() => void load(appliedSearch)}>Refresh</StudioButton></>} />
    <StudioCollectionToolbar><form className="named-search" onSubmit={event => { event.preventDefault(); void load(search); }}><StudioSearchInput aria-label={`Search ${title.toLowerCase()}`} value={search} onChange={event => setSearch(event.target.value)} /><StudioButton type="submit">Search</StudioButton></form><div className="studio-filter-group" role="group" aria-label="Ownership"><StudioButton aria-pressed={ownership === "mine"} onClick={() => setOwnership("mine")}>Mine</StudioButton><StudioButton aria-pressed={ownership === "shared"} onClick={() => setOwnership("shared")}>Shared with me</StudioButton></div></StudioCollectionToolbar>
    {loading && !items.length ? <SkeletonRows label={`Loading ${title.toLowerCase()}`} /> : error ? <StudioResultState tone="danger" title={`${title} could not be loaded`} description={error} actions={<StudioButton onClick={() => void load(appliedSearch)}>Retry</StudioButton>} /> : !items.length ? <StudioResultState title={appliedSearch ? "No matching definitions" : ownership === "shared" ? "Nothing has been shared with you" : `No ${title.toLowerCase()} yet`} description={appliedSearch ? "Change your search to see other definitions." : kind === "question" ? "Create a supported question using ordinary controls or Weaver." : "Create a dashboard and add accessible saved questions."} actions={appliedSearch ? <StudioButton onClick={() => { setSearch(""); void load(""); }}>Clear search</StudioButton> : canCreate ? <StudioButton onClick={() => navigate(`${resourcePath(kind)}/new`)}>Create {kind}</StudioButton> : undefined} /> : <StudioTableContainer role="region" aria-label={title} tabIndex={0}><table className="studio-table"><thead><tr><th scope="col">Name</th><th scope="col">Description</th><th scope="col">Access</th><th scope="col">Updated</th></tr></thead><tbody>{items.map(item => <tr key={item.id}><td><a href={resourcePath(kind, item.id)} onClick={event => { if (!event.metaKey && !event.ctrlKey && !event.shiftKey && event.button === 0) { event.preventDefault(); navigate(resourcePath(kind, item.id)); } }}>{item.name}</a></td><td>{item.description}</td><td>{item.capabilities.canShare ? "Owner" : item.capabilities.canEdit ? "Can edit" : "Can view"}</td><td><time dateTime={item.updatedAt}>{new Date(item.updatedAt).toLocaleString()}</time></td></tr>)}</tbody></table></StudioTableContainer>}
    {!error && items.length ? <StudioCollectionFooter>{items.length} loaded {title.toLowerCase()}{cursor ? <StudioButton disabled={loading} onClick={() => void load(appliedSearch, cursor)}>Load more</StudioButton> : null}</StudioCollectionFooter> : null}
    <p className="named-help">Existing Home widgets and layout remain available from Home. Saved questions refresh without AI.</p>
  </StudioPage>;
}

function ResourceRoute({ api, native, kind, id, navigate }: RouteProps & { native: DashboardApi; id?: string }) {
  const [resource, setResource] = useState<DashboardResource>();
  const [datasets, setDatasets] = useState<DatasetDeclaration[]>([]);
  const [error, setError] = useState<string>();
  const [sourceError, setSourceError] = useState<string>();
  const [attempt, setAttempt] = useState(0);
  useEffect(() => {
    const controller = new AbortController(); setResource(undefined); setError(undefined);
    if (kind === "question") void native.datasets(controller.signal).then(page => { if (!controller.signal.aborted) setDatasets(page.items); }).catch(cause => { if (!controller.signal.aborted) setSourceError(errorMessage(cause)); });
    if (id) void native.get(kind, id, controller.signal).then(value => { if (!controller.signal.aborted) setResource(value); }).catch(cause => { if (!controller.signal.aborted) setError(errorMessage(cause)); });
    else setResource({ id: "", kind, name: "", description: "", revision: "", ownerId: "", capabilities: { canRead: true, canEdit: true, canShare: false, canDelete: false, canCopy: false }, definition: kind === "question" ? initialDefinition() : undefined, definitionState: "available", tiles: [], createdAt: "", updatedAt: "" });
    return () => controller.abort();
  }, [native, kind, id, attempt]);
  if (error) return <StudioPage className="named-workspace"><StudioPageHeader headingLevel={2} title={kind === "question" ? "Question unavailable" : "Dashboard unavailable"} /><StudioResultState tone="danger" title="Definition could not be opened" description={error} actions={<><StudioButton onClick={() => setAttempt(value => value + 1)}>Retry</StudioButton><StudioButton onClick={() => navigate(resourcePath(kind))}>Back to collection</StudioButton></>} /></StudioPage>;
  if (!resource) return <StudioPage className="named-workspace"><SkeletonRows label="Loading definition" /></StudioPage>;
  return <ResourceEditor key={resource.id || "new"} api={api} native={native} initial={resource} datasets={datasets} sourceError={sourceError} onReload={() => setAttempt(value => value + 1)} navigate={navigate} />;
}

function ResourceEditor({ api, native, initial, datasets, sourceError, onReload, navigate }: { api: ElsaStudioModuleApi; native: DashboardApi; initial: DashboardResource; datasets: DatasetDeclaration[]; sourceError?: string; onReload(): void; navigate(path: string): void }) {
  const [saved, setSaved] = useState(initial);
  const [draft, setDraft] = useState(initial);
  const [error, setError] = useState<string>();
  const [busy, setBusy] = useState(false);
  const [dialog, setDialog] = useState<"share" | "history" | "questions" | null>(null);
  const [result, setResult] = useState<QueryResult>();
  const [resultDefinition, setResultDefinition] = useState<QuestionDefinition>();
  const [resultFingerprint, setResultFingerprint] = useState<string>();
  const [querying, setQuerying] = useState(false);
  const [stale, setStale] = useState(false);
  const [drillResult, setDrillResult] = useState<QueryResult>();
  const [drillError, setDrillError] = useState<string>();
  const [questionChoices, setQuestionChoices] = useState<DashboardResource[]>([]);
  const [choiceError, setChoiceError] = useState<string>();
  const [refresh, setRefresh] = useState(0);
  const [announcement, setAnnouncement] = useState("");
  const [editingTile, setEditingTile] = useState<DashboardTile>();
  const previewRequest = useRef<AbortController | undefined>(undefined);
  const operation = useRef<AbortController | undefined>(undefined);
  const createKey = useRef(uuid());
  const pageRef = useRef<HTMLDivElement>(null);
  const kind = draft.kind;
  const canEdit = saved.capabilities.canEdit;
  const dirty = JSON.stringify(draft) !== JSON.stringify(saved);
  const definitionFingerprint = JSON.stringify(draft.definition);
  const currentDefinition = useRef(definitionFingerprint);
  const outdated = resultFingerprint !== undefined && resultFingerprint !== definitionFingerprint;
  useEffect(() => () => { previewRequest.current?.abort(); operation.current?.abort(); }, []);
  useEffect(() => { if (currentDefinition.current !== definitionFingerprint) { currentDefinition.current = definitionFingerprint; previewRequest.current?.abort(); setQuerying(false); } }, [definitionFingerprint]);
  const notify = (title: string) => api.notifications?.notify({ level: "success", title });
  const accepted = (resource: DashboardResource) => { setSaved(resource); setDraft(resource); setDialog(null); };
  const runMutation = async (work: (signal: AbortSignal) => Promise<DashboardResource>, title: string) => {
    if (operation.current) return;
    const controller = new AbortController(); operation.current = controller; setBusy(true); setError(undefined);
    try { const resource = await work(controller.signal); if (controller.signal.aborted) return; accepted(resource); notify(title); if (!initial.id) navigate(resourcePath(kind, resource.id)); }
    catch (cause) { if (!controller.signal.aborted) { setError(errorMessage(cause)); if (accessLost(cause)) { setResult(undefined); setDrillResult(undefined); } } }
    finally { if (!controller.signal.aborted) { setBusy(false); operation.current = undefined; } }
  };
  const preview = async () => {
    if (!draft.definition) return;
    previewRequest.current?.abort(); const controller = new AbortController(); previewRequest.current = controller;
    const submitted = draft.definition; const fingerprint = JSON.stringify(submitted); setQuerying(true); setError(undefined);
    try { const value = !dirty && saved.id ? await native.execute(saved.id, saved.revision, controller.signal) : await native.preview(submitted, controller.signal); if (controller.signal.aborted) return; setResult(value); setResultDefinition(submitted); setResultFingerprint(fingerprint); setStale(false); }
    catch (cause) { if (!controller.signal.aborted) { setError(errorMessage(cause)); if (accessLost(cause)) { setResult(undefined); setDrillResult(undefined); } else setStale(Boolean(result)); } }
    finally { if (!controller.signal.aborted) setQuerying(false); }
  };
  const drill = async (row: QueryRow) => {
    if (!resultDefinition || !row.drill) return;
    setDrillError(undefined); setDrillResult(undefined);
    try { setDrillResult(saved.id && !dirty ? await native.savedDrill(saved, row.drill) : await native.drill({ question: resultDefinition, descriptor: row.drill, pageSize: 25 })); }
    catch (cause) { setDrillError(errorMessage(cause)); if (accessLost(cause)) setResult(undefined); }
  };
  const moveTile = (tileId: string, delta: number) => {
    const tiles = [...(draft.tiles ?? [])]; const from = tiles.findIndex(tile => tile.id === tileId); const to = from + delta;
    if (from < 0 || to < 0 || to >= tiles.length) return;
    [tiles[from], tiles[to]] = [tiles[to], tiles[from]]; setDraft({ ...draft, tiles }); setAnnouncement(`Tile moved to position ${to + 1} of ${tiles.length}. Save layout to keep this change.`);
    requestAnimationFrame(() => pageRef.current?.querySelector<HTMLButtonElement>(`[data-tile-id="${CSS.escape(tileId)}"] button[data-move="${delta}"]`)?.focus());
  };
  const openPicker = () => { setDialog("questions"); setQuestionChoices([]); setChoiceError(undefined); void Promise.all([native.list("question", "mine"), native.list("question", "shared")]).then(pages => setQuestionChoices([...pages[0].items, ...pages[1].items].filter((item, index, all) => all.findIndex(candidate => candidate.id === item.id) === index))).catch(cause => setChoiceError(errorMessage(cause))); };
  return <StudioPage className="named-workspace"><div className="named-editor-content" ref={pageRef}><StudioPageHeader headingLevel={2} title={draft.id ? draft.name : `Create ${kind}`} description={draft.id ? `Revision ${saved.revision}${dirty ? " · Unsaved changes" : ""}` : "Personal until you explicitly share it."} actions={<>
    {canEdit ? <StudioButton variant="primary" disabled={busy || !draft.name.trim() || kind === "question" && !draft.definition && draft.definitionState !== "withheld"} onClick={() => void runMutation(signal => native.save(draft, createKey.current, signal), kind === "question" ? "Question saved" : "Dashboard saved")}>{busy ? "Saving…" : kind === "question" ? "Save question" : "Save layout"}</StudioButton> : null}
    {saved.id && saved.capabilities.canCopy ? <StudioButton disabled={busy} onClick={() => { void api.dialogs.prompt({ title: "Save a private copy", message: kind === "dashboard" ? "The copy keeps its question references. Source access is still required." : "Copy the definition without its audience, history or results.", defaultValue: `${draft.name} copy` }).then(name => { if (name?.trim()) void runMutation(signal => native.copy(saved, name, uuid(), signal).then(resource => { navigate(resourcePath(kind, resource.id)); return resource; }), "Private copy saved"); }); }}>Save a copy</StudioButton> : null}
    {saved.id && saved.capabilities.canShare ? <StudioButton disabled={busy || dirty} onClick={() => setDialog("share")}>Share</StudioButton> : null}
    {saved.id ? <StudioButton disabled={busy} onClick={() => setDialog("history")}>History</StudioButton> : null}
    <StudioButton onClick={() => { void (dirty ? api.dialogs.confirm({ message: "Discard unsaved changes and return to the collection?" }) : Promise.resolve(true)).then(yes => { if (yes) navigate(resourcePath(kind)); }); }}>Back to {kind === "question" ? "questions" : "dashboards"}</StudioButton>
  </>} />
    {!canEdit ? <p className="named-access-note">Read-only definition. Data uses your current access.</p> : null}
    {error ? <div className="named-inline-error" role="alert"><p>{error}</p>{saved.id ? <StudioButton onClick={() => { void api.dialogs.confirm({ message: "Reload the saved definition? Unsaved changes will be discarded." }).then(yes => { if (yes) onReload(); }); }}>Reload saved definition</StudioButton> : null}<p>Your editable draft remains here. A successful local edit does not mean it was saved.</p></div> : null}
    <div className="named-definition-meta"><label>Name<input maxLength={200} value={draft.name} disabled={!canEdit || busy} onChange={event => setDraft({ ...draft, name: event.target.value })} /></label><label>Description<textarea maxLength={2000} value={draft.description ?? ""} disabled={!canEdit || busy} onChange={event => setDraft({ ...draft, description: event.target.value })} /></label></div>
    {kind === "question" ? <>
      {sourceError ? <p role="alert">Dataset discovery unavailable: {sourceError}. Existing definition is preserved.</p> : null}
      {draft.definitionState === "withheld" ? <p role="status">Parts of this definition are withheld under your current access. Metadata-only save preserves its query; copying or replacing withheld content requires complete disclosure rights.</p> : draft.definition ? <div className="named-authoring"><QueryControls definition={draft.definition} datasets={datasets} disabled={!canEdit || busy} onChange={definition => setDraft({ ...draft, definition })} /><section className="named-preview" aria-label="Preview"><h3>Preview</h3><p>Results use your current permissions. Ordinary editing, saving and refresh do not require an AI provider.</p><div className="named-actions"><StudioButton disabled={querying || !draft.definition.query.dataset.id} onClick={() => void preview()}>{querying ? "Querying…" : saved.id && !dirty ? "Refresh results" : "Preview question"}</StudioButton>{querying ? <StudioButton onClick={() => { previewRequest.current?.abort(); setQuerying(false); setError("Query cancelled. No saved definition was changed."); }}>Cancel query</StudioButton> : null}</div>{result ? <ResultView result={result} visualization={resultDefinition?.visualization ?? draft.definition.visualization} stale={stale} outdated={outdated} onDrill={row => void drill(row)} /> : <p role="status">Run a preview to see actual source results and coverage.</p>}</section></div> : null}
      {saved.id ? <StudioButton onClick={() => navigate("/dashboards")}>Arrange on a dashboard</StudioButton> : null}
    </> : <>
      <div className="named-actions">{canEdit ? <StudioButton onClick={openPicker}>Add saved question</StudioButton> : null}<StudioButton disabled={!saved.id || dirty} onClick={() => setRefresh(value => value + 1)}>Refresh all</StudioButton>{dirty ? <p className="named-help">Save layout before refreshing its exact saved revision.</p> : null}</div>
      {!draft.tiles?.length ? <StudioResultState title="No questions on this dashboard" description="Add an accessible saved question. Each tile can use its own size and presentation." /> : <div className="named-tile-grid">{draft.tiles.map((tile, index) => <article className="named-tile" data-size={tile.size} data-tile-id={tile.id} key={tile.id}><header><h3>Question {index + 1}</h3>{canEdit ? <div className="named-actions"><StudioButton data-move="-1" disabled={index === 0} aria-label={`Move question ${index + 1} earlier`} onClick={() => moveTile(tile.id, -1)}>Move earlier</StudioButton><StudioButton data-move="1" disabled={index === draft.tiles!.length - 1} aria-label={`Move question ${index + 1} later`} onClick={() => moveTile(tile.id, 1)}>Move later</StudioButton><label>Size for question {index + 1}<select value={tile.size} onChange={event => setDraft({ ...draft, tiles: draft.tiles?.map(item => item.id === tile.id ? { ...item, size: event.target.value as DashboardTile["size"] } : item) })}>{["small", "medium", "wide", "full"].map(item => <option key={item}>{item}</option>)}</select></label>{tile.referenceState !== "unavailable" && tile.questionId ? <StudioButton onClick={() => setEditingTile(tile)}>Settings for question {index + 1}</StudioButton> : null}<StudioButton onClick={() => setDraft({ ...draft, tiles: draft.tiles?.filter(item => item.id !== tile.id) })}>Remove tile {index + 1}</StudioButton></div> : null}</header>{tile.referenceState === "unavailable" || !tile.questionId ? <p role="status">This question is unavailable under your current access. Its reference is preserved during layout saves.</p> : <QuestionTile api={native} dashboard={saved} tile={tile} refresh={refresh} draftLayout={dirty} navigate={navigate} />}</article>)}</div>}
      <p aria-live="polite" className="named-announcement">{announcement}</p>
    </>}
    {saved.id && saved.capabilities.canDelete ? <div className="named-danger-actions"><StudioButton disabled={busy} onClick={() => { void api.dialogs.confirm({ title: `Delete ${kind}`, message: kind === "question" ? "Delete this question? Dashboard references will become unavailable. Underlying records will remain." : "Delete this dashboard? Its saved questions will remain.", tone: "danger" }).then(yes => { if (yes) { setBusy(true); void native.remove(saved).then(() => { notify(`${kind === "question" ? "Question" : "Dashboard"} deleted`); navigate(resourcePath(kind)); }).catch(cause => setError(errorMessage(cause))).finally(() => setBusy(false)); } }); }}>Delete {kind}</StudioButton></div> : null}
    {editingTile ? <TileOptionsDialog api={native} tile={editingTile} onClose={() => setEditingTile(undefined)} onApply={tile => { setDraft({ ...draft, tiles: draft.tiles?.map(item => item.id === tile.id ? tile : item) }); setEditingTile(undefined); }} /> : null}
    {dialog === "share" ? <ShareDialog api={native} resource={saved} onClose={() => setDialog(null)} onSaved={resource => { accepted(resource); notify("Audience saved"); }} /> : null}
    {dialog === "history" ? <HistoryDialog api={native} resource={saved} onClose={() => setDialog(null)} onRestored={resource => { accepted(resource); notify("Definition restored as a new revision"); }} /> : null}
    {dialog === "questions" ? <NativeDialog title="Add saved question" onClose={() => setDialog(null)}><p>Adding a reference does not share the question or copy another person's results. You can add the same question more than once.</p>{choiceError ? <p role="alert">{choiceError}</p> : !questionChoices.length ? <p role="status">No accessible saved questions loaded. Create a question first or check your access.</p> : <ul className="named-picker-list">{questionChoices.map(question => <li key={question.id}><span>{question.name}</span><StudioButton onClick={() => { setDraft({ ...draft, tiles: [...(draft.tiles ?? []), { id: uuid(), questionId: question.id, size: "medium", referenceState: "available" }] }); setDialog(null); }}>Add {question.name}</StudioButton></li>)}</ul>}<StudioButton onClick={() => navigate("/questions/new")}>Create a question</StudioButton></NativeDialog> : null}
    {drillResult || drillError ? <NativeDialog title="Matching source records" onClose={() => { setDrillResult(undefined); setDrillError(undefined); }}>{drillError ? <p role="alert">{drillError}</p> : drillResult ? <ResultView result={drillResult} visualization={{ kind: "table", y: [] }} /> : null}</NativeDialog> : null}
  </div></StudioPage>;
}

function QuestionTile({ api, dashboard, tile, refresh, draftLayout, navigate }: { api: DashboardApi; dashboard: DashboardResource; tile: DashboardTile; refresh: number; draftLayout: boolean; navigate(path: string): void }) {
  const [question, setQuestion] = useState<DashboardResource>();
  const [result, setResult] = useState<QueryResult>();
  const [error, setError] = useState<string>();
  const [loading, setLoading] = useState(false);
  const [drilled, setDrilled] = useState<QueryResult>();
  const [drillError, setDrillError] = useState<string>();
  const [drilling, setDrilling] = useState(false);
  const drillRequest = useRef<AbortController | undefined>(undefined);
  useEffect(() => () => drillRequest.current?.abort(), []);
  const drill = async (row: QueryRow) => {
    if (!question || !row.drill) return;
    drillRequest.current?.abort(); const controller = new AbortController(); drillRequest.current = controller;
    setDrilling(true); setDrillError(undefined); setDrilled(undefined);
    try { const response = await api.tileDrill(dashboard, tile.id, question.revision, row.drill, controller.signal); if (!controller.signal.aborted) setDrilled(response); }
    catch (cause) { if (!controller.signal.aborted) { setDrillError(errorMessage(cause)); if (accessLost(cause)) { setResult(undefined); setQuestion(undefined); } } }
    finally { if (!controller.signal.aborted) setDrilling(false); }
  };
  useEffect(() => {
    const controller = new AbortController(); setLoading(true); setError(undefined);
    void (async () => { try { const resource = await api.get("question", tile.questionId!, controller.signal); if (controller.signal.aborted) return; setQuestion(resource); if (!dashboard.id || draftLayout) return; const response = await api.tile(dashboard, tile.id, resource.revision, controller.signal); if (!controller.signal.aborted) setResult(response); } catch (cause) { if (!controller.signal.aborted) { setError(errorMessage(cause)); if (accessLost(cause)) { setResult(undefined); setQuestion(undefined); } } } finally { if (!controller.signal.aborted) setLoading(false); } })();
    return () => controller.abort();
  }, [api, dashboard.id, dashboard.revision, tile.id, tile.questionId, refresh, draftLayout]);
  return <div className="named-tile-body">{question ? <a href={resourcePath("question", question.id)} onClick={event => { event.preventDefault(); navigate(resourcePath("question", question.id)); }}>{question.name}</a> : null}{loading ? <p role="status">Refreshing question…</p> : null}{error ? <p role="alert">{error}</p> : null}{draftLayout ? <p role="status">Save this layout to execute its exact tile bindings.</p> : result ? <ResultView result={result} visualization={tile.presentation ?? question?.definition?.visualization ?? { kind: "table", y: [] }} stale={Boolean(error)} onDrill={row => void drill(row)} /> : !loading && !error ? <p role="status">Results have not been loaded.</p> : null}{drilling || drilled || drillError ? <NativeDialog title="Matching source records" onClose={() => { drillRequest.current?.abort(); setDrilling(false); setDrilled(undefined); setDrillError(undefined); }}>{drilling ? <p role="status">Loading authorized source records…</p> : drillError ? <p role="alert">{drillError}</p> : drilled ? <ResultView result={drilled} visualization={{ kind: "table", y: [] }} /> : null}</NativeDialog> : null}</div>;
}
