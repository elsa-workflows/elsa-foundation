import React, { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
import { useAuthSession, usePermissions, useStudioRouteLocation, useStudioWorkspaceTab, useStudioWorkspaceWhenActive, studioNavigationRequestedEvent, notifyStudioNavigationGuardReleased, type StudioNavigationRequest, type ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { studioRowInteraction, OverflowMenu, PeerViewSwitch, SkeletonRows, StudioButton, StudioDialogFooter, StudioDialog, StudioDialogFooter, StudioField, StudioTimestamp, StudioCollectionFooter, StudioCollectionToolbar, StudioPage, StudioPageHeader, StudioResultState, StudioSearchInput, StudioTableContainer } from "@elsa-workflows/studio-ui";
import { createHomeLayoutScope } from "../homeLayout";
import { DashboardApi } from "./api";
import { QueryControls } from "./QueryControls";
import { ResultView } from "./ResultView";
import { DashboardDependencyReview, HistoryDialog, NativeDialog, ShareDialog, TileOptionsDialog, QuestionPickerDialog } from "./ResourceDialogs";
import { accessLost, errorMessage, initialDefinition, resourcePath, type DashboardResource, type DashboardTile, type DatasetDeclaration, type QueryResult, type QueryRow, type QuestionDefinition, type QueryProblem } from "./types";
import { useRequestLifetime } from "./useRequestLifetime";
import { dashboardActorScope } from "./requestScheduler";
import { sameAuthoringTarget, type DashboardAuthoringController, type DashboardAuthoringSnapshot } from "./authoring";
import { DashboardAskWeaver } from "./weaverAuthoring";
import "./native.css";

type RouteProps = { api: ElsaStudioModuleApi; kind: "question" | "dashboard"; view: "list" | "create" | "detail" | "edit"; authoring?: DashboardAuthoringController; navigate(path: string): void };
const uuid = () => crypto.randomUUID();

/** The host can retain mounted tabs. Key all protected native state by actual authenticated/backend scope. */
export function NamedDashboardsRoute(props: RouteProps) {
  const session = useAuthSession();
  const location = useStudioRouteLocation();
  const pathname = location.pathname;
  const scope = JSON.stringify(createHomeLayoutScope(props.api, session));
  const segments = pathname.split("/").filter(Boolean);
  const id = props.view === "detail" || props.view === "edit" ? decodeURIComponent(segments[segments.length - (props.view === "edit" ? 2 : 1)] ?? "") : undefined;
  const actorScope = dashboardActorScope(props.api.backendSession?.identity ?? session);
  const native = useMemo(() => new DashboardApi(props.api.backend, actorScope), [props.api.backend, actorScope]);
  const key = `${scope}:${props.kind}:${props.view}:${id ?? ""}`;
  return props.view === "list" ? <ResourceCollection key={key} {...props} native={native} /> : <ResourceRoute key={key} {...props} native={native} id={id} />;
}

function ResourceCollection({ api, native, kind, navigate }: RouteProps & { native: DashboardApi }) {
  const permissions = usePermissions();
  const [ownership, setOwnership] = useState("mine");
  const [search, setSearch] = useState("");
  const [appliedSearch, setAppliedSearch] = useState("");
  const submittedSearch = useRef("");
  const [items, setItems] = useState<DashboardResource[]>([]);
  const itemsRef = useRef(items); itemsRef.current = items;
  const [cursor, setCursor] = useState<string | null>();
  const [error, setError] = useState<string>();
  const [deleteError, setDeleteError] = useState<string>();
  const [loading, setLoading] = useState(true);
  const request = useRef<AbortController | undefined>(undefined);
  const lifetime = useRequestLifetime();
  const collectionRef = useRef({ ownership, search: submittedSearch.current });
  collectionRef.current = { ownership, search: submittedSearch.current };
  const deletingRef = useRef<string | undefined>(undefined);
  const [deletingId, setDeletingId] = useState<string>();
  const canCreate = permissions.has("*") || permissions.has(`studio/${kind}s:create`);
  const title = kind === "question" ? "Questions" : "Dashboards";
  const load = useCallback(async (query: string, next?: string | null) => {
    request.current?.abort(); const controller = new AbortController(); request.current = controller;
    // Ownership may change before this request resolves. Keep the latest submitted query,
    // rather than reverting to either an empty search or the last successful response.
    submittedSearch.current = query; setAppliedSearch(query); setLoading(true); setError(undefined); setDeleteError(undefined);
    try { const page = await native.list(kind, ownership, query, next, controller.signal); if (controller.signal.aborted) return; setItems(current => next ? [...current, ...page.items] : page.items); setCursor(page.continuationToken); }
    catch (cause) { if (!controller.signal.aborted) { setItems([]); setCursor(null); setError(errorMessage(cause)); } }
    finally { if (!controller.signal.aborted) setLoading(false); }
  }, [native, kind, ownership]);
  useEffect(() => { setItems([]); void load(submittedSearch.current); return () => request.current?.abort(); }, [load]);
  const loadRef = useRef(load); loadRef.current = load;
  const deleteResource = async (item: DashboardResource, button: HTMLButtonElement) => {
    if (!item.capabilities.canDelete || deletingRef.current) return;
    const scope = JSON.stringify([ownership, submittedSearch.current]);
    const current = lifetime.capture();
    const confirmed = await api.dialogs.confirm({ title: `Delete ${kind}`, message: kind === "dashboard" ? `Delete "${item.name}"? Its saved questions will remain.` : `Delete "${item.name}"? Dashboard references will become unavailable. Underlying records will remain.`, tone: "danger" });
    if (!confirmed || !current() || deletingRef.current || JSON.stringify([collectionRef.current.ownership, collectionRef.current.search]) !== scope) return;
    const latest = itemsRef.current.find(candidate => candidate.id === item.id && candidate.revision === item.revision && candidate.capabilities.canDelete);
    if (!latest) return;
    const operation = lifetime.begin("collection-delete");
    deletingRef.current = latest.id; setDeletingId(latest.id); setDeleteError(undefined);
    try {
      await native.remove(latest, operation.signal);
      if (!operation.current()) return;
      if (JSON.stringify([collectionRef.current.ownership, collectionRef.current.search]) === scope) {
        setItems(currentItems => currentItems.filter(candidate => candidate.id !== latest.id));
        button.closest(".named-workspace")?.querySelector<HTMLInputElement>(`[aria-label="Search ${title.toLowerCase()}"]`)?.focus();
      }
      deletingRef.current = undefined; setDeletingId(undefined); operation.finish();
      void loadRef.current(collectionRef.current.search);
    } catch (cause) {
      if (operation.current()) {
        if (JSON.stringify([collectionRef.current.ownership, collectionRef.current.search]) === scope) setDeleteError(errorMessage(cause));
        deletingRef.current = undefined; setDeletingId(undefined);
      }
    } finally { operation.finish(); }
  };
  const showActions = items.some(item => item.capabilities.canDelete);
  return <StudioPage className="named-workspace" data-studio-collection><StudioPageHeader headingLevel={2} title={title} description={kind === "question" ? "Save a question once. Reuse it with your current data access." : "Arrange saved questions into personal and shared views."} actions={<>{canCreate ? <StudioButton variant="primary" onClick={() => navigate(`${resourcePath(kind)}/new`)}>New {kind}</StudioButton> : null}<StudioButton disabled={loading} onClick={() => void load(appliedSearch)}>Refresh</StudioButton></>} />
    <StudioCollectionToolbar><form className="named-search" onSubmit={event => { event.preventDefault(); void load(search); }}><StudioSearchInput aria-label={`Search ${title.toLowerCase()}`} value={search} onChange={event => setSearch(event.target.value)} /><StudioButton type="submit">Search</StudioButton></form><PeerViewSwitch ariaLabel="Ownership" value={ownership} onChange={setOwnership}
      views={[{ id: "mine", label: "Mine" }, { id: "shared", label: "Shared with me" }]} /></StudioCollectionToolbar>
    {deleteError ? <div className="named-inline-error" role="alert"><p>{deleteError}</p></div> : null}
    {loading && !items.length ? <SkeletonRows label={`Loading ${title.toLowerCase()}`} /> : error ? <StudioResultState tone="danger" title={`${title} could not be loaded`} description={error} actions={<StudioButton onClick={() => void load(appliedSearch)}>Retry</StudioButton>} /> : !items.length ? <StudioResultState title={appliedSearch ? "No matching definitions" : ownership === "shared" ? "Nothing has been shared with you" : `No ${title.toLowerCase()} yet`} description={appliedSearch ? "Change your search to see other definitions." : kind === "question" ? "Create a supported question using ordinary controls or Weaver." : "Create a dashboard and add accessible saved questions."} actions={appliedSearch ? <StudioButton onClick={() => { setSearch(""); void load(""); }}>Clear search</StudioButton> : canCreate ? <StudioButton onClick={() => navigate(`${resourcePath(kind)}/new`)}>New {kind}</StudioButton> : undefined} /> : <StudioTableContainer role="region" aria-label={title} tabIndex={0}><table className="studio-table named-resource-table"><thead><tr><th scope="col">Name</th><th scope="col">Description</th><th scope="col">Access</th><th scope="col">Updated</th>{showActions ? <th scope="col">Actions</th> : null}</tr></thead><tbody>{items.map(item => <tr key={item.id} {...studioRowInteraction<HTMLTableRowElement>(() => navigate(resourcePath(kind, item.id)))}><td><a className="studio-row-link" href={resourcePath(kind, item.id)} onClick={event => { if (!event.metaKey && !event.ctrlKey && !event.shiftKey && event.button === 0) { event.preventDefault(); navigate(resourcePath(kind, item.id)); } }}>{item.name}</a></td><td>{item.description}</td><td>{item.capabilities.canShare ? "Owner" : item.capabilities.canEdit ? "Can edit" : "Can view"}</td><td><StudioTimestamp value={item.updatedAt} /></td>{showActions ? <td>{item.capabilities.canDelete ? <StudioButton aria-label={`Delete ${item.name}`} disabled={deletingId !== undefined} onClick={event => void deleteResource(item, event.currentTarget)}>Delete</StudioButton> : null}</td> : null}</tr>)}</tbody></table></StudioTableContainer>}
    {!error && items.length ? <StudioCollectionFooter>{items.length} loaded {title.toLowerCase()}{cursor ? <StudioButton disabled={loading} onClick={() => void load(appliedSearch, cursor)}>Load more</StudioButton> : null}</StudioCollectionFooter> : null}
  </StudioPage>;
}

// Current disclosure can change while the saved layout revision stays the same. Keep local
// order/size/removal edits, but replace any retained binding that the server now withholds.
function reconcileTileDisclosure(local: DashboardTile[] | undefined, previous: DashboardTile[] | undefined, current: DashboardTile[] | undefined) {
  return local?.map(tile => {
    const before = previous?.find(item => item.id === tile.id);
    const fresh = current?.find(item => item.id === tile.id);
    if (!before || !fresh) return tile;
    const retained = tile.referenceState === "unavailable" || tile.preserveReference || tile.questionId === before.questionId;
    if (!retained) return tile; // A deliberate local replacement has its own validation on save.
    if (fresh.referenceState === "unavailable" || !fresh.questionId)
      return { ...fresh, size: tile.size };
    return tile.referenceState === "unavailable" || tile.preserveReference ? { ...fresh, size: tile.size } : tile;
  });
}

type ResourceMetadata = { name: string; description: string };

function NewResourceDialog({ kind, onCreate, onCancel }: {
  kind: RouteProps["kind"];
  onCreate(metadata: ResourceMetadata): void;
  onCancel(): void;
}) {
  const nameInput = useRef<HTMLInputElement>(null);
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  return <StudioDialog open title={`New ${kind}`} onClose={onCancel} initialFocusRef={nameInput}
    onSubmit={event => {
      event.preventDefault();
      if (name.trim()) onCreate({ name: name.trim(), description: description.trim() });
    }}
    footer={<StudioDialogFooter onCancel={onCancel} confirmLabel="Create" confirmType="submit" confirmDisabled={!name.trim()} />}>
    <StudioField label="Name"><input ref={nameInput} value={name} onChange={event => setName(event.target.value)} required maxLength={200} /></StudioField>
    <StudioField label="Description"><textarea value={description} onChange={event => setDescription(event.target.value)} maxLength={2000} /></StudioField>
  </StudioDialog>;
}

type AccessReview = { state: "checking" | "available" | "unavailable" | "denied"; message?: string };
function ResourceRoute({ api, native, kind, id, navigate, authoring, view }: RouteProps & { native: DashboardApi; id?: string }) {
  const permissions = usePermissions();
  const [resource, setResource] = useState<DashboardResource>();
  const [datasets, setDatasets] = useState<DatasetDeclaration[]>([]);
  const [review, setReview] = useState<AccessReview>({ state: "checking" });
  const [sourceError, setSourceError] = useState<string>();
  const [attempt, setAttempt] = useState(0);
  const [editorReset, setEditorReset] = useState(0);
  const [newMetadata, setNewMetadata] = useState<ResourceMetadata>();
  // Activity restarts effects when a retained tab returns. Revalidate before paint while
  // retaining its editor component and local working state throughout the check.
  useLayoutEffect(() => {
    const controller = new AbortController(); setReview({ state: "checking" });
    if (kind === "question") void native.datasets(controller.signal).then(page => { if (!controller.signal.aborted) { setDatasets(page.items); setSourceError(undefined); } }).catch(cause => { if (!controller.signal.aborted) setSourceError(errorMessage(cause)); });
    if (id) void native.get(kind, id, controller.signal).then(value => { if (!controller.signal.aborted) { setResource(value); setReview({ state: "available" }); } }).catch(cause => { if (!controller.signal.aborted) setReview({ state: accessLost(cause) ? "denied" : "unavailable", message: errorMessage(cause) }); });
    else {
      setResource(current => current ?? { id: "", kind, name: "", description: "", revision: "", ownerId: "", capabilities: { canRead: true, canEdit: true, canShare: false, canDelete: false, canCopy: false }, definition: kind === "question" ? initialDefinition() : undefined, definitionState: "available", tiles: [], createdAt: "", updatedAt: "" });
      setReview({ state: "available" });
    }
    return () => controller.abort();
  }, [native, kind, id, attempt]);
  if (!id && kind === "dashboard" && view === "create" && !permissions.has("*") && !permissions.has("studio/dashboards:create")) return <StudioPage className="named-workspace"><StudioPageHeader headingLevel={2} title="Dashboard creation unavailable" /><StudioResultState tone="danger" title="You cannot create dashboards" description="Ask an administrator for dashboard creation access." actions={<StudioButton onClick={() => navigate(resourcePath(kind))}>Back to dashboards</StudioButton>} /></StudioPage>;
  if (!resource && review.message) return <StudioPage className="named-workspace"><StudioPageHeader headingLevel={2} title={kind === "question" ? "Question unavailable" : "Dashboard unavailable"} /><StudioResultState tone="danger" title="Definition could not be opened" description={review.message} actions={<><StudioButton onClick={() => setAttempt(value => value + 1)}>Retry</StudioButton><StudioButton onClick={() => navigate(resourcePath(kind))}>Back to collection</StudioButton></>} /></StudioPage>;
  if (!resource) return <StudioPage className="named-workspace"><SkeletonRows label="Loading definition" /></StudioPage>;
  if (view === "create" && !newMetadata) return <>
    <ResourceCollection api={api} native={native} kind={kind} view="list" navigate={navigate} />
    <NewResourceDialog kind={kind} onCreate={setNewMetadata} onCancel={() => navigate(resourcePath(kind))} />
  </>;
  return <ResourceEditor key={`${resource.id || "new"}:${editorReset}`} api={api} native={native} initial={resource} newMetadata={newMetadata} review={review} authoring={authoring} datasets={datasets} sourceError={sourceError} onRetryAccess={() => setAttempt(value => value + 1)} onReload={() => { setReview({ state: "checking" }); setEditorReset(value => value + 1); setAttempt(value => value + 1); }} navigate={navigate} view={view} />;
}

function ResourceEditor({ api, native, initial, newMetadata, review, datasets, sourceError, onReload, onRetryAccess, navigate, authoring, view }: { api: ElsaStudioModuleApi; native: DashboardApi; initial: DashboardResource; newMetadata?: ResourceMetadata; review: AccessReview; authoring?: DashboardAuthoringController; datasets: DatasetDeclaration[]; sourceError?: string; onReload(): void; onRetryAccess(): void; navigate(path: string): void; view: RouteProps["view"] }) {
  const permissions = usePermissions();
  const canCreateQuestion = permissions.has("*") || permissions.has("studio/questions:create");
  const [saved, setSaved] = useState(initial);
  const [draft, replaceDraft] = useState(() => newMetadata ? { ...initial, ...newMetadata } : initial);
  const draftRef = useRef(draft);
  const savedRef = useRef(saved);
  draftRef.current = draft; savedRef.current = saved;
  const workingRevision = useRef(0);
  const generationId = useRef(uuid()).current;
  const lifetime = useRequestLifetime();
  const [proposalUndo, setProposalUndo] = useState<{ definition: QuestionDefinition; appliedRevision: string }>();
  const setDraft = useCallback((value: DashboardResource) => { workingRevision.current++; draftRef.current = value; replaceDraft(value); }, []);
  const [error, setError] = useState<string>();
  const [problems, setProblems] = useState<QueryProblem[]>([]);
  const [validationAttempt, setValidationAttempt] = useState(0);
  const [validating, setValidating] = useState(false);
  const [replacing, setReplacing] = useState(false);
  const [busy, setBusy] = useState(false);
  const busyRef = useRef(busy); busyRef.current = busy;
  const workspaceTab = useStudioWorkspaceTab(); const tabRef = useRef(workspaceTab); tabRef.current = workspaceTab;
  const whenActive = useStudioWorkspaceWhenActive();
  const [revisionConflict, setRevisionConflict] = useState(false);
  const [accessUnavailable, setAccessUnavailable] = useState(false);
  const accessUnavailableRef = useRef(false);
  const [dialog, setDialog] = useState<"share" | "history" | "questions" | "copy" | null>(null);
  const [copyDependenciesReady, setCopyDependenciesReady] = useState(false);
  const [result, setResult] = useState<QueryResult>();
  const resultRef = useRef(result); resultRef.current = result;
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
  const dashboardViewer = kind === "dashboard" && (view === "detail" || (view === "edit" && !canEdit));
  const guardEnabled = kind !== "dashboard" || (view !== "detail" && (view !== "edit" || canEdit));
  const dirty = JSON.stringify(draft) !== JSON.stringify(saved);
  const definitionFingerprint = JSON.stringify(draft.definition);
  const definitionDirty = definitionFingerprint !== JSON.stringify(saved.definition);
  const currentDefinition = useRef(definitionFingerprint);
  const validationDefinition = useRef(definitionFingerprint);
  const outdated = resultFingerprint !== undefined && resultFingerprint !== definitionFingerprint;
  useEffect(() => () => { previewRequest.current?.abort(); if (!tabRef.current || tabRef.current.isActive()) operation.current?.abort(); }, []);
  useEffect(() => { if (currentDefinition.current !== definitionFingerprint) { currentDefinition.current = definitionFingerprint; previewRequest.current?.abort(); setQuerying(false); } }, [definitionFingerprint]);
  useEffect(() => { if (validationDefinition.current !== definitionFingerprint) { validationDefinition.current = definitionFingerprint; lifetime.cancel("validation"); setValidating(false); setProblems([]); setError(current => current?.startsWith("Correct the marked question settings") ? undefined : current); } }, [definitionFingerprint, lifetime]);

  const denyProtectedContent = useCallback(() => {
    accessUnavailableRef.current = true;
    lifetime.invalidate(); previewRequest.current?.abort(); operation.current?.abort();
    setAccessUnavailable(true); setResult(undefined); setDrillResult(undefined); setResultDefinition(undefined); setResultResource(undefined);
    const redacted: DashboardResource = { ...savedRef.current, name: "", description: "", definition: undefined, tiles: [], capabilities: { canRead: false, canEdit: false, canCopy: false, canShare: false, canDelete: false } };
    savedRef.current = redacted; setSaved(redacted); setDraft(redacted); setProposalUndo(undefined);
  }, [lifetime, setDraft]);
  const historyAccessLost = useCallback((message: string) => {
    setError(message); setDialog(null); denyProtectedContent();
  }, [denyProtectedContent]);
  const withholdResults = useCallback(() => {
    // Neither a failed nor a cancelled read reauthorizes earlier facts.
    previewRequest.current?.abort(); setQuerying(false);
    lifetime.cancel("drill"); setDrilling(false); setDrillSource(undefined); setDrillResult(undefined);
    const hadResult = Boolean(resultRef.current);
    setResultWithheld(previous => previous || hadResult); setResult(undefined); setResultDefinition(undefined); setResultResource(undefined);
    setResultFingerprint(undefined); setResultCanDrill(false); setStale(false);
  }, [lifetime]);
  useLayoutEffect(() => { withholdResults(); }, [withholdResults]);
  const reviewedInitial = useRef(initial);
  useLayoutEffect(() => {
    if (review.state === "denied") { setError(review.message); denyProtectedContent(); return; }
    if (review.state !== "available") { withholdResults(); return; }
    if (!initial.capabilities.canRead) { setError("Current definition disclosure is unavailable."); denyProtectedContent(); return; }
    if (reviewedInitial.current === initial) return;
    if (initial.definitionState === "withheld") {
      if (reviewedInitial.current !== initial || draftRef.current.definition) {
        const persisted = savedRef.current;
        const changed = JSON.stringify(draftRef.current) !== JSON.stringify(persisted);
        const conflict = changed && persisted.revision !== initial.revision;
        const baseline = conflict ? { ...persisted, capabilities: initial.capabilities, definition: undefined, definitionState: "withheld" as const } : initial;
        savedRef.current = baseline; setSaved(baseline);
        const replacement = replacing && initial.capabilities.canEdit && draftRef.current.definition;
        setDraft(replacement ? { ...draftRef.current, capabilities: initial.capabilities } : changed ? { ...draftRef.current, capabilities: initial.capabilities, definition: undefined, definitionState: "withheld" } : baseline);
        setProposalUndo(undefined); setRevisionConflict(conflict); withholdResults(); reviewedInitial.current = initial;
        if (conflict) setError("The saved definition changed and query disclosure is unavailable. Review current access before saving metadata.");
      }
      return;
    }
    reviewedInitial.current = initial;
    const persisted = savedRef.current;
    if (persisted.revision !== initial.revision && JSON.stringify(draftRef.current) !== JSON.stringify(persisted)) {
      // Keep the authored content and its CAS baseline while applying current rights immediately.
      const baseline = { ...persisted, capabilities: initial.capabilities };
      savedRef.current = baseline; setSaved(baseline);
      setDraft({ ...draftRef.current, capabilities: initial.capabilities });
      withholdResults(); setRevisionConflict(true); setError("The saved definition changed while this tab was away. Your unsaved draft remains here. Reload it or save a new question."); return;
    }
    if (persisted.revision !== initial.revision) {
      savedRef.current = initial; setSaved(initial); setDraft(initial); setProposalUndo(undefined); setRevisionConflict(false); withholdResults();
    } else {
      const local = draftRef.current;
      const restored = persisted.definitionState === "withheld" && local.definitionState === "withheld";
      const updated = initial;
      const next = { ...local, capabilities: initial.capabilities, uses: initial.uses,
        ...(initial.kind === "dashboard" ? { tiles: reconcileTileDisclosure(local.tiles, persisted.tiles, initial.tiles) } : {}),
        ...(restored ? { definition: initial.definition, definitionState: initial.definitionState } : {}) };
      savedRef.current = updated; setSaved(updated);
      if (JSON.stringify(local) !== JSON.stringify(next)) setDraft(next);
      if (JSON.stringify(local.tiles) !== JSON.stringify(next.tiles)) setEditingTile(undefined);
      if (restored) { setProposalUndo(undefined); setReplacing(false); }
    }
  }, [initial, review, denyProtectedContent, withholdResults, setDraft, replacing]);
  useEffect(() => {
    const held = () => Boolean(operation.current) || busyRef.current || JSON.stringify(draftRef.current) !== JSON.stringify(savedRef.current);
    const guard = (event: Event) => {
      if (!held()) return;
      const detail = (event as CustomEvent<StudioNavigationRequest>).detail;
      const editorPath = view === "edit" ? `${resourcePath(initial.kind, initial.id)}/edit` : resourcePath(initial.kind, initial.id || "new");
      if (detail?.fromPath && new URL(detail.fromPath, window.location.origin).pathname !== editorPath) return;
      if (detail?.intent === "probe" || operation.current || busyRef.current) { event.preventDefault(); return; }
      if (!window.confirm("Discard unsaved changes to this question or dashboard?")) event.preventDefault();
    };
    const unload = (event: BeforeUnloadEvent) => { if (held()) { event.preventDefault(); event.returnValue = ""; } };
    if (!guardEnabled) { notifyStudioNavigationGuardReleased(); return; }
    window.addEventListener(studioNavigationRequestedEvent, guard); window.addEventListener("beforeunload", unload);
    return () => { window.removeEventListener(studioNavigationRequestedEvent, guard); window.removeEventListener("beforeunload", unload); };
  }, [initial.id, initial.kind, view, kind, canEdit, guardEnabled]);
  useEffect(() => { if (!dirty && !busy) notifyStudioNavigationGuardReleased(); }, [dirty, busy]);

  useEffect(() => {
    if (accessUnavailable || review.state !== "available" || revisionConflict || !authoring || kind !== "question" || !draft.definition || draft.definitionState === "withheld") return;
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
        if (proposal.target.resourceType === "question") {
          const current = await native.get("question", proposal.target.resourceId, request.signal);
          if (!current.capabilities.canRead || current.definitionState === "withheld" || !current.definition) {
            setError("The current question definition is no longer available under your access."); denyProtectedContent();
            return { outcome: "refused", summary: "Current question disclosure is unavailable. Nothing was applied or saved." };
          }
          if (current.revision !== proposal.target.revision || !current.capabilities.canEdit) {
            withholdResults();
            return { outcome: "refused", summary: "Current question access or revision no longer allows this proposal. Reload before applying." };
          }
        }
        const validation = await native.validate(proposal.definition, request.signal);
        if (!request.current() || !matches()) return { outcome: "refused", summary: "The originating question changed or closed during validation. Nothing was applied or saved." };
        if (!validation.isValid) return { outcome: "refused", summary: validation.problems.map(problem => problem.message).join(" ") || "The proposal is invalid under current source access." };
        const previous = structuredClone(origin.definition);
        setDraft({ ...draftRef.current, definition: structuredClone(proposal.definition) });
        setProposalUndo({ definition: previous, appliedRevision: String(workingRevision.current) });
        setAnnouncement("Proposal applied to the question draft. Nothing was saved. Undo is available.");
        return { outcome: "applied", summary: "Applied to the native question draft only; nothing was saved.", workingRevision: String(workingRevision.current) };
      } catch (cause) {
        if (!request.current()) return { outcome: "refused", summary: "The originating editor closed. Nothing was applied or saved." };
        const summary = errorMessage(cause);
        if (accessLost(cause)) { setError(summary); denyProtectedContent(); } else withholdResults();
        return { outcome: "refused", summary };
      } finally { request.finish(); }
    });
  }, [authoring, draft, saved, generationId, kind, native, lifetime, accessUnavailable, review.state, revisionConflict, denyProtectedContent, withholdResults, setDraft]);
  const notify = (title: string) => api.notifications?.notify({ level: "success", title });
  const accepted = (resource: DashboardResource) => {
    // Every successful write establishes a new exact revision. Retire its previous preview,
    // drill binding and in-flight reads; ordinary Refresh will bind the newly saved revision.
    withholdResults(); resultRef.current = undefined; setResultWithheld(false); setDrillError(undefined);
    setRevisionConflict(false); setReplacing(false); savedRef.current = resource; setSaved(resource); setDraft(resource); setProposalUndo(undefined); setDialog(null);
  };
  const validateDraft = async (report = true) => {
    const definition = draftRef.current.definition;
    if (!definition) return;
    const fingerprint = JSON.stringify(definition);
    const request = lifetime.begin("validation"); setValidating(true);
    try {
      const validation = await native.validate(definition, request.signal);
      if (!request.current() || JSON.stringify(draftRef.current.definition) !== fingerprint) return;
      if (typeof validation.isValid !== "boolean" || !Array.isArray(validation.problems)) throw new Error("Question validation did not return a supported response. Retry validation.");
      setProblems(validation.problems); setValidationAttempt(value => value + 1);
      if (!validation.isValid) { withholdResults(); setError("Correct the marked question settings, then validate, preview or save again. Your draft remains here."); }
      else if (report) { setError(undefined); setAnnouncement("Question settings are valid under current source access. Nothing has been saved."); }
    } catch (cause) {
      if (request.current()) {
        if (accessLost(cause)) { setError(errorMessage(cause)); denyProtectedContent(); }
        else { withholdResults(); if (report) setError(errorMessage(cause)); }
      }
    }
    finally { if (request.current()) setValidating(false); request.finish(); }
  };
  const runMutation = async (work: (signal: AbortSignal) => Promise<DashboardResource>, title: string, navigateToResult = false) => {
    if (operation.current || !lifetime.capture()()) return;
    const controller = new AbortController(); operation.current = controller; setBusy(true); setError(undefined);
    try { const resource = await work(controller.signal); if (controller.signal.aborted) return; accepted(resource); operation.current = undefined; busyRef.current = false; setBusy(false); whenActive(() => { notify(title); if (!initial.id || navigateToResult) navigate(resourcePath(kind, resource.id)); }); }
    catch (cause) { if (!controller.signal.aborted) { setError(errorMessage(cause)); if (accessLost(cause)) denyProtectedContent(); else if ((cause as { status?: number })?.status === 422) await validateDraft(false); } }
    finally { if (operation.current === controller) { operation.current = undefined; busyRef.current = false; setBusy(false); } }
  };
  const saveCurrent = async () => {
    const revision = workingRevision.current;
    const request = lifetime.begin("save-review");
    try {
      // Uses belong to dashboards, so another editor can add a reference without changing this
      // question's revision. Read current authority and reuse before reviewing an update.
      if (kind === "question" && savedRef.current.id && definitionDirty) {
        const baseline = savedRef.current;
        const current = await native.get("question", baseline.id, request.signal);
        if (!request.current()) return;
        if (!current.capabilities.canRead) { denyProtectedContent(); return; }
        if (workingRevision.current !== revision) { setError("The draft changed while current access was checked. Review it before saving."); return; }
        if ((current.definitionState === "withheld" || !current.definition) && !replacing) {
          const redacted = { ...baseline, capabilities: current.capabilities, uses: current.uses, definition: undefined, definitionState: "withheld" as const };
          savedRef.current = redacted; setSaved(redacted);
          setDraft({ ...draftRef.current, capabilities: current.capabilities, uses: current.uses, definition: undefined, definitionState: "withheld" });
          setProposalUndo(undefined); withholdResults();
          setError("The current question definition is withheld. Its protected query has been cleared; permitted metadata changes remain available."); return;
        }
        const updated = { ...baseline, capabilities: current.capabilities, uses: current.uses };
        savedRef.current = updated; setSaved(updated);
        const local = { ...draftRef.current, capabilities: current.capabilities, uses: current.uses };
        draftRef.current = local; replaceDraft(local);
        if (current.revision !== baseline.revision) {
          withholdResults(); setRevisionConflict(true); setError("The saved question changed. Your unsaved draft and original revision remain here. Reload it or save the draft as a new question."); return;
        }
        if (!current.capabilities.canEdit) { withholdResults(); setError("Current access makes this question read-only. Save the draft as a new question if permitted."); return; }
        if (current.uses?.length) {
          const uses = current.uses.map(use => `${use.name} (${use.tileCount} ${use.tileCount === 1 ? "tile" : "tiles"})`).join(", ");
          const confirmed = await api.dialogs.confirm({ title: "Update reused question", message: `This question is currently used by dashboards you can access: ${uses}. Saving changes updates what their question references will run on refresh. Choose Save draft as new question to keep an independent private definition instead. Update this saved question?` });
          if (!confirmed || !request.current()) return;
        }
      }
      if (!request.current()) return;
      if (workingRevision.current !== revision) { setError("The draft changed during review. Review it before saving."); return; }
      await runMutation(signal => native.save(draftRef.current, createKey.current, signal), kind === "question" ? "Question saved" : "Dashboard saved", kind === "dashboard" && Boolean(initial.id));
    } catch (cause) {
      if (request.current()) { setError(errorMessage(cause)); if (accessLost(cause)) denyProtectedContent(); else withholdResults(); }
    } finally { request.finish(); }
  };
  const copySavedVersion = () => {
    const current = lifetime.capture();
    const source = savedRef.current;
    void api.dialogs.prompt({ title: "Save a private copy", message: kind === "dashboard" ? "The copy retains the reviewed saved question references. Source access is still required." : "Copies the exact saved definition, with no audience, history or results. Use Save draft as new question to keep unsaved question edits.", defaultValue: `${source.name} copy` }).then(name => {
      if (!name?.trim() || !current()) return;
      const fingerprint = `saved-copy:${source.id}:${source.revision}:${name}`;
      let operationKey = draftCreateAttempts.current.get(fingerprint);
      if (!operationKey) { operationKey = uuid(); draftCreateAttempts.current.set(fingerprint, operationKey); }
      const key = operationKey;
      void runMutation(signal => native.copy(source, name, key, signal), "Private copy saved", true);
    });
  };
  const replaceUnavailableQuery = async () => {
    if (!canEdit || busy || draftRef.current.definitionState !== "withheld") return;
    const current = lifetime.capture(); const revision = workingRevision.current;
    const confirmed = await api.dialogs.confirm({ title: "Replace unavailable query", message: "Start a blank replacement query in this local draft? The unavailable query and its values will remain undisclosed. Nothing is saved until you explicitly save the validated replacement; saving replaces the old query for this question." });
    if (!confirmed || !current() || workingRevision.current !== revision || !savedRef.current.capabilities.canEdit) return;
    withholdResults(); setReplacing(true); setProblems([]); setError(undefined);
    setDraft({ ...draftRef.current, definition: initialDefinition(), definitionState: "available" });
    setAnnouncement("Started a blank replacement query. Nothing has been saved. Choose an available dataset and validate the new definition.");
  };
  const deleteResource = async () => {
    if (operation.current) return;
    const controller = new AbortController(); operation.current = controller; setBusy(true); setError(undefined);
    try {
      await native.remove(saved, controller.signal); if (controller.signal.aborted) return;
      setDraft(savedRef.current); operation.current = undefined; busyRef.current = false; setBusy(false);
      whenActive(() => { notify(`${kind === "question" ? "Question" : "Dashboard"} deleted`); navigate(resourcePath(kind)); });
    } catch (cause) { if (!controller.signal.aborted) { setError(errorMessage(cause)); if (accessLost(cause)) denyProtectedContent(); } }
    finally { if (operation.current === controller) { operation.current = undefined; busyRef.current = false; setBusy(false); } }
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
    const submitted = draft.definition; const fingerprint = JSON.stringify(submitted); currentDefinition.current = fingerprint; setQuerying(true); setError(undefined);
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
      else { withholdResults(); if ((cause as { status?: number })?.status === 422) await validateDraft(false); }
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
    [tiles[from], tiles[to]] = [tiles[to], tiles[from]]; setDraft({ ...draft, tiles }); setAnnouncement(`Tile moved to position ${to + 1} of ${tiles.length}. Save changes to keep this change.`);
    requestAnimationFrame(() => {
      const tile = [...(pageRef.current?.querySelectorAll<HTMLElement>("[data-tile-id]") ?? [])].find(item => item.dataset.tileId === tileId);
      const same = tile?.querySelector<HTMLButtonElement>(`button[data-move="${delta}"]`);
      (same && !same.disabled ? same : tile?.querySelector<HTMLButtonElement>("button[data-move]:not(:disabled)"))?.focus();
    });
  };
  const openPicker = () => setDialog("questions");
  const datasetSelected = Boolean(draft.definition?.query.dataset.id);
  if (!accessUnavailable && review.state !== "available") return <StudioPage className="named-workspace"><StudioPageHeader headingLevel={2} title={review.state === "checking" ? "Checking current access" : "Current access unavailable"} /><p role="status">{review.message || (dashboardViewer ? "Revalidating this dashboard. Protected results remain withheld." : "Revalidating this retained editor. Its unsaved working state is preserved; protected previews remain withheld.")}</p>{review.state === "unavailable" ? <StudioButton onClick={onRetryAccess}>Retry current access</StudioButton> : null}</StudioPage>;
  if (accessUnavailable) return <StudioPage className="named-workspace"><StudioPageHeader headingLevel={2} title="Definition unavailable" /><StudioResultState tone="danger" title="Current access could not authorize this definition" description={error || "Protected definition and result content has been cleared. Reload to check your current access."} actions={<><StudioButton onClick={onReload}>Reload current access</StudioButton><StudioButton onClick={() => navigate(resourcePath(kind))}>Back to collection</StudioButton></>} /></StudioPage>;
  if (dashboardViewer) return <StudioPage className="named-workspace"><div className="named-editor-content" ref={pageRef}>
    <StudioPageHeader headingLevel={2} title={saved.name} description={saved.description || undefined} actions={<>
      {canEdit ? <StudioButton variant="primary" onClick={() => navigate(`${resourcePath("dashboard", saved.id)}/edit`)}>Edit dashboard</StudioButton> : null}
      <StudioButton disabled={busy} onClick={() => setRefresh(value => value + 1)}>Refresh all</StudioButton>
      <OverflowMenu ariaLabel="Dashboard actions" items={[
        ...(saved.capabilities.canShare ? [{ id: "share", label: "Share", disabled: busy, onSelect: () => setDialog("share") }] : []),
        ...(saved.id ? [{ id: "history", label: "History", disabled: busy, onSelect: () => setDialog("history") }] : []),
        ...(saved.capabilities.canCopy ? [{ id: "copy", label: "Make a copy", disabled: busy, onSelect: () => { setCopyDependenciesReady(false); setDialog("copy"); } }] : [])
      ]} />
    </>} />
    {error ? <div className="named-inline-error" role="alert"><p>{error}</p></div> : null}
    {!saved.tiles?.length ? <StudioResultState title="No questions on this dashboard" description="This dashboard does not have any saved questions yet." /> : <div className="named-tile-grid">{saved.tiles.map((tile, index) => <article className="named-tile" data-size={tile.size} data-tile-id={tile.id} key={tile.id}><header><h3>Question {index + 1}</h3></header>{tile.referenceState === "unavailable" || !tile.questionId ? <div className="named-tile-body" role="status">This question is unavailable under your current access.</div> : <QuestionTile api={native} dashboard={saved} tile={tile} refresh={refresh} draftLayout={false} position={index + 1} navigate={navigate} />}</article>)}</div>}
    {dialog === "copy" ? <NativeDialog title="Review dashboard copy dependencies" onClose={() => setDialog(null)}
      footer={<StudioDialogFooter confirmType="button" onCancel={() => setDialog(null)} confirmLabel="Continue with retained references"
        confirmDisabled={!copyDependenciesReady || busy} onConfirm={() => { setDialog(null); copySavedVersion(); }} />}>
      <DashboardDependencyReview api={native} resource={saved} mode="copy" onReadyChange={setCopyDependenciesReady} />
    </NativeDialog> : null}
    {dialog === "share" ? <ShareDialog api={native} resource={saved} onClose={() => setDialog(null)} onSaved={resource => { accepted(resource); notify("Audience saved"); }} /> : null}
    {dialog === "history" ? <HistoryDialog api={native} resource={saved} onAccessLost={historyAccessLost} onClose={() => setDialog(null)} onRestored={resource => { accepted(resource); notify("Definition restored as a new revision"); }} /> : null}
  </div></StudioPage>;
  return <StudioPage className="named-workspace"><div className="named-editor-content" ref={pageRef}><StudioPageHeader headingLevel={2} title={draft.id && kind === "dashboard" ? "Edit dashboard" : draft.id ? draft.name : `New ${kind}`} description={kind === "dashboard" ? draft.id ? `${draft.name}${dirty ? " · Unsaved changes" : ""}` : "Personal until you explicitly share it." : draft.id ? `Revision ${saved.revision}${dirty ? " · Unsaved changes" : ""}` : "Personal until you explicitly share it."} actions={<>
    {canEdit ? <StudioButton variant="primary" disabled={busy || !draft.name.trim() || kind === "question" && !draft.definition && draft.definitionState !== "withheld"} onClick={() => void saveCurrent()}>{busy ? "Saving…" : kind === "question" ? "Save question" : "Save"}</StudioButton> : null}
    {kind === "dashboard" ? <StudioButton disabled={busy} onClick={() => navigate(draft.id ? resourcePath("dashboard", draft.id) : resourcePath("dashboard"))}>Cancel</StudioButton> : null}
    {saved.id && kind === "question" ? <OverflowMenu ariaLabel="Question actions" items={[
      ...(saved.capabilities.canCopy ? [{ id: "copy", label: "Copy saved version", disabled: busy, onSelect: copySavedVersion }] : []),
      ...(dirty && draft.definition && draft.definitionState !== "withheld" && canCreateQuestion ? [{ id: "save-as", label: "Save draft as new question", disabled: busy, onSelect: () => void saveDraftAsNew() }] : []),
      ...(saved.capabilities.canShare ? [{ id: "share", label: "Share", disabled: busy || dirty, onSelect: () => setDialog("share") }] : []),
      { id: "history", label: "History", disabled: busy, onSelect: () => setDialog("history") },
      { id: "arrange", label: "Arrange on a dashboard", onSelect: () => navigate("/dashboards") }
    ]} /> : null}
    {kind === "question" ? <StudioButton onClick={() => navigate(resourcePath(kind))}>Back to questions</StudioButton> : null}
  </>} />
    {proposalUndo ? <div className="named-actions"><StudioButton disabled={busy || proposalUndo.appliedRevision !== String(workingRevision.current)} onClick={() => { if (proposalUndo.appliedRevision === String(workingRevision.current)) { setDraft({ ...draftRef.current, definition: structuredClone(proposalUndo.definition) }); setProposalUndo(undefined); setAnnouncement("Proposal undone. Nothing was saved."); } }}>Undo applied proposal</StudioButton><p>Proposal changes stay in this draft until Save. Undo is available until another edit or save.</p></div> : null}
    {!canEdit ? <p className="named-access-note">Read-only definition. Data uses your current access.</p> : null}
    {error ? <div className="named-inline-error" role="alert"><p>{error}</p>{saved.id ? <StudioButton onClick={() => { const current = lifetime.capture(); void api.dialogs.confirm({ message: "Reload the saved definition? Unsaved changes will be discarded." }).then(yes => { if (yes && current()) onReload(); }); }}>Reload saved definition</StudioButton> : null}<p>Your editable draft remains here. A successful local edit does not mean it was saved.</p></div> : null}
    <div className="named-definition-meta"><label>Name<input maxLength={200} value={draft.name} disabled={!canEdit || busy} onChange={event => setDraft({ ...draft, name: event.target.value })} /></label>{kind === "question" ? <details className="named-optional-metadata"><summary>Description (optional)</summary><label>Description<textarea maxLength={2000} value={draft.description ?? ""} disabled={!canEdit || busy} onChange={event => setDraft({ ...draft, description: event.target.value })} /></label></details> : <label>Description<textarea maxLength={2000} value={draft.description ?? ""} disabled={!canEdit || busy} onChange={event => setDraft({ ...draft, description: event.target.value })} /></label>}</div>
    {kind === "question" ? <>
      <details className="named-weaver-disclosure"><summary>Ask Weaver <span>(optional)</span></summary><DashboardAskWeaver controller={authoring} generationId={generationId} /></details>
      {saved.uses?.length ? <section aria-label="Question reuse"><h3>Dashboards you can access using this question</h3><ul>{saved.uses.map(use => <li key={use.id}><a href={resourcePath("dashboard", use.id)} onClick={event => { if (!event.metaKey && !event.ctrlKey && !event.shiftKey && event.button === 0) { event.preventDefault(); navigate(resourcePath("dashboard", use.id)); } }}>{use.name}</a> — {use.tileCount} {use.tileCount === 1 ? "tile" : "tiles"}</li>)}</ul><p>These references follow this question's saved changes when refreshed. This list includes only dashboards you can currently access. Save draft as new question creates an independent private definition; Copy saved version copies the persisted definition.</p></section> : null}
      {announcement ? <p role="status">{announcement}</p> : null}
      {sourceError ? <p role="alert">Dataset discovery unavailable: {sourceError}. Existing definition is preserved.</p> : null}
      {draft.definitionState === "withheld" ? <section><p role="status">Parts of this definition are withheld under your current access. Metadata-only save preserves its query. The unavailable fields and values remain undisclosed.</p>{canEdit ? <StudioButton disabled={busy} onClick={() => void replaceUnavailableQuery()}>Replace unavailable query</StudioButton> : null}</section> : draft.definition ? <div className="named-authoring" data-dataset-selected={datasetSelected ? "true" : "false"}><QueryControls definition={draft.definition} datasets={datasets} disabled={!canEdit || busy} problems={problems} validationAttempt={validationAttempt} onChange={definition => setDraft({ ...draft, definition })} />{datasetSelected ? <section className="named-preview" aria-label="Preview"><h3>Preview</h3><p>Results use your current access.</p><div className="named-actions"><StudioButton disabled={querying || !draft.definition.query.dataset.id} onClick={() => void preview()}>{querying ? "Querying…" : saved.id && !definitionDirty ? "Refresh results" : "Preview question"}</StudioButton><StudioButton disabled={validating || busy} onClick={() => void validateDraft()}>{validating ? "Validating…" : "Validate question"}</StudioButton>{querying ? <StudioButton onClick={cancelPreview}>Cancel query</StudioButton> : null}</div>{result ? <ResultView result={result} visualization={resultDefinition?.visualization ?? draft.definition.visualization} stale={stale} outdated={outdated} onDrill={resultCanDrill ? row => void drill(row) : undefined} /> : <p role="status">Preview to see source results and coverage.</p>}{resultWithheld ? <p role="status">Previous result values are withheld because current data access could not be established. Your draft remains available. Retry the preview or refresh.</p> : null}{result && !resultCanDrill ? <p className="named-help">Save this question before investigating its changed definition.</p> : null}</section> : null}</div> : null}
    </> : <>
      <div className="named-actions">{canEdit ? <StudioButton disabled={busy || (draft.tiles?.length ?? 0) >= 50} onClick={openPicker}>Add saved question</StudioButton> : null}</div>
      {!draft.tiles?.length ? <StudioResultState title="No questions on this dashboard" description="Add an accessible saved question. Each tile can use its own size and presentation." /> : <div className="named-tile-grid">{draft.tiles.map((tile, index) => <article className="named-tile" data-size={tile.size} data-tile-id={tile.id} key={tile.id}><header><h3>Question {index + 1}</h3>{canEdit ? <div className="named-actions"><StudioButton data-move="-1" disabled={busy || index === 0} aria-label={`Move question ${index + 1} earlier`} onClick={() => moveTile(tile.id, -1)}>Move earlier</StudioButton><StudioButton data-move="1" disabled={busy || index === draft.tiles!.length - 1} aria-label={`Move question ${index + 1} later`} onClick={() => moveTile(tile.id, 1)}>Move later</StudioButton><label>Size for question {index + 1}<select disabled={busy} value={tile.size} onChange={event => setDraft({ ...draft, tiles: draft.tiles?.map(item => item.id === tile.id ? { ...item, size: event.target.value as DashboardTile["size"] } : item) })}>{["small", "medium", "wide", "full"].map(item => <option key={item}>{item}</option>)}</select></label>{tile.referenceState !== "unavailable" && tile.questionId ? <StudioButton disabled={busy} onClick={() => setEditingTile(tile)}>Settings for question {index + 1}</StudioButton> : null}<StudioButton disabled={busy} onClick={() => setDraft({ ...draft, tiles: draft.tiles?.filter(item => item.id !== tile.id) })}>Remove tile {index + 1}</StudioButton></div> : null}</header>{tile.referenceState === "unavailable" || !tile.questionId ? <div className="named-tile-body" role="status">This question is unavailable under your current access. Its reference is preserved during layout saves.</div> : <QuestionTileEditor api={native} tile={tile} position={index + 1} navigate={navigate} />}</article>)}</div>}
      <p aria-live="polite" className="named-announcement">{announcement}</p>
    </>}
    {saved.id && saved.capabilities.canDelete ? <div className="named-danger-actions"><StudioButton disabled={busy} onClick={() => { const current = lifetime.capture(); void api.dialogs.confirm({ title: `Delete ${kind}`, message: kind === "question" ? "Delete this question? Dashboard references will become unavailable. Underlying records will remain." : "Delete this dashboard? Its saved questions will remain.", tone: "danger" }).then(yes => { if (yes && current()) void deleteResource(); }); }}>Delete {kind}</StudioButton></div> : null}
    {editingTile ? <TileOptionsDialog api={native} tile={editingTile} onClose={() => setEditingTile(undefined)} onApply={tile => { setDraft({ ...draft, tiles: draft.tiles?.map(item => item.id === tile.id ? tile : item) }); setEditingTile(undefined); }} /> : null}
    {dialog === "copy" ? <NativeDialog title="Review dashboard copy dependencies" onClose={() => setDialog(null)}
      footer={<StudioDialogFooter confirmType="button" onCancel={() => setDialog(null)} confirmLabel="Continue with retained references"
        confirmDisabled={!copyDependenciesReady || busy} onConfirm={() => { setDialog(null); copySavedVersion(); }} />}>
      <DashboardDependencyReview api={native} resource={saved} mode="copy" onReadyChange={setCopyDependenciesReady} />
    </NativeDialog> : null}
    {dialog === "share" ? <ShareDialog api={native} resource={saved} onClose={() => setDialog(null)} onSaved={resource => { accepted(resource); notify("Audience saved"); }} /> : null}
    {dialog === "history" ? <HistoryDialog api={native} resource={saved} hasUnsavedChanges={dirty} onAccessLost={historyAccessLost} onClose={() => setDialog(null)} onRestored={resource => { accepted(resource); notify("Definition restored as a new revision"); }} /> : null}
    {dialog === "questions" ? <QuestionPickerDialog api={native} onClose={() => setDialog(null)} onPick={question => { setDraft({ ...draftRef.current, tiles: [...(draftRef.current.tiles ?? []), { id: uuid(), questionId: question.id, size: "medium", referenceState: "available" }] }); setDialog(null); }} onCreate={() => navigate("/questions/new")} /> : null}
    {drilling || drillResult || drillError ? <NativeDialog title="Matching source records" onClose={closeDrill}>{drilling ? <p role="status">Loading authorized source records…</p> : drillError ? <p role="alert">{drillError}</p> : drillResult ? <><ResultView result={drillResult} visualization={{ kind: "table", y: [] }} />{drillResult.nextCursor && drillSource ? <StudioButton onClick={() => void drill(drillSource, drillResult.nextCursor)}>Next matching records</StudioButton> : null}</> : null}</NativeDialog> : null}
  </div></StudioPage>;
}

