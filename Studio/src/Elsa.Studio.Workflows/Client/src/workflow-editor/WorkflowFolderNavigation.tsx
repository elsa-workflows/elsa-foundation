import { ChevronDown, ChevronRight, Folder, FolderPlus, Menu, X } from "lucide-react";
import { useEffect, useId, useRef, useState, type KeyboardEvent, type ReactNode } from "react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { createWorkflowFolder, getWorkflowFolder, listWorkflowFolders, workflowFoldersPath } from "../api/workflowDesign";
import type { WorkflowFolder } from "../workflowTypes";
import { getDialogs } from "./dialogs";
import { useDialogFocus } from "./useDialogFocus";

export type WorkflowFolderSelection = "all" | "unfiled" | { id: string };

export function selectedFolderId(selection: WorkflowFolderSelection) {
  return typeof selection === "object" ? selection.id : null;
}

export function WorkflowFolderNavigation({ context, selection, onSelect, onAvailable }: {
  context: StudioEndpointContext;
  selection: WorkflowFolderSelection;
  onSelect(selection: WorkflowFolderSelection): void;
  onAvailable(available: boolean): void;
}) {
  const [available, setAvailable] = useState<boolean | null>(null);
  const [roots, setRoots] = useState<WorkflowFolder[]>([]);
  const [children, setChildren] = useState<Record<string, WorkflowFolder[]>>({});
  const [continuations, setContinuations] = useState<Record<string, string | null>>({});
  const [expanded, setExpanded] = useState<Set<string>>(() => new Set());
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [crumbs, setCrumbs] = useState<WorkflowFolder[]>([]);
  const [error, setError] = useState<string | null>(null);

  const refreshRoots = async () => {
    const page = await listWorkflowFolders(context);
    if (page) { setRoots(page.items); setContinuations(current => ({ ...current, root: page.nextContinuationToken })); }
  };

  useEffect(() => {
    let cancelled = false;
    void workflowFoldersPath(context).then(async root => {
      if (cancelled) return;
      const enabled = !!root;
      setAvailable(enabled);
      onAvailable(enabled);
      if (enabled) await refreshRoots();
    }).catch(() => {
      if (!cancelled) setError("Couldn't load workflow folders. Try refreshing the page.");
    });
    return () => { cancelled = true; };
  // The advertised relation is the capability gate; do not probe folder endpoints without it.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [context]);

  useEffect(() => {
    const id = selectedFolderId(selection);
    if (!id || !available) { setCrumbs([]); return; }
    void getWorkflowFolder(context, id)
      .then(detail => setCrumbs(detail ? [...detail.ancestors, detail.folder] : []))
      .catch(() => setError("Couldn't load this folder's breadcrumb."));
  }, [available, context, selection]);

  const toggle = async (folder: WorkflowFolder) => {
    if (expanded.has(folder.id)) {
      setExpanded(current => { const next = new Set(current); next.delete(folder.id); return next; });
      return;
    }
    if (!children[folder.id]) {
      const loaded = await listWorkflowFolders(context, { parentId: folder.id });
      if (loaded) {
        setChildren(current => ({ ...current, [folder.id]: loaded.items }));
        setContinuations(current => ({ ...current, [folder.id]: loaded.nextContinuationToken }));
      }
    }
    setExpanded(current => new Set(current).add(folder.id));
  };

  const select = (next: WorkflowFolderSelection) => { setError(null); onSelect(next); setDrawerOpen(false); };
  const createFolder = async () => {
    const parentId = selectedFolderId(selection);
    const name = (await getDialogs().prompt({
      title: "Create folder",
      message: parentId ? "Enter a name for the folder in the current folder." : "Enter a name for the top-level folder.",
      confirmLabel: "Create"
    }))?.trim();
    if (!name) return;
    try {
      await createWorkflowFolder(context, { name, parentId });
      if (parentId) {
        const loaded = await listWorkflowFolders(context, { parentId });
        if (loaded) {
          setChildren(current => ({ ...current, [parentId]: loaded.items }));
          setContinuations(current => ({ ...current, [parentId]: loaded.nextContinuationToken }));
        }
      } else await refreshRoots();
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "Couldn't create the folder.");
    }
  };

  if (!available) return error ? <div className="wf-alert" role="alert">{error}</div> : null;
  const loadMore = async (parentId?: string) => {
    const key = parentId ?? "root";
    const continuationToken = continuations[key];
    if (!continuationToken) return;
    const page = await listWorkflowFolders(context, { parentId, continuationToken });
    if (!page) return;
    if (parentId) setChildren(current => ({ ...current, [parentId]: [...(current[parentId] ?? []), ...page.items] }));
    else setRoots(current => [...current, ...page.items]);
    setContinuations(current => ({ ...current, [key]: page.nextContinuationToken }));
  };
  const tree = <FolderTree roots={roots} children={children} continuations={continuations} expanded={expanded} selection={selection} onToggle={toggle} onSelect={select} onLoadMore={loadMore} />;
  return <>
    <div className="wf-folder-mobile-actions">
      <button type="button" onClick={() => setDrawerOpen(true)} aria-expanded={drawerOpen} aria-controls="workflow-folder-picker"><Menu size={15} /> Folders</button>
    </div>
    <aside className="wf-folder-nav" aria-label="Workflow folders">
      <div className="wf-folder-nav-heading"><strong>Folders</strong><button type="button" title="Create folder" aria-label="Create folder" onClick={() => void createFolder()}><FolderPlus size={16} /></button></div>
      {tree}
    </aside>
    <WorkflowFolderBreadcrumb crumbs={crumbs} selection={selection} onSelect={select} />
    {error ? <div className="wf-alert" role="alert">{error}</div> : null}
    {drawerOpen ? <FolderDrawer onClose={() => setDrawerOpen(false)} onCreate={createFolder}>{tree}</FolderDrawer> : null}
  </>;
}

function FolderTree({ roots, children, continuations, expanded, selection, onToggle, onSelect, onLoadMore }: {
  roots: WorkflowFolder[]; children: Record<string, WorkflowFolder[]>; continuations: Record<string, string | null>; expanded: Set<string>; selection: WorkflowFolderSelection;
  onToggle(folder: WorkflowFolder): void; onSelect(selection: WorkflowFolderSelection): void; onLoadMore(parentId?: string): void;
}) {
  const active = selectedFolderId(selection);
  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    const items = [...event.currentTarget.querySelectorAll<HTMLElement>('[role="treeitem"]')];
    const index = items.indexOf(document.activeElement as HTMLElement);
    if (event.key === "ArrowDown") { event.preventDefault(); items[index + 1]?.focus(); }
    if (event.key === "ArrowUp") { event.preventDefault(); items[index - 1]?.focus(); }
    if (event.key === "Home") { event.preventDefault(); items[0]?.focus(); }
    if (event.key === "End") { event.preventDefault(); items.at(-1)?.focus(); }
    if (event.key === "ArrowRight") {
      const folderId = (document.activeElement as HTMLElement)?.dataset.folderId;
      const folder = folderId ? findFolder(roots, children, folderId) : undefined;
      if (folder && !expanded.has(folder.id)) { event.preventDefault(); void onToggle(folder); }
      else if (folder && expanded.has(folder.id)) { event.preventDefault(); items[index + 1]?.focus(); }
    }
    if (event.key === "ArrowLeft") {
      const current = document.activeElement as HTMLElement;
      const folderId = current?.dataset.folderId;
      const folder = folderId ? findFolder(roots, children, folderId) : undefined;
      if (folder && expanded.has(folder.id)) { event.preventDefault(); void onToggle(folder); }
      else {
        const parent = current?.parentElement?.closest<HTMLElement>('[role="treeitem"]');
        if (parent) { event.preventDefault(); parent.focus(); }
      }
    }
  };
  const row = (label: string, value: WorkflowFolderSelection, icon?: ReactNode) => <button type="button" role="treeitem" aria-selected={value === selection || (typeof value === "object" && value.id === active)} className="wf-folder-tree-row" onClick={() => onSelect(value)}>{icon}{label}</button>;
  const folders = (items: WorkflowFolder[], level: number, parentId?: string): ReactNode => <>{items.map(folder => <div key={folder.id} role="treeitem" aria-expanded={expanded.has(folder.id)} aria-selected={folder.id === active} tabIndex={0} data-folder-id={folder.id} className="wf-folder-tree-item">
    <div className="wf-folder-tree-folder" style={{ paddingInlineStart: `${level * 14}px` }}>
      <button type="button" className="wf-folder-expand" aria-label={`${expanded.has(folder.id) ? "Collapse" : "Expand"} ${folder.name}`} aria-expanded={expanded.has(folder.id)} onClick={() => void onToggle(folder)}>{expanded.has(folder.id) ? <ChevronDown size={14} /> : <ChevronRight size={14} />}</button>
      <button type="button" className="wf-folder-tree-row" tabIndex={-1} onClick={() => onSelect({ id: folder.id })}><Folder size={14} />{folder.name}</button>
    </div>
    {expanded.has(folder.id) ? <div role="group">{folders(children[folder.id] ?? [], level + 1, folder.id)}</div> : null}
  </div>)}{continuations[parentId ?? "root"] ? <button type="button" className="wf-folder-load-more" onClick={() => void onLoadMore(parentId)}>Load more folders</button> : null}</>;
  return <div role="tree" aria-label="Workflow folders" className="wf-folder-tree" onKeyDown={onKeyDown}>
    {row("All workflows", "all")}
    {row("Unfiled", "unfiled")}
    {folders(roots, 0)}
  </div>;
}

