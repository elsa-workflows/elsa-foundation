import { useEffect, useRef, useState } from "react";
import { SkeletonRows, StudioAlert, StudioBreadcrumbs, StudioButton, StudioCollectionEmptyState, StudioCollectionFooter, StudioDialog, StudioDialogFooter, StudioField, StudioPage, StudioPageHeader, StudioResultState, StudioTableContainer } from "@elsa-workflows/studio-ui";
import { Bookmark, Trash2 } from "lucide-react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { deleteWorkflowView, listWorkflowViews, saveWorkflowView, workflowOrganizationSupport, type WorkflowLibraryView } from "../api/workflowOrganization";
import { StudioLink } from "../StudioLink";
import "./workflowOrganization.css";

type View = "list" | "create" | "edit";
type Loaded = { context: StudioEndpointContext; views?: WorkflowLibraryView[]; unsupported?: boolean; error?: string };
const message = (error: unknown) => error instanceof Error ? error.message : "Check your access and try again.";

function SavedViewsBreadcrumbs({ navigate }: { navigate(path: string): void }) {
  return <StudioBreadcrumbs items={[{ href: "/workflows/definitions", label: "Definitions" }]} current="Saved views"
    renderLink={item => <StudioLink href={item.href} navigateToStudioPath={navigate}>{item.label}</StudioLink>} />;
}

export function WorkflowViewsRoute({ context, view, navigate }: { context: StudioEndpointContext; view: View; navigate(path: string): void }) {
  const [loaded, setLoaded] = useState<Loaded>();
  const loadedRef = useRef(loaded);
  loadedRef.current = loaded;
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    const current = loadedRef.current;
    if (current?.context === context && current?.views !== undefined) return;
    let active = true;
    void (async () => {
      try {
        const support = await workflowOrganizationSupport(context);
        if (!active) return;
        if (!support.views) { setLoaded({ context, unsupported: true }); return; }
        const response = await listWorkflowViews(context);
        if (!active) return;
        setLoaded({ context, views: response.items });
      } catch (error) {
        if (active) { setLoaded({ context, error: message(error) }); }
      }
    })();
    return () => { active = false; };
  }, [attempt, context, view]);

  if (!loaded || loaded.context !== context) return <SkeletonRows label="Loading saved views" />;
  if (loaded.error || loaded.unsupported) {
    return <StudioPage><StudioPageHeader title="Saved views" breadcrumbs={<SavedViewsBreadcrumbs navigate={navigate} />} />
      <StudioResultState tone={loaded.error ? "danger" : undefined}
        title={loaded.error ? "Could not load saved views" : "Saved views unavailable"}
        description={loaded.error ?? "This backend does not support saved views."}
        actions={loaded.error ? <StudioButton onClick={() => { setLoaded(undefined); setAttempt(value => value + 1); }}>Retry</StudioButton> : undefined} />
    </StudioPage>;
  }

  return <WorkflowViewsPage key={attempt} context={context} initialViews={loaded.views!} navigate={navigate} />;
}

