import { useEffect, useId, useRef, useState } from "react";
import { StudioButton, StudioDialogFooter } from "@elsa-workflows/studio-ui";
import { ArrowDownAZ, Bookmark, Save, SlidersHorizontal, Star, Tags } from "lucide-react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import {
  deleteWorkflowView, getWorkflowTag, listWorkflowTags, listWorkflowViews, saveWorkflowView,
  type WorkflowLibraryView, type WorkflowOrganizationPermissions, type WorkflowTag,
  type WorkflowTagFilter, type workflowOrganizationSupport
} from "../api/workflowOrganization";
import {
  workflowBrowseLocationFromQuery, workflowBrowseQuery,
  workflowFolderSelectionKey, workflowLibraryFilterKey,
  type WorkflowDefinitionBrowseLocation
} from "./workflowDefinitionBrowseLocation";
import { StudioLink } from "../StudioLink";
import { useDialogFocus } from "./useDialogFocus";
import "./workflowOrganization.css";

type Support = Awaited<ReturnType<typeof workflowOrganizationSupport>>;

export function WorkflowLibraryControls({ context, support, location, onChange, onCatalog, onPermissions, onViews, refreshKey = 0, visibleTagIds = [] }: {
  context: StudioEndpointContext;
  support: Support;
  location: WorkflowDefinitionBrowseLocation;
  onChange(next: WorkflowDefinitionBrowseLocation): void;
  onCatalog(tags: WorkflowTag[]): void;
  onPermissions(permissions: WorkflowOrganizationPermissions): void;
  /** The visible library views once read, so the page can disclose the applied view's identity, owner kind and revision. */
  onViews?(views: WorkflowLibraryView[]): void;
  refreshKey?: number;
  visibleTagIds?: readonly string[];
}) {
  const [tags, setTags] = useState<WorkflowTag[]>([]);
  const [views, setViews] = useState<WorkflowLibraryView[]>([]);
  // Null until the view list has been read: the page then does not disclose a view it cannot yet identify.
  const [loadedViews, setLoadedViews] = useState<WorkflowLibraryView[] | null>(null);
  const [permissions, setPermissions] = useState<WorkflowOrganizationPermissions>({});
  const [catalogCursor, setCatalogCursor] = useState<string | null>(null);
  const [catalogSearch, setCatalogSearch] = useState("");
  const [catalogTerm, setCatalogTerm] = useState("");
  const [catalogLoading, setCatalogLoading] = useState(support.tags);
  const [error, setError] = useState("");
  const [filtersOpen, setFiltersOpen] = useState(Boolean(location.tagFilters?.length || location.publicationState));
  const [viewDialogOpen, setViewDialogOpen] = useState(false);
  const [refresh, setRefresh] = useState(0);
  const generationRef = useRef(0);
  const permissionsReported = useRef(false);
  const filtersId = useId();

  useEffect(() => {
    const generation = ++generationRef.current;
    setError("");
    setCatalogLoading(support.tags);
    if (support.tags) void listWorkflowTags(context, catalogTerm).then(page => {
      if (generation !== generationRef.current) return;
      setTags(current => catalogTerm ? mergeTags(current, page.items) : page.items);
      setCatalogCursor(page.nextContinuationToken ?? null);
      setPermissions(current => ({ ...current, ...page.permissions }));
      permissionsReported.current = true;
      onPermissions(page.permissions ?? {});
    }).catch(caught => {
      if (generation !== generationRef.current) return;
      setError(message(caught, "Couldn't read workflow tags."));
      // A first read that fails still settles the permissions: the page then offers none of the gated actions.
      if (!permissionsReported.current) { permissionsReported.current = true; onPermissions({}); }
    }).finally(() => { if (generation === generationRef.current) setCatalogLoading(false); });
    if (support.views) void listWorkflowViews(context).then(response => {
      if (generation !== generationRef.current) return;
      setViews(response.items);
      setLoadedViews(response.items);
      setPermissions(current => ({ ...current, ...response.permissions }));
    }).catch(caught => {
      if (generation !== generationRef.current) return;
      setError(message(caught, "Couldn't read saved views."));
      // A first read that fails still settles the view list as empty: the page then discloses no view it cannot identify,
      // rather than never settling.
      setLoadedViews(current => current ?? []);
    });
    return () => { generationRef.current += 1; };
  }, [catalogTerm, context, onPermissions, refresh, refreshKey, support.tags, support.views]);

  useEffect(() => { onCatalog(tags); }, [onCatalog, tags]);
  useEffect(() => { if (loadedViews) onViews?.(loadedViews); }, [onViews, loadedViews]);
  const missingTagIds = !catalogLoading ? JSON.stringify([...new Set([...(location.tagFilters ?? []).map(filter => filter.tagId), ...visibleTagIds])].filter(id => !tags.some(tag => tag.id === id))) : "[]";
  useEffect(() => {
    const ids = JSON.parse(missingTagIds) as string[];
    if (!ids.length || !support.tags) return;
    let active = true;
    void (async () => {
      const found: WorkflowTag[] = [];
      // Resolve only references in this page or its filters; catalog continuation stays user-driven.
      for (let start = 0; active && start < ids.length; start += 8) {
        const results = await Promise.allSettled(ids.slice(start, start + 8).map(id => getWorkflowTag(context, id)));
        found.push(...results.flatMap(result => result.status === "fulfilled" ? [result.value] : []));
      }
      if (active && found.length) setTags(current => mergeTags(current, found));
    })();
    return () => { active = false; };
  }, [context, missingTagIds, support.tags]);

  const loadMoreTags = async () => {
    if (!catalogCursor || catalogLoading) return;
    const generation = generationRef.current;
    setCatalogLoading(true);
    try {
      const page = await listWorkflowTags(context, catalogTerm, catalogCursor);
      if (generation !== generationRef.current) return;
      setTags(current => mergeTags(current, page.items));
      setCatalogCursor(page.nextContinuationToken ?? null);
    } catch (caught) { if (generation === generationRef.current) setError(message(caught, "Couldn't load more tags.")); }
    finally { if (generation === generationRef.current) setCatalogLoading(false); }
  };
  const change = (patch: Partial<WorkflowDefinitionBrowseLocation>) => onChange({ ...location, ...patch });
  const updateFilter = (index: number, patch: Partial<WorkflowTagFilter>) => {
    change({ invalidTagFilters: undefined, tagFilters: (location.tagFilters ?? []).map((filter, position) => position === index ? { ...filter, ...patch } : filter) });
  };
  const selectedView = views.find(view => view.id === location.viewId);
  const unknownView = Boolean(support.views && location.viewId && !selectedView);
  const savedLocation = selectedView ? workflowBrowseLocationFromQuery(selectedView.query, selectedView.id, selectedView.columns) : undefined;
  const modifiedView = savedLocation && (workflowLibraryFilterKey(savedLocation) !== workflowLibraryFilterKey(location)
    || workflowFolderSelectionKey(savedLocation.folderSelection) !== workflowFolderSelectionKey(location.folderSelection)
    || savedLocation.search !== location.search || savedLocation.listState !== location.listState
    || JSON.stringify(savedLocation.columns ?? []) !== JSON.stringify(location.columns ?? []));
  const activeCount = (location.tagFilters?.length ?? 0) + (location.publicationState ? 1 : 0);
  const viewsSaved = () => { setViewDialogOpen(false); setRefresh(value => value + 1); };

  return <section className="wf-library-controls" aria-label={support.tags || support.views || support.folders || support.favorites ? "Workflow library organization" : "Workflow library filters"}>
    <div className="wf-library-control-row wf-library-toolbar-row">
      <div className="wf-library-control-group" role="group" aria-label="Filter and sort workflows">
      {support.favorites && support.queryOptions ? <StudioButton className="wf-library-favorites" aria-label="Favorites" title="Show favorite workflows" aria-pressed={location.favoritesOnly === true} onClick={() => change({ favoritesOnly: !location.favoritesOnly })}><Star size={16} aria-hidden="true" fill={location.favoritesOnly ? "currentColor" : "none"} /></StudioButton> : null}
      {support.tags || support.folders || support.queryOptions ? <StudioButton aria-expanded={filtersOpen} aria-controls={filtersId} onClick={() => setFiltersOpen(value => !value)}><SlidersHorizontal size={15} aria-hidden="true" />{support.queryOptions ? `Filters${activeCount ? ` (${activeCount})` : ""}` : "Columns"}</StudioButton> : null}
      {support.folders && support.queryOptions && typeof location.folderSelection === "object" ? <label className="wf-library-check"><input type="checkbox" checked={location.includeDescendants ?? false} onChange={event => change({ includeDescendants: event.target.checked })} /> Include subfolders</label> : null}
      {support.queryOptions ? <label className="wf-library-toolbar-field"><ArrowDownAZ size={15} aria-hidden="true" /><span>Sort</span><select aria-label="Sort workflows" value={location.sort ?? "name"} onChange={event => change({ sort: event.target.value as WorkflowDefinitionBrowseLocation["sort"] })}>
        <option value="name">Name A–Z</option><option value="name-desc">Name Z–A</option><option value="modified">Recently modified</option>
      </select></label> : null}
      </div>
      <div className="wf-library-control-group wf-library-view-group" role="group" aria-label="Workflow views and tags">
      {support.views ? <>
        <label className="wf-library-toolbar-field"><Bookmark size={15} aria-hidden="true" /><span>View</span><select aria-label="Saved workflow view" value={location.viewId ?? ""} onChange={event => {
          const view = views.find(item => item.id === event.target.value);
          if (view) onChange(workflowBrowseLocationFromQuery(view.query, view.id, view.columns));
          else change({ viewId: undefined });
        }}><option value="">Unsaved view</option>{unknownView ? <option value={location.viewId}>Unavailable saved view</option> : null}
          {views.map(view => <option key={view.id} value={view.id}>{view.name}{view.shared ? " · Shared" : " · Private"}</option>)}
        </select></label>
        <StudioButton onClick={() => setViewDialogOpen(true)}><Save size={15} aria-hidden="true" />Save view</StudioButton>
      </> : null}
      {permissions.canManageTags === true ? <StudioLink className="studio-button" data-variant="default" href="/workflows/tags"><Tags size={15} aria-hidden="true" />Manage tags</StudioLink> : null}
      </div>
    </div>
    {unknownView ? <p role="alert">This saved view is unavailable. Its URL filters are retained; choose an available view or save a new one.</p> : null}
    {modifiedView ? <p className="wf-library-help">The current filters differ from the saved view. Save to update it or choose the view again to restore its filters.</p> : null}
    {support.folders && location.includeDescendants && typeof location.folderSelection !== "object" ? <p role="alert">The subfolder filter requires a selected folder. <button type="button" onClick={() => change({ includeDescendants: false })}>Clear subfolder filter</button></p> : null}
    {error ? <p role="alert">{error} <button type="button" onClick={() => setRefresh(value => value + 1)}>Retry organization</button></p> : null}

    {filtersOpen && (support.tags || support.folders || support.queryOptions) ? <div className="wf-library-filter-panel" id={filtersId}>
      {support.queryOptions ? <label>Publication <select aria-label="Publication filter" value={location.publicationState ?? ""} onChange={event => change({ publicationState: event.target.value || undefined })}>
        <option value="">Any publication state</option><option value="published">Published</option>{location.publicationState && location.publicationState !== "published" ? <option value={location.publicationState}>Unsupported state: {location.publicationState}</option> : null}
      </select></label> : null}
      {support.tags && support.queryOptions ? <><div className="wf-library-control-row"><label>Find tag <input aria-label="Find tags for filters" value={catalogSearch} onChange={event => setCatalogSearch(event.target.value)} /></label><button type="button" disabled={catalogLoading} onClick={() => setCatalogTerm(catalogSearch)}>Find tags</button>{catalogCursor ? <button type="button" disabled={catalogLoading} onClick={() => void loadMoreTags()}>Load more tags</button> : null}</div>
      {(location.tagFilters ?? []).map((filter, index) => {
        const tag = tags.find(item => item.id === filter.tagId);
        return <div className="wf-library-filter-clause" key={`${index}-${filter.tagId}`}>
          <label>Tag <select aria-label={`Tag filter ${index + 1}`} value={filter.tagId} onChange={event => updateFilter(index, { tagId: event.target.value, operator: "exists", valueIds: [] })}>
            {!tag ? <option value={filter.tagId}>{filter.tagId} (unavailable)</option> : null}
            {tags.map(item => <option key={item.id} value={item.id}>{item.name}{item.deprecated ? " (retired)" : ""}</option>)}
          </select></label>
          <label>Condition <select aria-label={`Condition for tag filter ${index + 1}`} value={filter.operator} onChange={event => updateFilter(index, { operator: event.target.value as WorkflowTagFilter["operator"], valueIds: [] })}>
            <option value="exists">Has tag</option><option value="missing">Missing tag</option>
            {tag?.mode === "controlled" ? <><option value="any">Any selected value</option>{tag.cardinality === "multiple" ? <option value="all">Every selected value</option> : null}<option value="none">None of these values</option></> : null}
          </select></label>
          {["any", "all", "none"].includes(filter.operator) ? <fieldset className="wf-library-values"><legend>Values</legend>
            {(tag?.values ?? []).map(value => <label key={value.id}><input type="checkbox" checked={filter.valueIds?.includes(value.id) ?? false} onChange={event => updateFilter(index, { valueIds: event.target.checked ? [...(filter.valueIds ?? []), value.id] : filter.valueIds?.filter(id => id !== value.id) })} />{value.name}{value.deprecated ? " (retired)" : ""}</label>)}
            {filter.valueIds?.filter(id => !tag?.values.some(value => value.id === id)).map(id => <span key={id} role="alert">Unavailable value: {id}</span>)}
          </fieldset> : null}
          {!tag && !catalogLoading ? <span role="alert">Load this tag or repair this filter; it remains applied.</span> : null}
          <button type="button" aria-label={`Remove tag filter ${index + 1}`} onClick={() => change({ tagFilters: location.tagFilters?.filter((_, position) => position !== index) })}>Remove</button>
        </div>;
      })}
      <div className="wf-library-control-row"><button type="button" disabled={!tags.length || (location.tagFilters?.length ?? 0) >= 16} onClick={() => change({ tagFilters: [...(location.tagFilters ?? []), { tagId: tags[0].id, operator: "exists" }] })}>Add tag filter</button>
        {activeCount ? <button type="button" onClick={() => change({ tagFilters: [], publicationState: undefined })}>Clear filters</button> : null}
      </div>
      <p className="wf-library-help">All tag conditions must match. Selected alternatives within a condition match any value, unless “Every selected value” is chosen.</p>
      </> : null}
      {support.tags || support.folders ? <fieldset className="wf-library-values"><legend>Optional columns</legend>{[...(support.tags ? ["tags"] : []), ...(support.folders ? ["location"] : [])].map(column => <label key={column}><input type="checkbox" checked={location.columns?.includes(column) ?? false} onChange={event => change({ columns: event.target.checked ? [...(location.columns ?? []), column] : location.columns?.filter(item => item !== column) })} />{column === "tags" ? "Tags" : "Location"}</label>)}</fieldset> : null}
    </div> : null}
    {viewDialogOpen ? <WorkflowViewDialog context={context} location={location} existing={selectedView} canShare={permissions.canShareViews === true} onClose={() => setViewDialogOpen(false)} onSaved={viewsSaved} onDeleted={() => { change({ viewId: undefined }); viewsSaved(); }} /> : null}
  </section>;
}

