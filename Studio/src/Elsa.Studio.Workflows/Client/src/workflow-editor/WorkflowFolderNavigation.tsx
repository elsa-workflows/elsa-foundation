import { ChevronDown, ChevronRight, Folder, FolderPen, FolderPlus, Menu, Move, Trash2, X } from "lucide-react";
import { useCallback, useEffect, useId, useRef, useState, type KeyboardEvent, type MutableRefObject, type ReactNode } from "react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { createWorkflowFolder, deleteEmptyWorkflowFolder, getWorkflowFolder, getWorkflowFolderMutationSupport, listWorkflowFolders, workflowFoldersPath } from "../api/workflowDesign";
import type { WorkflowFolder } from "../workflowTypes";
import { getDialogs } from "./dialogs";
import { useDialogFocus } from "./useDialogFocus";
import { MoveWorkflowFolderDialog, RenameWorkflowFolderDialog } from "./WorkflowFolderMutationDialogs";

export type WorkflowFolderSelection = "all" | "unfiled" | { id: string };

export function selectedFolderId(selection: WorkflowFolderSelection) {
  return typeof selection === "object" ? selection.id : null;
}

const rootKey = "root";

export function WorkflowFolderNavigation({ context, selection, onSelect, onAvailable, onFoldersMutated, refreshKey = 0 }: {
  context: StudioEndpointContext;
  selection: WorkflowFolderSelection;
  onSelect(selection: WorkflowFolderSelection): void;
  onAvailable(available: boolean): void;
  onFoldersMutated?(selection?: WorkflowFolderSelection): Promise<void> | void;
  /** Refreshes loaded folder projections without remounting the tree or discarding its expansion state. */
  refreshKey?: number;
}) {
  const [available, setAvailable] = useState<boolean | null>(null);
  const [roots, setRoots] = useState<WorkflowFolder[]>([]);
  const [children, setChildren] = useState<Record<string, WorkflowFolder[]>>({});
  const [continuations, setContinuations] = useState<Record<string, string | null>>({});
  const [expanded, setExpanded] = useState<Set<string>>(() => new Set());
  const [loadingKeys, setLoadingKeys] = useState<Set<string>>(() => new Set());
  const [loadFailures, setLoadFailures] = useState<Record<string, string>>({});
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [crumbs, setCrumbs] = useState<WorkflowFolder[]>([]);
  const [breadcrumbState, setBreadcrumbState] = useState<"idle" | "loading" | "ready" | "failed">("idle");
  const [breadcrumbError, setBreadcrumbError] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [focusedKey, setFocusedKey] = useState("all");
  const [mutationSupport, setMutationSupport] = useState({ rename: false, move: false, deleteEmpty: false });
  const [mutationDialog, setMutationDialog] = useState<"rename" | "move" | null>(null);
  const [localRefreshKey, setLocalRefreshKey] = useState(0);
  const [status, setStatus] = useState("");
  const [pendingChildFocusId, setPendingChildFocusId] = useState<string | null>(null);
  const keyboardExpansionIntents = useRef(new Set<string>());
  const inFlightGenerations = useRef<Record<string, number>>({});
  const loadedPageCounts = useRef<Record<string, number>>({});
  const nextLoadGeneration = useRef(0);
  const appliedRefreshKey = useRef(refreshKey);
  const folderPickerButtonRef = useRef<HTMLButtonElement>(null);
  const renameButtonRef = useRef<HTMLButtonElement>(null);
  const moveButtonRef = useRef<HTMLButtonElement>(null);

  const loadFolderPage = useCallback(async (parentId?: string, continuationToken?: string | null, append = false, force = false) => {
    const key = parentId ?? rootKey;
    if (inFlightGenerations.current[key] && !force) return false;
    const generation = ++nextLoadGeneration.current;
    inFlightGenerations.current[key] = generation;
    setLoadingKeys(current => new Set(current).add(key));
    setLoadFailures(current => {
      const next = { ...current };
      delete next[key];
      return next;
    });
    try {
      const pageCount = force ? loadedPageCounts.current[key] ?? 1 : 1;
      const items: WorkflowFolder[] = [];
      let nextToken = force ? undefined : continuationToken;
      let nextContinuationToken: string | null = null;
      let loadedPages = 0;
      for (let pageIndex = 0; pageIndex < pageCount; pageIndex += 1) {
        const page = await listWorkflowFolders(context, { parentId, continuationToken: nextToken });
        if (inFlightGenerations.current[key] !== generation) return false;
        if (!page) return false;
        items.push(...page.items);
        nextContinuationToken = page.nextContinuationToken;
        loadedPages += 1;
        if (!force || !nextContinuationToken) break;
        nextToken = nextContinuationToken;
      }
      if (parentId) {
        setChildren(current => ({
          ...current,
          [parentId]: append ? [...(current[parentId] ?? []), ...items] : items
        }));
      } else {
        setRoots(current => append ? [...current, ...items] : items);
      }
      loadedPageCounts.current[key] = append ? (loadedPageCounts.current[key] ?? 1) + loadedPages : loadedPages;
      setContinuations(current => ({ ...current, [key]: nextContinuationToken }));
      return true;
    } catch (caught) {
      if (inFlightGenerations.current[key] !== generation) return false;
      setLoadFailures(current => ({
        ...current,
        [key]: caught instanceof Error ? caught.message : "Couldn't load folders."
      }));
      return false;
    } finally {
      if (inFlightGenerations.current[key] === generation) {
        delete inFlightGenerations.current[key];
        setLoadingKeys(current => {
          const next = new Set(current);
          next.delete(key);
          return next;
        });
      }
    }
  }, [context]);

  useEffect(() => {
    let cancelled = false;
    void workflowFoldersPath(context).then(root => {
      if (cancelled) return;
      const enabled = !!root;
      setAvailable(enabled);
      onAvailable(enabled);
      if (enabled) void loadFolderPage();
    }).catch(() => {
      if (!cancelled) setError("Couldn't inspect workflow folder support. Refresh the page to retry.");
    });
    return () => { cancelled = true; };
  }, [context, loadFolderPage, onAvailable]);

  useEffect(() => {
    let cancelled = false;
    void getWorkflowFolderMutationSupport(context)
      .then(support => { if (!cancelled) setMutationSupport(support); })
      .catch(() => { if (!cancelled) setMutationSupport({ rename: false, move: false, deleteEmpty: false }); });
    return () => { cancelled = true; };
  }, [context]);

  useEffect(() => {
    if (refreshKey === appliedRefreshKey.current) return;
    appliedRefreshKey.current = refreshKey;
    if (!available) return;
    void loadFolderPage(undefined, undefined, false, true);
    for (const folderId of expanded) void loadFolderPage(folderId, undefined, false, true);
  }, [available, expanded, loadFolderPage, refreshKey]);

  useEffect(() => {
    const id = selectedFolderId(selection);
    setCrumbs([]);
    setBreadcrumbError(null);
    if (!id || !available) { setBreadcrumbState("idle"); return; }
    setBreadcrumbState("loading");
    let cancelled = false;
    void getWorkflowFolder(context, id)
      .then(detail => {
        if (!cancelled) {
          setCrumbs(detail ? [...detail.ancestors, detail.folder] : []);
          setBreadcrumbState("ready");
        }
      })
      .catch(() => {
        if (!cancelled) {
          setBreadcrumbError("Couldn't load this folder's path. Select the folder again to retry.");
          setBreadcrumbState("failed");
        }
      });
    return () => { cancelled = true; };
  }, [available, context, localRefreshKey, refreshKey, selection]);

  const toggle = async (folder: WorkflowFolder) => {
    if (expanded.has(folder.id)) {
      setExpanded(current => {
        const next = new Set(current);
        next.delete(folder.id);
        return next;
      });
      return;
    }
    setExpanded(current => new Set(current).add(folder.id));
    if (!children[folder.id]) await loadFolderPage(folder.id);
  };

  const select = (next: WorkflowFolderSelection) => {
    setError(null);
    setStatus("");
    setFocusedKey(typeof next === "object" ? `folder:${next.id}` : next);
    onSelect(next);
    setDrawerOpen(false);
  };

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
      await loadFolderPage(parentId ?? undefined, undefined, false, true);
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "Couldn't create the folder.");
    }
  };

  const selectedId = selectedFolderId(selection);
  const selectedFolder = selectedId ? findFolder(roots, children, selectedId) ?? crumbs.at(-1) : undefined;

  const refreshAfterStructureChange = async (nextSelection?: WorkflowFolderSelection) => {
    await Promise.all([
      loadFolderPage(undefined, undefined, false, true),
      ...Array.from(expanded, folderId => loadFolderPage(folderId, undefined, false, true))
    ]);
    setLocalRefreshKey(current => current + 1);
    await onFoldersMutated?.(nextSelection);
  };

  const restoreFolderFocus = (key: string) => {
    requestAnimationFrame(() => {
      if (window.matchMedia?.("(max-width: 700px)").matches) {
        folderPickerButtonRef.current?.focus();
        return;
      }
      document.querySelector<HTMLElement>(`.wf-folder-nav [data-tree-key="${key}"]`)?.focus();
    });
  };

  const closeMutationDialog = (action: "rename" | "move") => {
    setMutationDialog(null);
    requestAnimationFrame(() => (action === "rename" ? renameButtonRef.current : moveButtonRef.current)?.focus());
  };

  const deleteFolder = async () => {
    if (!selectedFolder) return;
    const confirmed = await getDialogs().confirm({
      message: `Delete empty folder "${selectedFolder.name}"? This cannot be undone.`,
      confirmLabel: "Delete folder",
      tone: "danger"
    });
    if (!confirmed) return;
    setError(null);
    setStatus("");
    try {
      await deleteEmptyWorkflowFolder(context, selectedFolder.id);
      const survivor: WorkflowFolderSelection = selectedFolder.parentId ? { id: selectedFolder.parentId } : "all";
      const survivorKey = selectedFolder.parentId ? `folder:${selectedFolder.parentId}` : "all";
      select(survivor);
      setFocusedKey(survivorKey);
      await refreshAfterStructureChange(survivor);
      setStatus(`Deleted folder ${selectedFolder.name}`);
      restoreFolderFocus(survivorKey);
    } catch (caught) {
      setError(`Couldn't delete this folder. ${caught instanceof Error ? caught.message : "The server rejected the request."} Ensure it is empty and you have permission, then try again.`);
    }
  };

  if (!available) return error ? <div className="wf-alert" role="alert">{error}</div> : null;

  const treeProps = {
    roots,
    children,
    continuations,
    expanded,
    loadingKeys,
    loadFailures,
    selection,
    focusedKey,
    pendingChildFocusId,
    keyboardExpansionIntents,
    onFocusedKeyChange: setFocusedKey,
    onPendingChildFocusChange: setPendingChildFocusId,
    onToggle: toggle,
    onSelect: select,
    onLoadMore: async (parentId?: string) => {
      const key = parentId ?? rootKey;
      if (await loadFolderPage(parentId, continuations[key], true)) {
        setFocusedKey(parentId ? `folder:${parentId}` : "all");
      }
    },
    onRetry: async (parentId?: string) => {
      if (await loadFolderPage(parentId)) {
        setFocusedKey(parentId ? `folder:${parentId}` : "all");
      }
    }
  };

  return <>
    <div className="wf-folder-mobile-actions">
      <button ref={folderPickerButtonRef} type="button" onClick={() => setDrawerOpen(true)} aria-expanded={drawerOpen} aria-controls="workflow-folder-picker"><Menu size={15} /> Folders</button>
    </div>
    {!drawerOpen ? (
      <aside className="wf-folder-nav" aria-label="Workflow folders">
        <div className="wf-folder-nav-heading"><strong>Folders</strong><button type="button" title="Create folder" aria-label="Create folder" onClick={() => void createFolder()}><FolderPlus size={16} /></button></div>
        <FolderTree {...treeProps} />
      </aside>
    ) : null}
    <WorkflowFolderBreadcrumb crumbs={crumbs} state={breadcrumbState} error={breadcrumbError} selection={selection} onSelect={select} />
    {selectedFolder && (mutationSupport.rename || mutationSupport.move || mutationSupport.deleteEmpty) ? (
      <div className="wf-folder-actions" aria-label={`Actions for ${selectedFolder.name}`}>
        {mutationSupport.rename ? <button ref={renameButtonRef} type="button" onClick={() => setMutationDialog("rename")}><FolderPen size={14} /> Rename</button> : null}
        {mutationSupport.move ? <button ref={moveButtonRef} type="button" onClick={() => setMutationDialog("move")}><Move size={14} /> Move</button> : null}
        {mutationSupport.deleteEmpty ? <button type="button" className="danger" onClick={() => void deleteFolder()}><Trash2 size={14} /> Delete</button> : null}
      </div>
    ) : null}
    {error ? <div className="wf-alert" role="alert">{error}</div> : null}
    {status ? <div className="wf-status-line" role="status">{status}</div> : null}
    {drawerOpen ? <FolderDrawer onClose={() => setDrawerOpen(false)} onCreate={createFolder}><FolderTree {...treeProps} /></FolderDrawer> : null}
    {mutationDialog === "rename" && selectedFolder ? <RenameWorkflowFolderDialog
      context={context}
      folder={selectedFolder}
      onClose={() => closeMutationDialog("rename")}
      onRenamed={async () => {
        await refreshAfterStructureChange();
        setStatus("Folder renamed");
      }}
    /> : null}
    {mutationDialog === "move" && selectedFolder ? <MoveWorkflowFolderDialog
      context={context}
      folder={selectedFolder}
      onClose={() => closeMutationDialog("move")}
      onMoved={async () => {
        await refreshAfterStructureChange();
        setStatus("Folder moved");
      }}
    /> : null}
  </>;
}