function WorkflowViewsPage({ context, initialViews, navigate }: { context: StudioEndpointContext; initialViews: WorkflowLibraryView[]; navigate(path: string): void }) {
  const [views, setViews] = useState(initialViews);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [editingView, setEditingView] = useState<WorkflowLibraryView | null>(null);

  const refresh = async () => {
    setBusy(true); setError("");
    try {
      const response = await listWorkflowViews(context);
      setViews(response.items);
    } catch (caught) { setError(message(caught)); }
    finally { setBusy(false); }
  };

  const applyView = (view: WorkflowLibraryView) => {
    const params = new URLSearchParams();
    if (view.query.searchTerm) params.set("search", view.query.searchTerm);
    if (view.query.state && view.query.state !== "active") params.set("state", view.query.state);
    if (view.query.folderId) params.set("folderId", view.query.folderId);
    if (view.query.unfiled) params.set("unfiled", "true");
    if (view.query.includeDescendants) params.set("includeDescendants", "true");
    if (view.query.favoritesOnly) params.set("favoritesOnly", "true");
    if (view.query.sort && view.query.sort !== "name") params.set("sort", view.query.sort);
    if (view.query.publicationState) params.set("publicationState", view.query.publicationState);
    if (view.query.tagFilters?.length) params.set("tagFilters", JSON.stringify(view.query.tagFilters));
    params.set("view", view.id);
    if (view.columns?.length) params.set("columns", view.columns.join(","));
    const query = params.toString();
    navigate(`/workflows/definitions${query ? `?${query}` : ""}`);
  };

  const deleteView = async (view: WorkflowLibraryView) => {
    if (!view.canDelete) return;
    if (!confirm(`Delete "${view.name}"?`)) return;
    setBusy(true); setError("");
    try {
      await deleteWorkflowView(context, view);
      setViews(current => current.filter(item => item.id !== view.id));
    } catch (caught) { setError(message(caught)); }
    finally { setBusy(false); }
  };

  const viewsSaved = () => { setEditingView(null); void refresh(); };

  return <StudioPage className="wf-tag-page" aria-labelledby="workflow-views-title">
    <StudioPageHeader title="Saved views" titleId="workflow-views-title" breadcrumbs={<SavedViewsBreadcrumbs navigate={navigate} />} description="Manage your saved workflow library views."
      actions={<>
        <StudioButton disabled={busy} onClick={() => void refresh()}>Refresh</StudioButton>
      </>} />
    {error ? <StudioResultState tone="danger" title="Could not load saved views" description={error}
      actions={<StudioButton onClick={() => void refresh()}>Retry</StudioButton>} /> : null}
    {busy && !views.length ? <SkeletonRows label="Loading saved views" /> : views.length ? <StudioTableContainer role="region" aria-label="Saved views table" tabIndex={0}>
      <table className="studio-table" aria-label="Saved views">
        <thead><tr><th scope="col">Name</th><th scope="col">Visibility</th><th scope="col">Actions</th></tr></thead>
        <tbody>{views.map(view => <tr key={view.id}>
          <td><strong>{view.name}</strong></td>
          <td>{view.shared ? "Shared" : "Private"}</td>
          <td>
            <div className="wf-library-control-row">
              <StudioButton onClick={() => applyView(view)}><Bookmark size={14} aria-hidden="true" />Apply</StudioButton>
              <StudioButton onClick={() => setEditingView(view)}><span aria-hidden="true">✏️</span>Edit</StudioButton>
              {view.canDelete ? <StudioButton onClick={() => void deleteView(view)}><Trash2 size={14} aria-hidden="true" />Delete</StudioButton> : null}
            </div>
          </td>
        </tr>)}</tbody>
      </table>
    </StudioTableContainer> : !error ? <StudioCollectionEmptyState title="No saved views"
      description="Save a view from the workflow definitions page to organize your filters and sort order." /> : null}
    <StudioCollectionFooter>
      <span>{views.length} saved view{views.length === 1 ? "" : "s"}</span>
    </StudioCollectionFooter>
    {editingView ? <WorkflowViewEditDialog context={context} view={editingView} onClose={() => setEditingView(null)} onSaved={viewsSaved} /> : null}
  </StudioPage>;
}

function WorkflowViewEditDialog({ context, view, onClose, onSaved }: {
  context: StudioEndpointContext; view: WorkflowLibraryView; onClose(): void; onSaved(): void;
}) {
  const nameRef = useRef<HTMLInputElement>(null);
  const [name, setName] = useState(view.name);
  const [shared, setShared] = useState(view.shared);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

  const save = async () => {
    setBusy(true); setError("");
    try {
      await saveWorkflowView(context, { name: name.trim(), query: view.query, shared, columns: view.columns, revision: view.revision }, view.id);
      onSaved();
    } catch (caught) { setError(message(caught)); }
    finally { setBusy(false); }
  };

  return <StudioDialog
    open
    title="Edit view"
    onClose={onClose}
    initialFocusRef={nameRef}
    dismissDisabled={busy}
    dismissOnOutsideClick={false}
    onSubmit={event => {
      event.preventDefault();
      if (!busy && name.trim()) void save();
    }}
    footer={<StudioDialogFooter
      onCancel={onClose}
      cancelDisabled={busy}
      confirmLabel="Save"
      confirmDisabled={busy || !name.trim()}
    />}
  >
    <StudioField label="View name">
      <input ref={nameRef} required maxLength={200} value={name} disabled={busy} onChange={event => setName(event.target.value)} />
    </StudioField>
    <label className="wf-library-check">
      <input type="checkbox" checked={shared} disabled={busy} onChange={event => setShared(event.target.checked)} />
      Share within this tenant
    </label>
    {error ? <StudioAlert tone="danger">{error}</StudioAlert> : null}
  </StudioDialog>;
}
