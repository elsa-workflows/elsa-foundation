import { useEffect, useRef, useState } from "react";
import { SkeletonRows, StudioButton, StudioCollectionFooter, StudioCollectionToolbar, StudioPage, StudioPageHeader, StudioResultState, StudioSearchInput, StudioTableContainer } from "@elsa-workflows/studio-ui";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { getWorkflowTag, listWorkflowTags, workflowOrganizationSupport, type WorkflowTag, type WorkflowTagPage } from "../api/workflowOrganization";
import { StudioLink } from "../StudioLink";
import { WorkflowTagEditor } from "./WorkflowTagEditor";
import "./workflowTags.css";

const tagsPath = "/workflows/tags";
const tagPath = (id: string) => `${tagsPath}/${encodeURIComponent(id)}`;
type View = "list" | "create" | "edit";
type Loaded = { identity: string; context: StudioEndpointContext; page?: WorkflowTagPage; tag?: WorkflowTag; unsupported?: boolean; error?: string };
const message = (error: unknown) => error instanceof Error ? error.message : "Check your access and try again.";

export function WorkflowTagsRoute({ context, view, navigate }: { context: StudioEndpointContext; view: View; navigate(path: string): void }) {
  const [pathname, setPathname] = useState(() => window.location.pathname);
  const [loaded, setLoaded] = useState<Loaded>();
  const loadedRef = useRef(loaded);
  loadedRef.current = loaded;
  const [attempt, setAttempt] = useState(0);
  useEffect(() => {
    const sync = () => setPathname(window.location.pathname);
    window.addEventListener("popstate", sync);
    return () => window.removeEventListener("popstate", sync);
  }, []);
  const tagId = view === "edit" ? pathname.split("/").pop() ?? "" : "";
  const identity = `${view}:${tagId}`;
  useEffect(() => {
    // Activity reconnects effects when a retained tab returns. Its current resource and draft
    // are already loaded; explicit refresh/reload remains with the owning list/editor.
    const current = loadedRef.current;
    if (current?.context === context && current.identity === identity && current.page) return;
    let active = true;
    void (async () => {
      try {
        const support = await workflowOrganizationSupport(context);
        if (!active) return;
        if (!support.tags) { setLoaded({ identity, context, unsupported: true }); return; }
        const page = await listWorkflowTags(context);
        if (!active) return;
        // The catalog's server-issued permission also guards direct create/edit navigation.
        const tag = view === "edit" && page.permissions?.canManageTags === true
          ? await getWorkflowTag(context, decodeURIComponent(tagId)) : undefined;
        if (active) { setLoaded({ identity, context, page, tag }); }
      } catch (error) {
        if (active) { setLoaded({ identity, context, error: message(error) }); }
      }
    })();
    return () => { active = false; };
  }, [attempt, context, identity, tagId, view]);

  if (!loaded || loaded.context !== context || loaded.identity !== identity) return <SkeletonRows label="Loading workflow tags" />;
  const back = <StudioButton onClick={() => navigate(view === "list" ? "/workflows/definitions" : tagsPath)}>{view === "list" ? "Back to workflows" : "Back to tags"}</StudioButton>;
  if (loaded.error || loaded.unsupported || (view !== "list" && loaded.page?.permissions?.canManageTags !== true)) {
    return <StudioPage><StudioPageHeader title="Workflow tags" actions={back} />
      <StudioResultState tone={loaded.error ? "danger" : undefined}
        title={loaded.error ? "Could not load workflow tags" : loaded.unsupported ? "Workflow tags unavailable" : "Tag management is not allowed"}
        description={loaded.error ?? (loaded.unsupported ? "This backend does not support workflow tags." : "You do not have permission to manage workflow tags.")}
        actions={loaded.error ? <StudioButton onClick={() => { setLoaded(undefined); setAttempt(value => value + 1); }}>Retry</StudioButton> : undefined} />
    </StudioPage>;
  }
  return view === "list" ? <WorkflowTagsPage key={attempt} context={context} initialPage={loaded.page!} navigate={navigate} />
    : <WorkflowTagEditor key={identity} context={context} tag={loaded.tag} onBack={() => navigate(tagsPath)} onCreated={tag => navigate(tagPath(tag.id))} />;
}

