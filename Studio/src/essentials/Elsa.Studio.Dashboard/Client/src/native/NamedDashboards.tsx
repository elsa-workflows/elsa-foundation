import React, { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useAuthSession, usePermissions, type ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { SkeletonRows, StudioButton, StudioCollectionFooter, StudioCollectionToolbar, StudioPage, StudioPageHeader, StudioResultState, StudioSearchInput, StudioTableContainer } from "@elsa-workflows/studio-ui";
import { createHomeLayoutScope } from "../homeLayout";
import { DashboardApi } from "./api";
import { QueryControls } from "./QueryControls";
import { ResultView } from "./ResultView";
import { HistoryDialog, NativeDialog, ShareDialog, TileOptionsDialog, QuestionPickerDialog } from "./ResourceDialogs";
import { accessLost, errorMessage, initialDefinition, resourcePath, type DashboardResource, type DashboardTile, type DatasetDeclaration, type QueryResult, type QueryRow, type QuestionDefinition } from "./types";
import { useRequestLifetime } from "./useRequestLifetime";
import { sameAuthoringTarget, type DashboardAuthoringController, type DashboardAuthoringSnapshot } from "./authoring";
import { DashboardAskWeaver } from "./weaverAuthoring";
import "./native.css";

type RouteProps = { api: ElsaStudioModuleApi; kind: "question" | "dashboard"; view: "list" | "create" | "detail"; authoring?: DashboardAuthoringController; navigate(path: string): void };
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

function ResourceCollection({ native, kind, navigate }: RouteProps & { native: DashboardApi }) {
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

function ResourceRoute({ api, native, kind, id, navigate, authoring }: RouteProps & { native: DashboardApi; id?: string }) {
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
  return <ResourceEditor key={resource.id || "new"} api={api} native={native} initial={resource} authoring={authoring} datasets={datasets} sourceError={sourceError} onReload={() => setAttempt(value => value + 1)} navigate={navigate} />;
}

function ResourceEditor({ api, native, initial, datasets, sourceError, onReload, navigate, authoring }: { api: ElsaStudioModuleApi; native: DashboardApi; initial: DashboardResource; authoring?: DashboardAuthoringController; datasets: DatasetDeclaration[]; sourceError?: string; onReload(): void; navigate(path: string): void }) {
  const permissions = usePermissions();
  const canCreateQuestion = permissions.has("*") || permissions.has("studio/questions:create");
  const [saved, setSaved] = useState(initial);
  const [draft, replaceDraft] = useState(initial);
  const draftRef = useRef(draft);
  const savedRef = useRef(saved);
  draftRef.current = draft; savedRef.current = saved;
  const workingRevision = useRef(0);
  const generationId = useRef(uuid()).current;
  const lifetime = useRequestLifetime();
  const [proposalUndo, setProposalUndo] = useState<{ definition: QuestionDefinition; appliedRevision: string }>();
  const setDraft = (value: DashboardResource) => { workingRevision.current++; draftRef.current = value; replaceDraft(value); };
  const [error, setError] = useState<string>();
  const [busy, setBusy] = useState(false);
  const [accessUnavailable, setAccessUnavailable] = useState(false);
  const accessUnavailableRef = useRef(false);
  const [dialog, setDialog] = useState<"share" | "history" | "questions" | null>(null);
  const [result, setResult] = useState<QueryResult>();
  const [resultDefinition, setResultDefinition] = useState<QuestionDefinition>();
  const [resultResource, setResultResource] = useState<DashboardResource>();
  const [resultCanDrill, setResultCanDrill] = useState(false);
  const [resultFingerprint, setResultFingerprint] = useState<string>();
  const [querying, setQuerying] = useState(false);
  const [stale, setStale] = useState(false);
  const [resultWithheld, setResultWithheld] = useState(false);
  const [drillResult, setDrillResult] = useState<QueryResult>();
  const [drillSource, setDrillSource] = useState<QueryRow>();
  const [drilling, setDrilling] = useState(false);
  const [drillError, setDrillError] = useState<string>();
  const [refresh, setRefresh] = useState(0);
  const [announcement, setAnnouncement] = useState("");
  const [editingTile, setEditingTile] = useState<DashboardTile>();
  const previewRequest = useRef<AbortController | undefined>(undefined);
  const operation = useRef<AbortController | undefined>(undefined);
  const createKey = useRef(uuid());
  const draftCreateAttempts = useRef(new Map<string, string>());
  const pageRef = useRef<HTMLDivElement>(null);
  const kind = draft.kind;
  const canEdit = saved.capabilities.canEdit;
  const dirty = JSON.stringify(draft) !== JSON.stringify(saved);
  const definitionFingerprint = JSON.stringify(draft.definition);
  const definitionDirty = definitionFingerprint !== JSON.stringify(saved.definition);
  const currentDefinition = useRef(definitionFingerprint);
  const outdated = resultFingerprint !== undefined && resultFingerprint !== definitionFingerprint;
  useEffect(() => () => { previewRequest.current?.abort(); operation.current?.abort(); }, []);
  useEffect(() => { if (currentDefinition.current !== definitionFingerprint) { currentDefinition.current = definitionFingerprint; previewRequest.current?.abort(); setQuerying(false); } }, [definitionFingerprint]);
  useEffect(() => {
    if (accessUnavailable || !authoring || kind !== "question" || !draft.definition || draft.definitionState === "withheld") return;
    const snapshot = (): DashboardAuthoringSnapshot => {
      const current = draftRef.current;
      const persisted = savedRef.current;
      return {
        generationId, client: { clientId: authoring.clientId, generationId },
        target: { moduleId: "Elsa.Studio.Dashboard", resourceType: persisted.id ? "question" : "question-draft", resourceId: persisted.id || generationId, revision: persisted.id ? persisted.revision : String(workingRevision.current), displayName: current.name },
        workingRevision: String(workingRevision.current), definition: structuredClone(current.definition!),
        name: current.name, dirty: JSON.stringify(current) !== JSON.stringify(persisted), canEdit: persisted.capabilities.canEdit
      };
    };
    return authoring.publish(snapshot(), async proposal => {
      const origin = snapshot();
      const matches = () => {
        const current = snapshot();
        return !accessUnavailableRef.current && current.canEdit && !operation.current && current.workingRevision === proposal.workingRevision && sameAuthoringTarget(current.target, proposal.target)
          && proposal.client.clientId === current.client.clientId && proposal.client.generationId === current.client.generationId;
      };
      if (!matches()) return { outcome: "refused", summary: "The question changed or is read-only. Repreview the proposal; nothing was applied or saved." };
      if (proposal.definitionFingerprint && proposal.definitionFingerprint !== JSON.stringify(proposal.definition))
        return { outcome: "refused", summary: "The proposed definition no longer matches its accepted values. Repreview it before applying." };
      const request = lifetime.begin("proposal-use");
      try {
        if (proposal.target.resourceType === "question") { const current = await native.get("question", proposal.target.resourceId, request.signal); if (current.revision !== proposal.target.revision || !current.capabilities.canEdit || current.definitionState === "withheld") return { outcome: "refused", summary: "Current question access or revision no longer allows this proposal. Reload before applying." }; }
        const validation = await native.validate(proposal.definition, request.signal);
        if (!request.current() || !matches()) return { outcome: "refused", summary: "The originating question changed or closed during validation. Nothing was applied or saved." };
        if (!validation.isValid) return { outcome: "refused", summary: validation.problems.map(problem => problem.message).join(" ") || "The proposal is invalid under current source access." };
        const previous = structuredClone(origin.definition);
        setDraft({ ...draftRef.current, definition: structuredClone(proposal.definition) });
        setProposalUndo({ definition: previous, appliedRevision: String(workingRevision.current) });
        setAnnouncement("Proposal applied to the question draft. Nothing was saved. Undo is available.");
        return { outcome: "applied", summary: "Applied to the native question draft only; nothing was saved.", workingRevision: String(workingRevision.current) };
      } catch (cause) {
        return { outcome: "refused", summary: request.current() ? errorMessage(cause) : "The originating editor closed. Nothing was applied or saved." };
      } finally { request.finish(); }
    });
  }, [authoring, draft, saved, generationId, kind, native, lifetime, accessUnavailable]);
  const denyProtectedContent = () => {
    accessUnavailableRef.current = true;
    lifetime.invalidate(); previewRequest.current?.abort(); operation.current?.abort();
    setAccessUnavailable(true); setResult(undefined); setDrillResult(undefined); setResultDefinition(undefined); setResultResource(undefined);
    const redacted: DashboardResource = { ...savedRef.current, name: "", description: "", definition: undefined, tiles: [], capabilities: { canRead: false, canEdit: false, canCopy: false, canShare: false, canDelete: false } };
    savedRef.current = redacted; setSaved(redacted); setDraft(redacted); setProposalUndo(undefined);
  };
  const withholdResults = () => {
    // Neither a failed nor a cancelled read reauthorizes earlier facts.
    previewRequest.current?.abort(); setQuerying(false);
    lifetime.cancel("drill"); setDrilling(false); setDrillSource(undefined); setDrillResult(undefined);
    setResultWithheld(previous => previous || Boolean(result)); setResult(undefined); setResultDefinition(undefined); setResultResource(undefined);
    setResultFingerprint(undefined); setResultCanDrill(false); setStale(false);
  };
  const notify = (title: string) => api.notifications?.notify({ level: "success", title });
  const accepted = (resource: DashboardResource) => { savedRef.current = resource; setSaved(resource); setDraft(resource); setProposalUndo(undefined); setDialog(null); };
  const runMutation = async (work: (signal: AbortSignal) => Promise<DashboardResource>, title: string, navigateToResult = false) => {
    if (operation.current || !lifetime.capture()()) return;
    const controller = new AbortController(); operation.current = controller; setBusy(true); setError(undefined);
    try { const resource = await work(controller.signal); if (controller.signal.aborted) return; accepted(resource); notify(title); if (!initial.id || navigateToResult) navigate(resourcePath(kind, resource.id)); }
    catch (cause) { if (!controller.signal.aborted) { setError(errorMessage(cause)); if (accessLost(cause)) denyProtectedContent(); } }
    finally { if (!controller.signal.aborted) { setBusy(false); operation.current = undefined; } }
  };
  const saveDraftAsNew = async () => {
    if (!draftRef.current.definition || !canCreateQuestion || busy) return;
    const current = lifetime.capture();
    const snapshot = structuredClone(draftRef.current);
    const name = await api.dialogs.prompt({ title: "Save draft as new question", message: "Create a new personal question from this unsaved draft. It has independent ownership and no inherited audience, history or results.", defaultValue: `${snapshot.name} copy` });
    if (!name?.trim() || !current()) return;
    const content = { kind: "question" as const, name: name.trim(), description: snapshot.description, definition: snapshot.definition, definitionState: "available" as const };
    const fingerprint = JSON.stringify(content);
    let key = draftCreateAttempts.current.get(fingerprint);
    if (!key) { key = uuid(); draftCreateAttempts.current.set(fingerprint, key); }
    const operationKey = key;
    await runMutation(async signal => {
      await native.get("question", savedRef.current.id, signal);
      const validation = await native.validate(content.definition!, signal);
      if (!validation.isValid) throw new Error(validation.problems.map(problem => problem.message).join(" ") || "This draft is not valid under current source access.");
      return native.save(content, operationKey, signal);
    }, "Draft saved as a new personal question", true);
  };
  const preview = async () => {
    if (!draft.definition) return;
    previewRequest.current?.abort(); const controller = new AbortController(); previewRequest.current = controller;
    const submitted = draft.definition; const fingerprint = JSON.stringify(submitted); setQuerying(true); setError(undefined);
    try {
      const value = saved.id
        ? definitionDirty ? await native.savedPreview(saved.id, saved.revision, submitted, controller.signal) : await native.execute(saved.id, saved.revision, controller.signal)
        : await native.preview(submitted, controller.signal);
      if (controller.signal.aborted) return;
      setResult(value); setResultDefinition(submitted); setResultResource(!definitionDirty && saved.id ? saved : undefined);
      setResultCanDrill(!saved.id || !definitionDirty); setResultFingerprint(fingerprint); setStale(false); setResultWithheld(false);
    }
    catch (cause) { if (!controller.signal.aborted) {
      setError(errorMessage(cause));
      if (accessLost(cause)) denyProtectedContent();
      else withholdResults();
    } }
    finally { if (!controller.signal.aborted) setQuerying(false); }
  };
  const cancelPreview = () => {
    previewRequest.current?.abort(); setQuerying(false); withholdResults();
    setError("Query cancelled. No saved definition was changed.");
  };
  const drill = async (row: QueryRow, cursor?: string) => {
    if (!resultDefinition || !row.drill) return;
    const request = lifetime.begin("drill"); setDrilling(true); setDrillSource(row); setDrillError(undefined); setDrillResult(undefined);
    try {
      const response = resultResource ? await native.savedDrill(resultResource, row.drill, request.signal, cursor) : await native.drill({ question: resultDefinition, descriptor: row.drill, pageSize: 25, cursor }, request.signal);
      if (request.current()) setDrillResult(response);
    } catch (cause) { if (request.current()) { setDrillError(errorMessage(cause)); if (accessLost(cause)) { setError(errorMessage(cause)); denyProtectedContent(); } else withholdResults(); } }
    finally { if (request.current()) setDrilling(false); request.finish(); }
  };
  const closeDrill = () => {
    if (drilling) { withholdResults(); setError("Investigation cancelled. No saved definition was changed."); }
    lifetime.cancel("drill"); setDrilling(false); setDrillSource(undefined); setDrillResult(undefined); setDrillError(undefined);
  };

  const moveTile = (tileId: string, delta: number) => {
    const tiles = [...(draft.tiles ?? [])]; const from = tiles.findIndex(tile => tile.id === tileId); const to = from + delta;
    if (from < 0 || to < 0 || to >= tiles.length) return;
    [tiles[from], tiles[to]] = [tiles[to], tiles[from]]; setDraft({ ...draft, tiles }); setAnnouncement(`Tile moved to position ${to + 1} of ${tiles.length}. Save layout to keep this change.`);
    requestAnimationFrame(() => pageRef.current?.querySelector<HTMLButtonElement>(`[data-tile-id="${CSS.escape(tileId)}"] button[data-move="${delta}"]`)?.focus());
  };
  const openPicker = () => setDialog("questions");
  if (accessUnavailable) return <StudioPage className="named-workspace"><StudioPageHeader headingLevel={2} title="Definition unavailable" /><StudioResultState tone="danger" title="Current access could not authorize this definition" description={error || "Protected definition and result content has been cleared. Reload to check your current access."} actions={<><StudioButton onClick={onReload}>Reload current access</StudioButton><StudioButton onClick={() => navigate(resourcePath(kind))}>Back to collection</StudioButton></>} /></StudioPage>;
  return <StudioPage className="named-workspace"><div className="named-editor-content" ref={pageRef}><StudioPageHeader headingLevel={2} title={draft.id ? draft.name : `Create ${kind}`} description={draft.id ? `Revision ${saved.revision}${dirty ? " · Unsaved changes" : ""}` : "Personal until you explicitly share it."} actions={<>
    {canEdit ? <StudioButton variant="primary" disabled={busy || !draft.name.trim() || kind === "question" && !draft.definition && draft.definitionState !== "withheld"} onClick={() => void runMutation(signal => native.save(draft, createKey.current, signal), kind === "question" ? "Question saved" : "Dashboard saved")}>{busy ? "Saving…" : kind === "question" ? "Save question" : "Save layout"}</StudioButton> : null}
    {saved.id && saved.capabilities.canCopy ? <StudioButton disabled={busy} onClick={() => { const current = lifetime.capture(); void api.dialogs.prompt({ title: "Save a private copy", message: kind === "dashboard" ? "The copy keeps the saved question references. Source access is still required." : "Copies the exact saved definition, with no audience, history or results. Use Save draft as new question to keep unsaved question edits.", defaultValue: `${saved.name} copy` }).then(name => { if (name?.trim() && current()) { const fingerprint = `saved-copy:${saved.id}:${saved.revision}:${name}`; let operationKey = draftCreateAttempts.current.get(fingerprint); if (!operationKey) { operationKey = uuid(); draftCreateAttempts.current.set(fingerprint, operationKey); } const key = operationKey; void runMutation(signal => native.copy(saved, name, key, signal), "Private copy saved", true); } }); }}>Copy saved version</StudioButton> : null}
    {saved.id && kind === "question" && dirty && draft.definition && draft.definitionState !== "withheld" && canCreateQuestion ? <StudioButton disabled={busy} onClick={() => void saveDraftAsNew()}>Save draft as new question</StudioButton> : null}
    {saved.id && saved.capabilities.canShare ? <StudioButton disabled={busy || dirty} onClick={() => setDialog("share")}>Share</StudioButton> : null}
    {saved.id ? <StudioButton disabled={busy || dirty} onClick={() => setDialog("history")}>History</StudioButton> : null}
    <StudioButton onClick={() => { const current = lifetime.capture(); void (dirty ? api.dialogs.confirm({ message: "Discard unsaved changes and return to the collection?" }) : Promise.resolve(true)).then(yes => { if (yes && current()) navigate(resourcePath(kind)); }); }}>Back to {kind === "question" ? "questions" : "dashboards"}</StudioButton>
  </>} />
    {proposalUndo ? <div className="named-actions"><StudioButton disabled={busy || proposalUndo.appliedRevision !== String(workingRevision.current)} onClick={() => { if (proposalUndo.appliedRevision === String(workingRevision.current)) { setDraft({ ...draftRef.current, definition: structuredClone(proposalUndo.definition) }); setProposalUndo(undefined); setAnnouncement("Proposal undone. Nothing was saved."); } }}>Undo applied proposal</StudioButton><p>Proposal changes stay in this draft until Save. Undo is available until another edit or save.</p></div> : null}
    {!canEdit ? <p className="named-access-note">Read-only definition. Data uses your current access.</p> : null}
    {error ? <div className="named-inline-error" role="alert"><p>{error}</p>{saved.id ? <StudioButton onClick={() => { const current = lifetime.capture(); void api.dialogs.confirm({ message: "Reload the saved definition? Unsaved changes will be discarded." }).then(yes => { if (yes && current()) onReload(); }); }}>Reload saved definition</StudioButton> : null}<p>Your editable draft remains here. A successful local edit does not mean it was saved.</p></div> : null}
    <div className="named-definition-meta"><label>Name<input maxLength={200} value={draft.name} disabled={!canEdit || busy} onChange={event => setDraft({ ...draft, name: event.target.value })} /></label><label>Description<textarea maxLength={2000} value={draft.description ?? ""} disabled={!canEdit || busy} onChange={event => setDraft({ ...draft, description: event.target.value })} /></label></div>
    {kind === "question" ? <>
      <DashboardAskWeaver controller={authoring} generationId={generationId} />
      {sourceError ? <p role="alert">Dataset discovery unavailable: {sourceError}. Existing definition is preserved.</p> : null}
      {draft.definitionState === "withheld" ? <p role="status">Parts of this definition are withheld under your current access. Metadata-only save preserves its query; copying or replacing withheld content requires complete disclosure rights.</p> : draft.definition ? <div className="named-authoring"><QueryControls definition={draft.definition} datasets={datasets} disabled={!canEdit || busy} onChange={definition => setDraft({ ...draft, definition })} /><section className="named-preview" aria-label="Preview"><h3>Preview</h3><p>Results use your current permissions. Ordinary editing, saving and refresh do not require an AI provider.</p><div className="named-actions"><StudioButton disabled={querying || !draft.definition.query.dataset.id} onClick={() => void preview()}>{querying ? "Querying…" : saved.id && !definitionDirty ? "Refresh results" : "Preview question"}</StudioButton>{querying ? <StudioButton onClick={cancelPreview}>Cancel query</StudioButton> : null}</div>{result ? <ResultView result={result} visualization={resultDefinition?.visualization ?? draft.definition.visualization} stale={stale} outdated={outdated} onDrill={resultCanDrill ? row => void drill(row) : undefined} /> : <p role="status">Run a preview to see actual source results and coverage.</p>}{resultWithheld ? <p role="status">Previous result values are withheld because current data access could not be established. Your draft remains available. Retry the preview or refresh.</p> : null}{result && !resultCanDrill ? <p className="named-help">Save this question before investigating its changed definition.</p> : null}</section></div> : null}
      {saved.id ? <StudioButton onClick={() => navigate("/dashboards")}>Arrange on a dashboard</StudioButton> : null}
    </> : <>
      <div className="named-actions">{canEdit ? <StudioButton disabled={busy || (draft.tiles?.length ?? 0) >= 50} onClick={openPicker}>Add saved question</StudioButton> : null}<StudioButton disabled={!saved.id || dirty} onClick={() => setRefresh(value => value + 1)}>Refresh all</StudioButton>{dirty ? <p className="named-help">Save layout before refreshing its exact saved revision.</p> : null}</div>
      {!draft.tiles?.length ? <StudioResultState title="No questions on this dashboard" description="Add an accessible saved question. Each tile can use its own size and presentation." /> : <div className="named-tile-grid">{draft.tiles.map((tile, index) => <article className="named-tile" data-size={tile.size} data-tile-id={tile.id} key={tile.id}><header><h3>Question {index + 1}</h3>{canEdit ? <div className="named-actions"><StudioButton data-move="-1" disabled={busy || index === 0} aria-label={`Move question ${index + 1} earlier`} onClick={() => moveTile(tile.id, -1)}>Move earlier</StudioButton><StudioButton data-move="1" disabled={busy || index === draft.tiles!.length - 1} aria-label={`Move question ${index + 1} later`} onClick={() => moveTile(tile.id, 1)}>Move later</StudioButton><label>Size for question {index + 1}<select disabled={busy} value={tile.size} onChange={event => setDraft({ ...draft, tiles: draft.tiles?.map(item => item.id === tile.id ? { ...item, size: event.target.value as DashboardTile["size"] } : item) })}>{["small", "medium", "wide", "full"].map(item => <option key={item}>{item}</option>)}</select></label>{tile.referenceState !== "unavailable" && tile.questionId ? <StudioButton disabled={busy} onClick={() => setEditingTile(tile)}>Settings for question {index + 1}</StudioButton> : null}<StudioButton disabled={busy} onClick={() => setDraft({ ...draft, tiles: draft.tiles?.filter(item => item.id !== tile.id) })}>Remove tile {index + 1}</StudioButton></div> : null}</header>{tile.referenceState === "unavailable" || !tile.questionId ? <p role="status">This question is unavailable under your current access. Its reference is preserved during layout saves.</p> : <QuestionTile api={native} dashboard={saved} tile={tile} refresh={refresh} draftLayout={dirty} navigate={navigate} />}</article>)}</div>}
      <p aria-live="polite" className="named-announcement">{announcement}</p>
    </>}
    {saved.id && saved.capabilities.canDelete ? <div className="named-danger-actions"><StudioButton disabled={busy} onClick={() => { const current = lifetime.capture(); void api.dialogs.confirm({ title: `Delete ${kind}`, message: kind === "question" ? "Delete this question? Dashboard references will become unavailable. Underlying records will remain." : "Delete this dashboard? Its saved questions will remain.", tone: "danger" }).then(yes => { if (yes && current()) { const request = lifetime.begin("delete"); setBusy(true); void native.remove(saved, request.signal).then(() => { if (!request.current()) return; notify(`${kind === "question" ? "Question" : "Dashboard"} deleted`); navigate(resourcePath(kind)); }).catch(cause => { if (request.current()) setError(errorMessage(cause)); }).finally(() => { if (request.current()) setBusy(false); request.finish(); }); } }); }}>Delete {kind}</StudioButton></div> : null}
    {editingTile ? <TileOptionsDialog api={native} tile={editingTile} onClose={() => setEditingTile(undefined)} onApply={tile => { setDraft({ ...draft, tiles: draft.tiles?.map(item => item.id === tile.id ? tile : item) }); setEditingTile(undefined); }} /> : null}
    {dialog === "share" ? <ShareDialog api={native} resource={saved} onClose={() => setDialog(null)} onSaved={resource => { accepted(resource); notify("Audience saved"); }} /> : null}
    {dialog === "history" ? <HistoryDialog api={native} resource={saved} onClose={() => setDialog(null)} onRestored={resource => { accepted(resource); notify("Definition restored as a new revision"); }} /> : null}
    {dialog === "questions" ? <QuestionPickerDialog api={native} onClose={() => setDialog(null)} onPick={question => { setDraft({ ...draftRef.current, tiles: [...(draftRef.current.tiles ?? []), { id: uuid(), questionId: question.id, size: "medium", referenceState: "available" }] }); setDialog(null); }} onCreate={() => { const current = lifetime.capture(); void (dirty ? api.dialogs.confirm({ message: "Save or discard the dashboard layout before leaving to create a question. Discard unsaved changes?" }) : Promise.resolve(true)).then(yes => { if (yes && current()) navigate("/questions/new"); }); }} /> : null}
    {drilling || drillResult || drillError ? <NativeDialog title="Matching source records" onClose={closeDrill}>{drilling ? <p role="status">Loading authorized source records…</p> : drillError ? <p role="alert">{drillError}</p> : drillResult ? <><ResultView result={drillResult} visualization={{ kind: "table", y: [] }} />{drillResult.nextCursor && drillSource ? <StudioButton onClick={() => void drill(drillSource, drillResult.nextCursor)}>Next matching records</StudioButton> : null}</> : null}</NativeDialog> : null}
  </div></StudioPage>;
}

type QuestionTileProps = { api: DashboardApi; dashboard: DashboardResource; tile: DashboardTile; refresh: number; draftLayout: boolean; navigate(path: string): void };
export function QuestionTile(props: QuestionTileProps) {
  const { dashboard, tile, refresh, draftLayout } = props;
  // A new binding owns fresh protected state before paint, including retained tile IDs.
  const binding = JSON.stringify([dashboard.id, dashboard.revision, tile.id, tile.questionId,
    tile.presentation, tile.parameters, refresh, draftLayout]);
  return <QuestionTileContent key={binding} {...props} />;
}

function QuestionTileContent({ api, dashboard, tile, draftLayout, navigate }: QuestionTileProps) {
  const [question, setQuestion] = useState<DashboardResource>();
  const [result, setResult] = useState<QueryResult>();
  const [error, setError] = useState<string>();
  const [loading, setLoading] = useState(false);
  const [withheld, setWithheld] = useState(false);
  const [drilled, setDrilled] = useState<QueryResult>();
  const [drillError, setDrillError] = useState<string>();
  const [drilling, setDrilling] = useState(false);
  const [drillSource, setDrillSource] = useState<QueryRow>();
  const drillRequest = useRef<AbortController | undefined>(undefined);
  useEffect(() => () => drillRequest.current?.abort(), []);
  const drill = async (row: QueryRow, cursor?: string) => {
    if (!question || !row.drill) return;
    drillRequest.current?.abort(); const controller = new AbortController(); drillRequest.current = controller;
    setDrilling(true); setDrillSource(row); setDrillError(undefined); setDrilled(undefined);
    try { const response = await api.tileDrill(dashboard, tile.id, question.revision, row.drill, controller.signal, cursor); if (!controller.signal.aborted) setDrilled(response); }
    catch (cause) { if (!controller.signal.aborted) { setDrillError(errorMessage(cause)); setResult(undefined); setDrilled(undefined); setWithheld(true); if (accessLost(cause)) setQuestion(undefined); } }
    finally { if (!controller.signal.aborted) setDrilling(false); }
  };
  useEffect(() => {
    const controller = new AbortController(); setLoading(true); setError(undefined); setResult(undefined); setQuestion(undefined); setWithheld(false);
    drillRequest.current?.abort(); setDrilled(undefined); setDrillError(undefined); setDrilling(false);
    void (async () => { try { const resource = await api.get("question", tile.questionId!, controller.signal); if (controller.signal.aborted) return; setQuestion(resource); if (!dashboard.id || draftLayout) return; const response = await api.tile(dashboard, tile.id, resource.revision, controller.signal); if (!controller.signal.aborted) setResult(response); } catch (cause) { if (!controller.signal.aborted) { setError(errorMessage(cause)); setResult(undefined); setDrilled(undefined); setWithheld(true); if (accessLost(cause)) setQuestion(undefined); } } finally { if (!controller.signal.aborted) setLoading(false); } })();
    return () => controller.abort();
  }, [api, dashboard, tile.id, tile.questionId, draftLayout]);
  return <div className="named-tile-body">{question ? <a href={resourcePath("question", question.id)} onClick={event => { event.preventDefault(); navigate(resourcePath("question", question.id)); }}>{question.name}</a> : null}{loading ? <p role="status">Refreshing question…</p> : null}{error ? <p role="alert">{error}</p> : null}{withheld ? <p role="status">Result values are withheld because current data access could not be established. Refresh the dashboard to retry.</p> : null}{draftLayout ? <p role="status">Save this layout to execute its exact tile bindings.</p> : result ? <ResultView result={result} visualization={tile.presentation ?? question?.definition?.visualization ?? { kind: "table", y: [] }} onDrill={row => void drill(row)} /> : !loading && !error ? <p role="status">Results have not been loaded.</p> : null}{drilling || drilled || drillError ? <NativeDialog title="Matching source records" onClose={() => { if (drilling) { setResult(undefined); setWithheld(true); setError("Investigation cancelled. Refresh the dashboard to retry."); } drillRequest.current?.abort(); setDrilling(false); setDrillSource(undefined); setDrilled(undefined); setDrillError(undefined); }}>{drilling ? <p role="status">Loading authorized source records…</p> : drillError ? <p role="alert">{drillError}</p> : drilled ? <><ResultView result={drilled} visualization={{ kind: "table", y: [] }} />{drilled.nextCursor && drillSource ? <StudioButton onClick={() => void drill(drillSource, drilled.nextCursor)}>Next matching records</StudioButton> : null}</> : null}</NativeDialog> : null}</div>;
}
