import { ChevronDown, ChevronRight, Folder, FolderPlus, Menu, X } from "lucide-react";
import { useCallback, useEffect, useId, useRef, useState, type KeyboardEvent, type ReactNode } from "react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { createWorkflowFolder, getWorkflowFolder, listWorkflowFolders, workflowFoldersPath } from "../api/workflowDesign";
import type { WorkflowFolder } from "../workflowTypes";
import { getDialogs } from "./dialogs";
import { useDialogFocus } from "./useDialogFocus";

export type WorkflowFolderSelection = "all" | "unfiled" | { id: string };

export function selectedFolderId(selection: WorkflowFolderSelection) {
  return typeof selection === "object" ? selection.id : null;
}

const rootKey = "root";

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
  const [loadingKeys, setLoadingKeys] = useState<Set<string>>(() => new Set());
  const [loadFailures, setLoadFailures] = useState<Record<string, string>>({});
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [crumbs, setCrumbs] = useState<WorkflowFolder[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [focusedKey, setFocusedKey] = useState("all");
  const pendingKeys = useRef(new Set<string>());

  const loadFolderPage = useCallback(async (parentId?: string, continuationToken?: string | null, append = false) => {
    const key = parentId ?? rootKey;
    if (pendingKeys.current.has(key)) return false;
    pendingKeys.current.add(key);
    setLoadingKeys(current => new Set(current).add(key));
    setLoadFailures(current => {
      const next = { ...current };
      delete next[key];
      return next;
    });
    try {
      const page = await listWorkflowFolders(context, { parentId, continuationToken });
      if (!page) return false;
      if (parentId) {
        setChildren(current => ({
          ...current,
          [parentId]: append ? [...(current[parentId] ?? []), ...page.items] : page.items
        }));
      } else {
        setRoots(current => append ? [...current, ...page.items] : page.items);
      }
      setContinuations(current => ({ ...current, [key]: page.nextContinuationToken }));
      return true;
    } catch (caught) {
      setLoadFailures(current => ({
        ...current,
        [key]: caught instanceof Error ? caught.message : "Couldn't load folders."
      }));
      return false;
    } finally {
      pendingKeys.current.delete(key);
      setLoadingKeys(current => {
        const next = new Set(current);
        next.delete(key);
        return next;
      });
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
    const id = selectedFolderId(selection);
    if (!id || !available) { setCrumbs([]); return; }
    let cancelled = false;
    void getWorkflowFolder(context, id)
      .then(detail => {
        if (!cancelled) setCrumbs(detail ? [...detail.ancestors, detail.folder] : []);
      })
      .catch(() => {
        if (!cancelled) setError("Couldn't load this folder's breadcrumb. Select the folder again to retry.");
      });
    return () => { cancelled = true; };
  }, [available, context, selection]);

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
      await loadFolderPage(parentId ?? undefined);
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "Couldn't create the folder.");
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
    onFocusedKeyChange: setFocusedKey,
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
      <button type="button" onClick={() => setDrawerOpen(true)} aria-expanded={drawerOpen} aria-controls="workflow-folder-picker"><Menu size={15} /> Folders</button>
    </div>
    {!drawerOpen ? (
      <aside className="wf-folder-nav" aria-label="Workflow folders">
        <div className="wf-folder-nav-heading"><strong>Folders</strong><button type="button" title="Create folder" aria-label="Create folder" onClick={() => void createFolder()}><FolderPlus size={16} /></button></div>
        <FolderTree {...treeProps} />
      </aside>
    ) : null}
    <WorkflowFolderBreadcrumb crumbs={crumbs} selection={selection} onSelect={select} />
    {error ? <div className="wf-alert" role="alert">{error}</div> : null}
    {drawerOpen ? <FolderDrawer onClose={() => setDrawerOpen(false)} onCreate={createFolder}><FolderTree {...treeProps} /></FolderDrawer> : null}
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
  onFocusedKeyChange(key: string): void;
  onToggle(folder: WorkflowFolder): void;
  onSelect(selection: WorkflowFolderSelection): void;
  onLoadMore(parentId?: string): void;
  onRetry(parentId?: string): void;
}

function FolderTree(props: FolderTreeProps) {
  const {
    roots, children, continuations, expanded, loadingKeys, loadFailures, selection, focusedKey,
    onFocusedKeyChange, onToggle, onSelect, onLoadMore, onRetry
  } = props;
  const active = selectedFolderId(selection);
  const treeId = useId().replaceAll(":", "");

  const focusItem = (item: HTMLElement | null | undefined) => {
    if (!item) return;
    onFocusedKeyChange(item.dataset.treeKey ?? "all");
    item.focus();
  };

  const activate = (item: HTMLElement) => {
    const kind = item.dataset.kind;
    if (kind === "folder") onSelect({ id: item.dataset.folderId! });
    else if (kind === "selection") onSelect(item.dataset.selection as "all" | "unfiled");
    else item.click();
  };

  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    const current = (event.target as HTMLElement).closest<HTMLElement>('[role="treeitem"]');
    if (!current || !event.currentTarget.contains(current)) return;
    const items = [...event.currentTarget.querySelectorAll<HTMLElement>('[role="treeitem"]')];
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
      if (!expanded.has(folder.id)) void onToggle(folder);
      else focusItem(current.querySelector<HTMLElement>(':scope > [role="group"] [role="treeitem"]'));
      return;
    }
    if (event.key === "ArrowLeft") {
      event.preventDefault();
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
      onFocus={() => onFocusedKeyChange(value)}
      onClick={() => onSelect(value)}
    >{label}</div>
  );

  const actionRow = (parentId: string | undefined, kind: "load-more" | "retry", label: string) => {
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
        onClick={() => {
          if (!loading) void (kind === "retry" ? onRetry(parentId) : onLoadMore(parentId));
        }}
      >{loading ? "Loading folders…" : label}</button>
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
            onFocus={() => onFocusedKeyChange(key)}
            onClick={() => onSelect({ id: folder.id })}
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
      {loading && items.length === 0 ? <span className="wf-folder-loading" role="status">Loading folders…</span> : null}
      {loadFailures[parentKey] ? (
        <div className="wf-folder-load-error" role="none">
          <span role="alert">Couldn't load folders. {loadFailures[parentKey]}</span>
          {actionRow(parentId, "retry", "Retry loading folders")}
        </div>
      ) : continuations[parentKey] ? actionRow(parentId, "load-more", "Load more folders") : null}
    </>;
  };

  return (
    <div role="tree" aria-label="Workflow folders" aria-busy={loadingKeys.has(rootKey)} className="wf-folder-tree" onKeyDown={onKeyDown}>
      {selectionRow("All workflows", "all")}
      {selectionRow("Unfiled", "unfiled")}
      {folderRows(roots, 0)}
    </div>
  );
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