function WorkflowTagsPage({ context, initialPage, navigate }: { context: StudioEndpointContext; initialPage: WorkflowTagPage; navigate(path: string): void }) {
  const [page, setPage] = useState(initialPage);
  const [search, setSearch] = useState("");
  const [term, setTerm] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const generation = useRef(0);
  // A hidden tab retains this collection. Its pending reads may still settle local state;
  // a newer search invalidates them, and a real route/context change unmounts the collection.
  const load = async (nextTerm: string, more = false) => {
    const current = ++generation.current;
    setBusy(true); setError("");
    // Clear results when the search scope changes; a failed search must not show unrelated rows.
    if (!more) setPage({ items: [], permissions: page.permissions });
    try {
      const next = await listWorkflowTags(context, nextTerm, more ? page.nextContinuationToken : undefined);
      if (current !== generation.current) return;
      setTerm(nextTerm);
      setPage(previous => ({ ...next, items: more ? [...new Map([...previous.items, ...next.items].map(tag => [tag.id, tag])).values()] : next.items }));
    } catch (caught) { if (current === generation.current) setError(message(caught)); }
    finally { if (current === generation.current) setBusy(false); }
  };
  const canManage = page.permissions?.canManageTags === true;
  return <StudioPage className="wf-tag-page" aria-labelledby="workflow-tags-title">
    <StudioPageHeader title="Workflow tags" titleId="workflow-tags-title" description="Manage reusable labels and controlled classifications for workflows."
      actions={<>
        <StudioButton onClick={() => navigate("/workflows/definitions")}>Back to workflows</StudioButton>
        {canManage ? <StudioButton variant="primary" onClick={() => navigate(`${tagsPath}/new`)}>Create tag</StudioButton> : null}
        <StudioButton disabled={busy} onClick={() => void load(term)}>Refresh</StudioButton>
      </>} />
    <form onSubmit={event => { event.preventDefault(); void load(search.trim()); }}>
      <StudioCollectionToolbar>
        <StudioSearchInput aria-label="Search workflow tags" placeholder="Search tags" value={search} onChange={event => setSearch(event.currentTarget.value)} />
        <StudioButton type="submit" disabled={busy}>Search</StudioButton>
      </StudioCollectionToolbar>
    </form>
    {error ? <StudioResultState tone="danger" title="Could not load workflow tags" description={error}
      actions={<StudioButton onClick={() => void load(search.trim())}>Retry</StudioButton>} /> : null}
    {busy && !page.items.length ? <SkeletonRows label="Loading workflow tags" /> : page.items.length ? <StudioTableContainer role="region" aria-label="Workflow tags table" tabIndex={0}>
      <table className="studio-table" aria-label="Workflow tags">
        <thead><tr><th scope="col">Name</th><th scope="col">Description</th><th scope="col">Kind</th><th scope="col">Status</th></tr></thead>
        <tbody>{page.items.map(tag => <tr key={tag.id}>
          <td>{canManage ? <StudioLink href={tagPath(tag.id)} navigateToStudioPath={navigate}>{tag.name}</StudioLink> : tag.name}</td>
          <td>{tag.description || "—"}</td>
          <td>{tag.mode === "marker" ? "Simple label" : `Controlled values · ${tag.cardinality === "single" ? "One per workflow" : "Several per workflow"}`}</td>
          <td>{tag.deprecated ? "Retired" : "Active"}</td>
        </tr>)}</tbody>
      </table>
    </StudioTableContainer> : !error ? <StudioResultState title={term ? "No matching tags" : "No workflow tags yet"}
      description={term ? "Try a different search." : "Create a tag to organize workflows."} /> : null}
    <StudioCollectionFooter>
      <span>{page.items.length} tags loaded</span>
      {page.nextContinuationToken ? <StudioButton disabled={busy} onClick={() => void load(term, true)}>{busy ? "Loading…" : "Load more tags"}</StudioButton> : null}
    </StudioCollectionFooter>
  </StudioPage>;
}