type QuestionTileEditorProps = { api: DashboardApi; tile: DashboardTile; position?: number; navigate(path: string): void };
function QuestionTileEditor({ api, tile, position, navigate }: QuestionTileEditorProps) {
  const [question, setQuestion] = useState<DashboardResource>();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(false);
  useEffect(() => {
    const controller = new AbortController(); setLoading(true); setError(false); setQuestion(undefined);
    void api.get("question", tile.questionId!, controller.signal).then(resource => {
      if (!controller.signal.aborted && resource.capabilities.canRead) setQuestion(resource);
      else if (!controller.signal.aborted) setError(true);
    }).catch(() => { if (!controller.signal.aborted) setError(true); }).finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [api, tile.questionId]);
  if (loading) return <div className="named-tile-body" role="status">Checking access to question {position ?? ""}…</div>;
  if (error || !question) return <div className="named-tile-body" role="status">This question is unavailable under your current access.</div>;
  return <div className="named-tile-body"><a href={resourcePath("question", question.id)} onClick={event => { if (!event.metaKey && !event.ctrlKey && !event.shiftKey && event.button === 0) { event.preventDefault(); navigate(resourcePath("question", question.id)); } }}>{question.name}</a><p>Results are shown on the dashboard view.</p></div>;
}

type QuestionTileProps = { api: DashboardApi; dashboard: DashboardResource; tile: DashboardTile; refresh: number; draftLayout: boolean; position?: number; navigate(path: string): void };
export function QuestionTile(props: QuestionTileProps) {
  const { dashboard, tile, refresh, draftLayout } = props;
  // A new binding owns fresh protected state before paint, including retained tile IDs.
  const binding = JSON.stringify([dashboard.id, dashboard.revision, tile.id, tile.questionId,
    tile.presentation, tile.parameters, refresh, draftLayout]);
  return <QuestionTileContent key={binding} {...props} />;
}

function QuestionTileContent({ api, dashboard, tile, draftLayout, position, navigate }: QuestionTileProps) {
  const [question, setQuestion] = useState<DashboardResource>();
  const [result, setResult] = useState<QueryResult>();
  const [error, setError] = useState<string>();
  const [loading, setLoading] = useState(false);
  const [withheld, setWithheld] = useState(false);
  const [drilled, setDrilled] = useState<QueryResult>();
  const [drillError, setDrillError] = useState<string>();
  const [drilling, setDrilling] = useState(false);
  const [drillSource, setDrillSource] = useState<QueryRow>();
  const [retry, setRetry] = useState(0);
  const refreshRequest = useRef<AbortController | undefined>(undefined);
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
    refreshRequest.current = controller;
    drillRequest.current?.abort(); setDrilled(undefined); setDrillError(undefined); setDrilling(false);
    void (async () => { try { const resource = await api.get("question", tile.questionId!, controller.signal); if (controller.signal.aborted) return; setQuestion(resource); if (!dashboard.id || draftLayout) return; const response = await api.tile(dashboard, tile.id, resource.revision, controller.signal); if (!controller.signal.aborted) setResult(response); } catch (cause) { if (!controller.signal.aborted) { setError(errorMessage(cause)); setResult(undefined); setDrilled(undefined); setWithheld(true); if (accessLost(cause)) setQuestion(undefined); } } finally { if (!controller.signal.aborted) setLoading(false); } })();
    return () => controller.abort();
  }, [api, dashboard, tile.id, tile.questionId, draftLayout, retry]);
  return <div className="named-tile-body">{question ? <a href={resourcePath("question", question.id)} onClick={event => { event.preventDefault(); navigate(resourcePath("question", question.id)); }}>{question.name}</a> : null}{loading ? <><p role="status">Refreshing question…</p><StudioButton aria-label={position ? `Cancel refresh for question ${position}` : `Cancel refresh for ${question?.name ?? "this question"}`} onClick={() => { refreshRequest.current?.abort(); drillRequest.current?.abort(); setLoading(false); setDrilling(false); setResult(undefined); setDrilled(undefined); setWithheld(true); setError("Refresh cancelled. No saved definition was changed."); }}>Cancel refresh</StudioButton></> : null}{error ? <p role="alert">{error}</p> : null}{withheld ? <p role="status">Result values are withheld because current data access could not be established. Refresh the dashboard to retry.</p> : null}{!loading && (error || withheld) && !draftLayout ? <StudioButton aria-label={position ? `Retry refresh for question ${position}` : `Retry refresh for ${question?.name ?? "this question"}`} onClick={() => setRetry(value => value + 1)}>Retry refresh</StudioButton> : null}{draftLayout ? <p role="status">Save this layout to execute its exact tile bindings.</p> : result ? <ResultView result={result} visualization={tile.presentation ?? question?.definition?.visualization ?? { kind: "table", y: [] }} compactProvenance onDrill={row => void drill(row)} /> : !loading && !error ? <p role="status">Results have not been loaded.</p> : null}{drilling || drilled || drillError ? <NativeDialog title="Matching source records" onClose={() => { if (drilling) { setResult(undefined); setWithheld(true); setError("Investigation cancelled. Refresh the dashboard to retry."); } drillRequest.current?.abort(); setDrilling(false); setDrillSource(undefined); setDrilled(undefined); setDrillError(undefined); }}>{drilling ? <p role="status">Loading authorized source records…</p> : drillError ? <p role="alert">{drillError}</p> : drilled ? <><ResultView result={drilled} visualization={{ kind: "table", y: [] }} />{drilled.nextCursor && drillSource ? <StudioButton onClick={() => void drill(drillSource, drilled.nextCursor)}>Next matching records</StudioButton> : null}</> : null}</NativeDialog> : null}</div>;
}