function WorkflowFolderBreadcrumb({ crumbs, selection, onSelect }: { crumbs: WorkflowFolder[]; selection: WorkflowFolderSelection; onSelect(selection: WorkflowFolderSelection): void }) {
  if (selection === "all") return <div className="wf-folder-breadcrumb" aria-label="Folder breadcrumb">All workflows</div>;
  if (selection === "unfiled") return <div className="wf-folder-breadcrumb" aria-label="Folder breadcrumb">Unfiled</div>;
  return <nav className="wf-folder-breadcrumb" aria-label="Folder breadcrumb"><button type="button" onClick={() => onSelect("all")}>All workflows</button>{crumbs.map(folder => <span key={folder.id}><span aria-hidden="true">/</span><button type="button" onClick={() => onSelect({ id: folder.id })}>{folder.name}</button></span>)}</nav>;
}

function findFolder(roots: WorkflowFolder[], children: Record<string, WorkflowFolder[]>, id: string): WorkflowFolder | undefined {
  return [...roots, ...Object.values(children).flat()].find(folder => folder.id === id);
}

function FolderDrawer({ children, onClose, onCreate }: { children: ReactNode; onClose(): void; onCreate(): void }) {
  const ref = useRef<HTMLElement | null>(null);
  const id = useId();
  useDialogFocus(ref, onClose);
  return <div className="wf-folder-drawer-backdrop" role="presentation"><aside ref={ref} id="workflow-folder-picker" className="wf-folder-drawer" role="dialog" aria-modal="true" aria-labelledby={id}><div><strong id={id}>Workflow folders</strong><span><button type="button" aria-label="Create folder" onClick={() => void onCreate()}><FolderPlus size={16} /></button><button type="button" aria-label="Close folder picker" onClick={onClose}><X size={16} /></button></span></div>{children}</aside></div>;
}