interface FolderTreeProps {
  roots: WorkflowFolder[];
  children: Record<string, WorkflowFolder[]>;
  continuations: Record<string, string | null>;
  expanded: Set<string>;
  loadingKeys: Set<string>;
  loadFailures: Record<string, string>;
  selection: WorkflowFolderSelection;
  focusedKey: string;
  pendingChildFocusId: string | null;
  keyboardExpansionIntents: MutableRefObject<Set<string>>;
  onFocusedKeyChange(key: string): void;
  onPendingChildFocusChange(folderId: string | null): void;
  onToggle(folder: WorkflowFolder): void;
  onSelect(selection: WorkflowFolderSelection): void;
  onLoadMore(parentId?: string): Promise<void>;
  onRetry(parentId?: string): Promise<void>;
}

function firstDirectFolderChild(parent: HTMLElement) {
  const group = [...parent.children].find(element => element.getAttribute("role") === "group");
  return [...(group?.children ?? [])].find(element =>
    element.getAttribute("role") === "treeitem" && element.getAttribute("data-kind") !== "status"
  ) as HTMLElement | undefined;
}

function FolderTree(props: FolderTreeProps) {
  const {
    roots, children, continuations, expanded, loadingKeys, loadFailures, selection, focusedKey,
    pendingChildFocusId, keyboardExpansionIntents, onFocusedKeyChange, onPendingChildFocusChange,
    onToggle, onSelect, onLoadMore, onRetry
  } = props;
  const active = selectedFolderId(selection);
  const treeId = useId().replaceAll(":", "");
  const treeRef = useRef<HTMLDivElement>(null);

  const focusItem = (item: HTMLElement | null | undefined) => {
    if (!item) return;
    onFocusedKeyChange(item.dataset.treeKey ?? "all");
    item.focus();
  };

  useEffect(() => {
    if (!pendingChildFocusId) return;
    const parent = [...(treeRef.current?.querySelectorAll<HTMLElement>("[data-folder-id]") ?? [])]
      .find(item => item.dataset.folderId === pendingChildFocusId);
    if (!parent || document.activeElement !== parent) {
      keyboardExpansionIntents.current.delete(pendingChildFocusId);
      onPendingChildFocusChange(null);
      return;
    }
    const firstChild = firstDirectFolderChild(parent);
    if (firstChild) {
      keyboardExpansionIntents.current.delete(pendingChildFocusId);
      onPendingChildFocusChange(null);
      onFocusedKeyChange(firstChild.dataset.treeKey ?? "all");
      firstChild.focus();
      return;
    }
    if (loadFailures[pendingChildFocusId] || (pendingChildFocusId in children && !loadingKeys.has(pendingChildFocusId))) {
      keyboardExpansionIntents.current.delete(pendingChildFocusId);
      onPendingChildFocusChange(null);
    }
  }, [children, keyboardExpansionIntents, loadFailures, loadingKeys, onFocusedKeyChange, onPendingChildFocusChange, pendingChildFocusId]);

  const activate = (item: HTMLElement) => {
    const kind = item.dataset.kind;
    if (kind === "folder") onSelect({ id: item.dataset.folderId! });
    else if (kind === "selection") onSelect(item.dataset.selection as "all" | "unfiled");
    else item.click();
  };

  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    const current = (event.target as HTMLElement).closest<HTMLElement>('[role="treeitem"]');
    if (!current || !event.currentTarget.contains(current)) return;
    const items = [...event.currentTarget.querySelectorAll<HTMLElement>('[role="treeitem"]:not([data-kind="status"])')];
    const index = items.indexOf(current);
    if (event.key === "ArrowDown") { event.preventDefault(); focusItem(items[index + 1]); return; }
    if (event.key === "ArrowUp") { event.preventDefault(); focusItem(items[index - 1]); return; }
    if (event.key === "Home") { event.preventDefault(); focusItem(items[0]); return; }
    if (event.key === "End") { event.preventDefault(); focusItem(items.at(-1)); return; }
    if (event.key === "Enter" || event.key === " ") { event.preventDefault(); activate(current); return; }
    if (current.dataset.kind !== "folder") return;
    const folder = findFolder(roots, children, current.dataset.folderId!);
    if (!folder) return;
    if (event.key === "ArrowRight") {
      event.preventDefault();
      if (!expanded.has(folder.id)) {
        if (keyboardExpansionIntents.current.has(folder.id)) onPendingChildFocusChange(folder.id);
        else {
          keyboardExpansionIntents.current.add(folder.id);
          void onToggle(folder);
        }
      } else {
        const firstChild = firstDirectFolderChild(current);
        if (firstChild) {
          keyboardExpansionIntents.current.delete(folder.id);
          focusItem(firstChild);
        } else {
          onPendingChildFocusChange(folder.id);
        }
      }
      return;
    }
    if (event.key === "ArrowLeft") {
      event.preventDefault();
      keyboardExpansionIntents.current.delete(folder.id);
      if (pendingChildFocusId === folder.id) onPendingChildFocusChange(null);
      if (expanded.has(folder.id)) void onToggle(folder);
      else focusItem(current.parentElement?.closest<HTMLElement>('[role="treeitem"]') ?? undefined);
    }
  };

  const tabIndexFor = (key: string) => focusedKey === key ? 0 : -1;
  const selectionRow = (label: string, value: "all" | "unfiled") => (
    <div
      key={value}
      role="treeitem"
      tabIndex={tabIndexFor(value)}
      data-tree-key={value}
      data-kind="selection"
      data-selection={value}
      aria-selected={selection === value}
      className="wf-folder-tree-row"
      onFocus={event => {
        if (event.currentTarget === event.target) onFocusedKeyChange(value);
      }}
      onClick={() => onSelect(value)}
    >{label}</div>
  );

  const actionRow = (parentId: string | undefined, kind: "load-more" | "retry", label: string, announcement?: string) => {
    const parentKey = parentId ?? rootKey;
    const key = `${kind}:${parentKey}`;
    const loading = loadingKeys.has(parentKey);
    return (
      <button
        type="button"
        role="treeitem"
        tabIndex={tabIndexFor(key)}
        data-tree-key={key}
        data-kind={kind}
        className="wf-folder-load-more"
        aria-disabled={loading}
        onFocus={() => onFocusedKeyChange(key)}
        onClick={event => {
          if (loading) return;
          const tree = event.currentTarget.closest<HTMLElement>('[role="tree"]');
          const survivorKey = parentId ? `folder:${parentId}` : "all";
          onFocusedKeyChange(survivorKey);
          requestAnimationFrame(() => tree?.querySelector<HTMLElement>(`[data-tree-key="${survivorKey}"]`)?.focus());
          void (kind === "retry" ? onRetry(parentId) : onLoadMore(parentId));
        }}
      >
        {announcement ? <span role="alert">{announcement} </span> : null}
        {loading ? "Loading folders…" : label}
      </button>
    );
  };

  const folderRows = (items: WorkflowFolder[], level: number, parentId?: string): ReactNode => {
    const parentKey = parentId ?? rootKey;
    const loading = loadingKeys.has(parentKey);
    return <>
      {items.map(folder => {
        const key = `folder:${folder.id}`;
        const groupId = `${treeId}-children-${folder.id}`;
        return (
          <div
            key={folder.id}
            role="treeitem"
            tabIndex={tabIndexFor(key)}
            data-tree-key={key}
            data-kind="folder"
            data-folder-id={folder.id}
            aria-expanded={expanded.has(folder.id)}
            aria-selected={folder.id === active}
            aria-controls={groupId}
            className="wf-folder-tree-item"
            onFocus={event => {
              if ((event.target as HTMLElement).closest('[role="treeitem"]') === event.currentTarget) onFocusedKeyChange(key);
            }}
            onClick={event => {
              if ((event.target as HTMLElement).closest('[role="treeitem"]') === event.currentTarget) {
                event.currentTarget.focus();
                onSelect({ id: folder.id });
              }
            }}
          >
            <div className="wf-folder-tree-folder" style={{ paddingInlineStart: `${level * 14}px` }}>
              <button
                type="button"
                tabIndex={-1}
                className="wf-folder-expand"
                aria-label={`${expanded.has(folder.id) ? "Collapse" : "Expand"} ${folder.name}`}
                aria-expanded={expanded.has(folder.id)}
                aria-controls={groupId}
                onClick={event => {
                  event.stopPropagation();
                  keyboardExpansionIntents.current.delete(folder.id);
                  if (pendingChildFocusId === folder.id) onPendingChildFocusChange(null);
                  event.currentTarget.closest<HTMLElement>('[role="treeitem"]')?.focus();
                  void onToggle(folder);
                }}
              >{expanded.has(folder.id) ? <ChevronDown size={14} /> : <ChevronRight size={14} />}</button>
              <span className="wf-folder-tree-row"><Folder size={14} />{folder.name}</span>
            </div>
            {expanded.has(folder.id) ? (
              <div id={groupId} role="group" aria-busy={loadingKeys.has(folder.id)}>
                {folderRows(children[folder.id] ?? [], level + 1, folder.id)}
              </div>
            ) : null}
          </div>
        );
      })}
      {loading && items.length === 0 ? (
        <div role="treeitem" tabIndex={-1} data-kind="status" aria-disabled="true" className="wf-folder-loading">
          <span role="status">Loading folders…</span>
        </div>
      ) : null}
      {loadFailures[parentKey] ? (
        actionRow(parentId, "retry", "Retry loading folders", `Couldn't load folders. ${loadFailures[parentKey]}`)
      ) : continuations[parentKey] ? actionRow(parentId, "load-more", "Load more folders") : null}
    </>;
  };

  return (
    <div
      ref={treeRef}
      role="tree"
      aria-label="Workflow folders"
      aria-busy={loadingKeys.has(rootKey)}
      className="wf-folder-tree"
      onKeyDown={onKeyDown}
      onPointerDown={() => {
        keyboardExpansionIntents.current.clear();
        onPendingChildFocusChange(null);
      }}
    >
      {selectionRow("All workflows", "all")}
      {selectionRow("Unfiled", "unfiled")}
      {folderRows(roots, 0)}
    </div>
  );
}

function WorkflowFolderBreadcrumb({ crumbs, state, error, selection, onSelect }: {
  crumbs: WorkflowFolder[];
  state: "idle" | "loading" | "ready" | "failed";
  error: string | null;
  selection: WorkflowFolderSelection;
  onSelect(selection: WorkflowFolderSelection): void;
}) {
  if (selection === "all") return <div className="wf-folder-breadcrumb" aria-label="Folder breadcrumb">All workflows</div>;
  if (selection === "unfiled") return <div className="wf-folder-breadcrumb" aria-label="Folder breadcrumb">Unfiled</div>;
  if (state === "loading") return <div className="wf-folder-breadcrumb" aria-label="Folder breadcrumb" aria-busy="true"><span role="status">Loading folder path…</span></div>;
  if (state === "failed") return <div className="wf-folder-breadcrumb" aria-label="Folder breadcrumb"><span role="alert">{error}</span></div>;
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