function WorkflowViewDialog({ context, location, existing, canShare, onClose, onSaved, onDeleted }: {
  context: StudioEndpointContext; location: WorkflowDefinitionBrowseLocation; existing?: WorkflowLibraryView;
  canShare: boolean; onClose(): void; onSaved(): void; onDeleted(): void;
}) {
  const ref = useRef<HTMLElement>(null);
  const titleId = useId();
  const [name, setName] = useState(existing?.name ?? "");
  const [shared, setShared] = useState(canShare && (existing?.shared ?? false));
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  useDialogFocus(ref, busy ? null : onClose);
  const save = async (replace: boolean) => {
    setBusy(true); setError("");
    try { await saveWorkflowView(context, { name: name.trim(), query: workflowBrowseQuery(location), shared, columns: location.columns ?? [], ...(replace ? { revision: existing?.revision } : {}) }, replace ? existing?.id : undefined); onSaved(); }
    catch (caught) { setError(message(caught, "Couldn't save this view. Reload after a conflict and review your changes.")); }
    finally { setBusy(false); }
  };
  const remove = async () => {
    if (!existing) return;
    setBusy(true); setError("");
    try { await deleteWorkflowView(context, existing); onDeleted(); }
    catch (caught) { setError(message(caught, "Couldn't delete this view.")); }
    finally { setBusy(false); }
  };
  return <div className="wf-dialog-backdrop" role="presentation"><section ref={ref} className="wf-dialog wf-organization-dialog" role="dialog" aria-modal="true" aria-labelledby={titleId} tabIndex={-1}>
    <form onSubmit={event => { event.preventDefault(); void save(false); }}>
      <h3 id={titleId}>Save workflow view</h3>
      <label>View name<input aria-label="View name" required maxLength={200} value={name} disabled={busy} onChange={event => setName(event.target.value)} /></label>
      <label className="wf-library-check"><input type="checkbox" checked={shared} disabled={busy || !canShare} onChange={event => setShared(event.target.checked)} /> Share within this tenant</label>
      {!canShare ? <p className="wf-library-help">You can save private views. Sharing requires additional permission.</p> : null}
      <p className="wf-library-help">Saves the current folder, filters, sort and optional columns. Each reader sees only workflows they can access.</p>
      {error ? <p role="alert">{error}</p> : null}
      <StudioDialogFooter
        className="wf-dialog-actions"
        extra={<>
          {error ? <StudioButton disabled={busy} onClick={onSaved}>Reload view list</StudioButton> : null}
          {existing?.canDelete ? <StudioButton disabled={busy} onClick={() => void remove()}>Delete saved view</StudioButton> : null}
          {existing?.canEdit ? <StudioButton disabled={busy || !name.trim() || (shared && !canShare)} onClick={() => void save(true)}>Update saved view</StudioButton> : null}
        </>}
        onCancel={onClose}
        cancelDisabled={busy}
        confirmLabel={busy ? "Saving…" : "Save as new view"}
        confirmDisabled={busy || !name.trim() || (shared && !canShare)}
      />
    </form>
  </section></div>;
}

function mergeTags(current: WorkflowTag[], next: WorkflowTag[]) {
  return [...new Map([...current, ...next].map(tag => [tag.id, tag])).values()];
}
function message(caught: unknown, fallback: string) { return caught instanceof Error ? `${fallback} ${caught.message}` : fallback; }
