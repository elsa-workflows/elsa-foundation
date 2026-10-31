import { lazy, Suspense, useEffect, useId, useRef, useState } from "react";
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
import { useDialogFocus } from "./useDialogFocus";
import "./workflowOrganization.css";

const WorkflowTagCatalogDialog = lazy(() => import("./WorkflowTagCatalogDialog").then(module => ({ default: module.WorkflowTagCatalogDialog })));
type Support = Awaited<ReturnType<typeof workflowOrganizationSupport>>;

export function WorkflowLibraryControls({ context, support, location, onChange, onCatalog, onPermissions }: {
  context: StudioEndpointContext;
  support: Support;
  location: WorkflowDefinitionBrowseLocation;
  onChange(next: WorkflowDefinitionBrowseLocation): void;
  onCatalog(tags: WorkflowTag[]): void;
  onPermissions(permissions: WorkflowOrganizationPermissions): void;
}) {
  const [tags, setTags] = useState<WorkflowTag[]>([]);
  const [views, setViews] = useState<WorkflowLibraryView[]>([]);
  const [permissions, setPermissions] = useState<WorkflowOrganizationPermissions>({});
  const [catalogCursor, setCatalogCursor] = useState<string | null>(null);
  const [catalogSearch, setCatalogSearch] = useState("");
  const [catalogTerm, setCatalogTerm] = useState("");
  const [catalogLoading, setCatalogLoading] = useState(false);
  const [error, setError] = useState("");
  const [filtersOpen, setFiltersOpen] = useState(Boolean(location.tagFilters?.length || location.publicationState));
  const [catalogOpen, setCatalogOpen] = useState(false);
  const [viewDialogOpen, setViewDialogOpen] = useState(false);
  const [refresh, setRefresh] = useState(0);
  const generationRef = useRef(0);
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
      onPermissions(page.permissions ?? {});
    }).catch(caught => {
      if (generation === generationRef.current) setError(message(caught, "Couldn't read workflow tags."));
    }).finally(() => { if (generation === generationRef.current) setCatalogLoading(false); });
    if (support.views) void listWorkflowViews(context).then(response => {
      if (generation !== generationRef.current) return;
      setViews(response.items);
      setPermissions(current => ({ ...current, ...response.permissions }));
    }).catch(caught => {
      if (generation === generationRef.current) setError(message(caught, "Couldn't read saved views."));
    });
    return () => { generationRef.current += 1; };
  }, [catalogTerm, context, onPermissions, refresh, support.tags, support.views]);

  useEffect(() => { onCatalog(tags); }, [onCatalog, tags]);
  const missingFilterTags = !catalogLoading ? JSON.stringify([...new Set((location.tagFilters ?? []).map(filter => filter.tagId))].filter(id => !tags.some(tag => tag.id === id))) : "[]";
  useEffect(() => {
    const ids = JSON.parse(missingFilterTags) as string[];
    if (!ids.length || !support.tags) return;
    let active = true;
    void Promise.allSettled(ids.map(id => getWorkflowTag(context, id))).then(results => {
      if (!active) return;
      const found = results.flatMap(result => result.status === "fulfilled" ? [result.value] : []);
      if (found.length) setTags(current => mergeTags(current, found));
    });
    return () => { active = false; };
  }, [context, missingFilterTags, support.tags]);

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
  const unknownView = Boolean(location.viewId && !selectedView);
  const savedLocation = selectedView ? workflowBrowseLocationFromQuery(selectedView.query, selectedView.id, selectedView.columns) : undefined;
  const modifiedView = savedLocation && (workflowLibraryFilterKey(savedLocation) !== workflowLibraryFilterKey(location)
    || workflowFolderSelectionKey(savedLocation.folderSelection) !== workflowFolderSelectionKey(location.folderSelection)
    || savedLocation.search !== location.search || savedLocation.listState !== location.listState
    || JSON.stringify(savedLocation.columns ?? []) !== JSON.stringify(location.columns ?? []));
  const activeCount = (location.tagFilters?.length ?? 0) + (location.publicationState ? 1 : 0);
  const viewsSaved = () => { setViewDialogOpen(false); setRefresh(value => value + 1); };

  return <section className="wf-library-controls" aria-label="Workflow library organization">
    <div className="wf-library-control-row">
      {support.favorites ? <button type="button" aria-pressed={location.favoritesOnly === true} onClick={() => change({ favoritesOnly: !location.favoritesOnly })}>Favorites</button> : null}
      {typeof location.folderSelection === "object" ? <label className="wf-library-check"><input type="checkbox" checked={location.includeDescendants ?? false} onChange={event => change({ includeDescendants: event.target.checked })} /> Include subfolders</label> : null}
      <label>Sort <select aria-label="Sort workflows" value={location.sort ?? "name"} onChange={event => change({ sort: event.target.value as WorkflowDefinitionBrowseLocation["sort"] })}>
        <option value="name">Name A–Z</option><option value="name-desc">Name Z–A</option><option value="modified">Recently modified</option>
      </select></label>
      {support.tags ? <button type="button" aria-expanded={filtersOpen} aria-controls={filtersId} onClick={() => setFiltersOpen(value => !value)}>Filters{activeCount ? ` (${activeCount})` : ""}</button> : null}
      {support.views ? <>
        <label>View <select aria-label="Saved workflow view" value={location.viewId ?? ""} onChange={event => {
          const view = views.find(item => item.id === event.target.value);
          if (view) onChange(workflowBrowseLocationFromQuery(view.query, view.id, view.columns));
          else change({ viewId: undefined });
        }}><option value="">Current filters</option>{unknownView ? <option value={location.viewId}>Unavailable saved view</option> : null}
          {views.map(view => <option key={view.id} value={view.id}>{view.name}{view.shared ? " · Shared" : " · Private"}</option>)}
        </select></label>
        <button type="button" onClick={() => setViewDialogOpen(true)}>Save view</button>
      </> : null}
      {permissions.canManageTags === true ? <button type="button" onClick={() => setCatalogOpen(true)}>Manage tags</button> : null}
    </div>
    {unknownView ? <p role="alert">This saved view is unavailable. Its URL filters are retained; choose an available view or save a new one.</p> : null}
    {modifiedView ? <p className="wf-library-help">The current filters differ from the saved view. Save to update it or choose the view again to restore its filters.</p> : null}
    {location.includeDescendants && typeof location.folderSelection !== "object" ? <p role="alert">The subfolder filter requires a selected folder. <button type="button" onClick={() => change({ includeDescendants: false })}>Clear subfolder filter</button></p> : null}
    {error ? <p role="alert">{error} <button type="button" onClick={() => setRefresh(value => value + 1)}>Retry organization</button></p> : null}
    {location.invalidTagFilters ? <p role="alert">The tag filters in this link are invalid. <button type="button" onClick={() => change({ invalidTagFilters: undefined, tagFilters: [] })}>Clear invalid tag filters</button></p> : null}
    {filtersOpen && support.tags ? <div className="wf-library-filter-panel" id={filtersId}>
      <label>Publication <select aria-label="Publication filter" value={location.publicationState ?? ""} onChange={event => change({ publicationState: event.target.value || undefined })}>
        <option value="">Any publication state</option><option value="published">Published</option>{location.publicationState && location.publicationState !== "published" ? <option value={location.publicationState}>Unsupported state: {location.publicationState}</option> : null}
      </select></label>
      <div className="wf-library-control-row"><label>Find tag <input aria-label="Find tags for filters" value={catalogSearch} onChange={event => setCatalogSearch(event.target.value)} /></label><button type="button" disabled={catalogLoading} onClick={() => setCatalogTerm(catalogSearch)}>Find tags</button>{catalogCursor ? <button type="button" disabled={catalogLoading} onClick={() => void loadMoreTags()}>Load more tags</button> : null}</div>
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
      <fieldset className="wf-library-values"><legend>Optional columns</legend>{["tags", "location"].map(column => <label key={column}><input type="checkbox" checked={location.columns?.includes(column) ?? false} onChange={event => change({ columns: event.target.checked ? [...(location.columns ?? []), column] : location.columns?.filter(item => item !== column) })} />{column === "tags" ? "Tags" : "Location"}</label>)}</fieldset>
    </div> : null}
    {catalogOpen ? <Suspense fallback={<p role="status">Loading tag manager…</p>}><WorkflowTagCatalogDialog context={context} tags={tags} onClose={() => setCatalogOpen(false)} onSaved={() => setRefresh(value => value + 1)} /></Suspense> : null}
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
      <div className="wf-dialog-actions"><button type="button" disabled={busy} onClick={onClose}>Cancel</button>{error ? <button type="button" disabled={busy} onClick={onSaved}>Reload view list</button> : null}
        {existing?.canDelete ? <button type="button" disabled={busy} onClick={() => void remove()}>Delete saved view</button> : null}
        {existing?.canEdit ? <button type="button" disabled={busy || !name.trim() || (shared && !canShare)} onClick={() => void save(true)}>Update saved view</button> : null}
        <button type="submit" disabled={busy || !name.trim() || (shared && !canShare)}>{busy ? "Saving…" : "Save as new view"}</button>
      </div>
    </form>
  </section></div>;
}

function mergeTags(current: WorkflowTag[], next: WorkflowTag[]) {
  return [...new Map([...current, ...next].map(tag => [tag.id, tag])).values()];
}
function message(caught: unknown, fallback: string) { return caught instanceof Error ? `${fallback} ${caught.message}` : fallback; }
