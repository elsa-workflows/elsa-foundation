import { ChevronDown, ChevronRight, Folder, FolderPen, FolderPlus, Inbox, LayoutList, Menu, Move, PanelLeftClose, PanelLeftOpen, Trash2, X } from "lucide-react";
import { lazy, Suspense, useEffect, useId, useMemo, useRef, useState, type KeyboardEvent, type MutableRefObject, type ReactNode } from "react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { StudioButton } from "@elsa-workflows/studio-ui";
import { createWorkflowFolder, deleteEmptyWorkflowFolder, getWorkflowFolder, getWorkflowFolderMutationSupport, isWorkflowFolderBrowsingAvailable } from "../api/workflowDesign";
import type { WorkflowFolder } from "../workflowTypes";
import { WorkflowFolderDeletionDialog } from "./WorkflowFolderDeletionDialog";
import { notifySuccess, useNotificationGuard } from "./notifications";
import { getDialogs } from "./dialogs";
import { useDialogFocus } from "./useDialogFocus";
import { useWorkflowFolderTree, workflowFolderRootKey, workflowFolderTreeKey } from "./useWorkflowFolderTree";

const RenameWorkflowFolderDialog = lazy(() => import("./WorkflowFolderMutationDialogs").then(module => ({ default: module.RenameWorkflowFolderDialog })));
const MoveWorkflowFolderDialog = lazy(() => import("./WorkflowFolderMutationDialogs").then(module => ({ default: module.MoveWorkflowFolderDialog })));

export type WorkflowFolderSelection = "all" | "unfiled" | { id: string };

export function selectedFolderId(selection: WorkflowFolderSelection) {
  return typeof selection === "object" ? selection.id : null;
}

export function WorkflowFolderNavigation({ context, selection, onSelect, onAvailable, onFoldersMutated, canManage = true, children: browseContent }: {
  context: StudioEndpointContext;
  selection: WorkflowFolderSelection;
  onSelect(selection: WorkflowFolderSelection): void;
  onAvailable(available: boolean): void;
  onFoldersMutated?(selection?: WorkflowFolderSelection): Promise<void> | void;
  canManage?: boolean;
  children?: ReactNode;
}) {
  const captureNotificationGuard = useNotificationGuard(context);
  const [available, setAvailable] = useState<boolean | null>(null);
  const [expanded, setExpanded] = useState<Set<string>>(() => new Set());
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [managerOpen, setManagerOpen] = useState(false);
  const manageButtonRef = useRef<HTMLButtonElement>(null);
  const [collapsed, setCollapsed] = useState(false);
  const folderTreeId = useId();
  const [crumbs, setCrumbs] = useState<WorkflowFolder[]>([]);
  const [breadcrumbState, setBreadcrumbState] = useState<"idle" | "loading" | "ready" | "failed">("idle");
  const [breadcrumbError, setBreadcrumbError] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [focusedKey, setFocusedKey] = useState("all");
  const [mutationSupport, setMutationSupport] = useState({ rename: false, move: false, deleteEmpty: false, deleteContents: false });
  const [mutationDialog, setMutationDialog] = useState<"rename" | "move" | "delete" | null>(null);
  const [localRefreshKey, setLocalRefreshKey] = useState(0);
  const [pendingChildFocusId, setPendingChildFocusId] = useState<string | null>(null);
  const keyboardExpansionIntents = useRef(new Set<string>());
  const navigationRef = useRef<HTMLElement>(null);
  const folderPickerButtonRef = useRef<HTMLButtonElement>(null);
  const folderToggleButtonRef = useRef<HTMLButtonElement>(null);
  const renameButtonRef = useRef<HTMLButtonElement>(null);
  const deleteButtonRef = useRef<HTMLButtonElement>(null);
  const moveButtonRef = useRef<HTMLButtonElement>(null);
  const selectedId = selectedFolderId(selection);
  const dialogOwner = useMemo(() => ({ context, selectedId, mutationDialog }), [context, selectedId, mutationDialog]);
  const currentDialogOwner = useRef(dialogOwner); currentDialogOwner.current = dialogOwner;
  const breadcrumbIdentity = `${context.baseUrl}\u0000${selectedId ?? ""}`;
  const breadcrumbIdentityRef = useRef<string | null>(null);
  const breadcrumbContextRef = useRef(context);
  const {
    roots,
    children,
    continuations,
    loadingKeys,
    loadFailures,
    loadPage: loadFolderPage,
    invalidatePages
  } = useWorkflowFolderTree({ context });

  useEffect(() => {
    let cancelled = false;
    void isWorkflowFolderBrowsingAvailable(context).then(enabled => {
      if (cancelled) return;
      setAvailable(enabled);
      onAvailable(enabled);
      if (enabled) void loadFolderPage();
    }).catch(() => {
      if (cancelled) return;
      setAvailable(false);
      onAvailable(false);
      setError("Couldn't inspect workflow folder support. Refresh the page to retry.");
    });
    return () => { cancelled = true; };
  }, [context, loadFolderPage, onAvailable]);

  useEffect(() => {
    let cancelled = false;
    void getWorkflowFolderMutationSupport(context)
      .then(support => { if (!cancelled) setMutationSupport(support); })
      .catch(() => { if (!cancelled) setMutationSupport({ rename: false, move: false, deleteEmpty: false, deleteContents: false }); });
    return () => { cancelled = true; };
  }, [context]);

  useEffect(() => {
    const id = selectedFolderId(selection);
    const identityChanged = breadcrumbIdentityRef.current !== breadcrumbIdentity || breadcrumbContextRef.current !== context;
    breadcrumbIdentityRef.current = breadcrumbIdentity;
    breadcrumbContextRef.current = context;
    if (identityChanged || !available) {
      setCrumbs([]);
      setBreadcrumbError(null);
    }
    if (!id || !available) {
      setBreadcrumbState("idle");
      return;
    }
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
  }, [available, breadcrumbIdentity, context, localRefreshKey, selection]);

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
    if (!children.has(folder.id)) await loadFolderPage(folder.id);
  };

  const select = (next: WorkflowFolderSelection) => {
    setError(null);
    setFocusedKey(typeof next === "object" ? `folder:${next.id}` : next);
    onSelect(next);
    if (!managerOpen) setDrawerOpen(false);
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
      await loadFolderPage(parentId ?? undefined, undefined, { force: true });
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "Couldn't create the folder.");
    }
  };

  const breadcrumbContextCurrent = breadcrumbContextRef.current === context && breadcrumbIdentityRef.current === breadcrumbIdentity;
  const selectedFolder = selectedId && breadcrumbContextCurrent
    ? crumbs.at(-1) ?? findFolder(roots, children, selectedId)
    : undefined;

  const refreshAfterStructureChange = async (
    affectedParentIds: ReadonlyArray<string | null>,
    nextSelection?: WorkflowFolderSelection,
    invalidatedFolderId?: string
  ) => {
    const parents = [...new Set(affectedParentIds)];
    if (invalidatedFolderId) invalidatePages([invalidatedFolderId]);
    for (const parentId of parents) {
      if (parentId !== null && !expanded.has(parentId)) {
        invalidatePages([parentId]);
      } else {
        await loadFolderPage(parentId ?? undefined, undefined, { force: true });
      }
    }
    setLocalRefreshKey(current => current + 1);
    // Deleting the selected folder supplies its survivor; changing selection already reloads definitions.
    if (nextSelection === undefined) await onFoldersMutated?.();
  };

  const restoreFolderFocus = (key: string) => {
    requestAnimationFrame(() => {
      if (window.matchMedia?.("(max-width: 700px)").matches) {
        folderPickerButtonRef.current?.focus();
        return;
      }
      if (navigationRef.current?.dataset.collapsed === "true") {
        folderToggleButtonRef.current?.focus();
        return;
      }
      (findTreeItemByKey(navigationRef.current, key) ?? findTreeItemByKey(navigationRef.current, "all") ?? manageButtonRef.current)?.focus();
    });
  };

  const closeMutationDialog = (action: "rename" | "move" | "delete", restoreFocus = true) => {
    if (currentDialogOwner.current !== dialogOwner) return;
    setMutationDialog(null);
    if (restoreFocus) requestAnimationFrame(() => ((action === "rename" ? renameButtonRef.current : action === "move" ? moveButtonRef.current : deleteButtonRef.current) ?? manageButtonRef.current)?.focus());
  };

  const deleteFolder = async () => {
    if (!selectedFolder) return;
    const canNotify = captureNotificationGuard();
    const confirmed = await getDialogs().confirm({
      message: `Delete folder "${selectedFolder.name}"? Only empty folders can be deleted by this backend. This cannot be undone.`,
      confirmLabel: "Delete folder",
      tone: "danger"
    });
    if (!confirmed || !canNotify.isOwned() || currentDialogOwner.current !== dialogOwner) return;
    setError(null);
    try {
      await deleteEmptyWorkflowFolder(context, selectedFolder.id, selectedFolder.revision);
      if (!canNotify.isOwned()) return;
      // The accepted delete still reconciles its owning tab while hidden; only transient feedback is visit-bound.
      const survivor: WorkflowFolderSelection = selectedFolder.parentId ? { id: selectedFolder.parentId } : "all";
      const survivorKey = selectedFolder.parentId ? `folder:${selectedFolder.parentId}` : "all";
      select(survivor);
      setFocusedKey(survivorKey);
      await refreshAfterStructureChange([selectedFolder.parentId ?? null], survivor, selectedFolder.id);
      if (canNotify()) notifySuccess(`Deleted folder ${selectedFolder.name}`);
      if (canNotify()) restoreFolderFocus(survivorKey);
    } catch (caught) {
      if (canNotify.isOwned()) setError(`Couldn't delete this folder. ${caught instanceof Error ? caught.message : "The server rejected the request."} Ensure it is empty and you have permission, then try again.`);
    }
  };

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
      const key = workflowFolderTreeKey(parentId);
      if (await loadFolderPage(parentId, continuations.get(key), { append: true })) {
        setFocusedKey(parentId ? `folder:${parentId}` : "all");
      }
    },
    onRetry: async (parentId?: string) => {
      if (await loadFolderPage(parentId)) {
        setFocusedKey(parentId ? `folder:${parentId}` : "all");
      }
    }
  };

  const closeDrawer = () => { setDrawerOpen(false); setManagerOpen(false); };
  const folderActions = canManage && selectedFolder && (mutationSupport.rename || mutationSupport.move || mutationSupport.deleteEmpty || mutationSupport.deleteContents) ? (
    <div className="wf-folder-actions" role="group" aria-label={`Actions for ${selectedFolder.name}`}>
      {mutationSupport.rename ? <StudioButton ref={renameButtonRef} onClick={() => { closeDrawer(); setMutationDialog("rename"); }}><FolderPen size={14} aria-hidden="true" /> Rename</StudioButton> : null}
      {mutationSupport.move ? <StudioButton ref={moveButtonRef} onClick={() => { closeDrawer(); setMutationDialog("move"); }}><Move size={14} aria-hidden="true" /> Move</StudioButton> : null}
      {mutationSupport.deleteEmpty || mutationSupport.deleteContents ? <StudioButton ref={deleteButtonRef} className="wf-delete-action" onClick={() => { closeDrawer(); if (mutationSupport.deleteContents) setMutationDialog("delete"); else void deleteFolder(); }}><Trash2 size={14} aria-hidden="true" /> Delete</StudioButton> : null}
    </div>
  ) : null;

  return <>
    <div className="wf-folder-browser" data-folders-available={available ? "true" : undefined} data-folders-collapsed={collapsed ? "true" : undefined}>
      {available ? <div className="wf-folder-navigation">
        <div className="wf-folder-mobile-actions">
          <StudioButton ref={folderPickerButtonRef} onClick={() => setDrawerOpen(true)} aria-expanded={drawerOpen} aria-controls="workflow-folder-picker"><Menu size={15} aria-hidden="true" /> Folders</StudioButton>
        </div>
        {!drawerOpen ? (
          <aside ref={navigationRef} className="wf-folder-nav" aria-label="Workflow folders" data-collapsed={collapsed ? "true" : undefined}>
            <div className="wf-folder-nav-heading">
              <strong hidden={collapsed}>Folders</strong>
              <div className="wf-folder-nav-actions">
                {canManage && !collapsed ? <button type="button" title="Create folder" aria-label="Create folder" onClick={() => void createFolder()}><FolderPlus size={16} /></button> : null}
                <button ref={folderToggleButtonRef} type="button" title={collapsed ? "Expand folders" : "Collapse folders"} aria-label={collapsed ? "Expand folders" : "Collapse folders"}
                  aria-expanded={!collapsed} aria-controls={folderTreeId} onClick={() => setCollapsed(current => !current)}>
                  {collapsed ? <PanelLeftOpen size={16} aria-hidden="true" /> : <PanelLeftClose size={16} aria-hidden="true" />}
                </button>
              </div>
            </div>
            <div id={folderTreeId} hidden={collapsed}><FolderTree {...treeProps} /></div>
          </aside>
        ) : null}
      </div> : null}
      <div className="wf-folder-content">
        {available ? <>
          <div className="wf-folder-content-heading">
          <WorkflowFolderBreadcrumb crumbs={crumbs} state={breadcrumbState} error={breadcrumbError} selection={selection} onSelect={select} />
          {canManage ? <StudioButton ref={manageButtonRef} aria-haspopup="dialog" onClick={() => { setManagerOpen(true); setDrawerOpen(true); }}><FolderPen size={15} aria-hidden="true" /> Manage folders</StudioButton> : null}
          </div>
          {!managerOpen ? folderActions : null}
        </> : null}
        {error ? <div className="wf-alert" role="alert">{error}</div> : null}
        {browseContent}
      </div>
    </div>
    {available && drawerOpen ? <FolderDrawer managing={managerOpen} onClose={closeDrawer} onCreate={canManage ? async () => { closeDrawer(); await createFolder(); } : undefined}>
      <FolderTree {...treeProps} />
      {managerOpen ? <div className="wf-folder-management">
        <p>{selectedFolder ? `Selected folder: ${selectedFolder.name}` : "Select a folder to rename, move, or delete it. Only empty folders can be deleted."}</p>
        {folderActions}
      </div> : null}
    </FolderDrawer> : null}
    <Suspense fallback={<p role="status">Loading folder actions…</p>}>
    {available && mutationDialog === "rename" && selectedFolder ? <RenameWorkflowFolderDialog
      context={context}
      folder={selectedFolder}
      onClose={restoreFocus => closeMutationDialog("rename", restoreFocus)}
      onRenamed={async canNotify => {
        if (currentDialogOwner.current !== dialogOwner) return;
        await refreshAfterStructureChange([selectedFolder.parentId ?? null]);
        if (canNotify()) notifySuccess("Folder renamed");
      }}
    /> : null}
    {available && mutationDialog === "move" && selectedFolder ? <MoveWorkflowFolderDialog
      context={context}
      folder={selectedFolder}
      onClose={restoreFocus => closeMutationDialog("move", restoreFocus)}
      onMoved={async (parentId, previousParentId, canNotify) => {
        if (currentDialogOwner.current !== dialogOwner) return;
        await refreshAfterStructureChange([previousParentId, parentId]);
        if (canNotify()) notifySuccess("Folder moved");
      }}
    /> : null}
    </Suspense>
    {available && mutationDialog === "delete" && selectedFolder ? <WorkflowFolderDeletionDialog
      key={`${context.baseUrl}:${selectedFolder.id}`}
      context={context}
      folder={selectedFolder}
      onClose={() => closeMutationDialog("delete")}
      onDeleted={async preview => {
        const canNotify = captureNotificationGuard();
        setMutationDialog(null);
        const parentId = preview.parent?.id ?? null;
        const survivor: WorkflowFolderSelection = parentId ? { id: parentId } : "unfiled";
        const survivorKey = parentId ? `folder:${parentId}` : "unfiled";
        select(survivor);
        setFocusedKey(survivorKey);
        await refreshAfterStructureChange([parentId], survivor, preview.folder.id);
        if (canNotify()) notifySuccess(`Deleted folder ${preview.folder.name}`);
        restoreFolderFocus(survivorKey);
      }}
    /> : null}
  </>;
}

interface FolderTreeProps {
  roots: WorkflowFolder[];
  children: ReadonlyMap<string, WorkflowFolder[]>;
  continuations: ReadonlyMap<string, string | null>;
  expanded: Set<string>;
  loadingKeys: Set<string>;
  loadFailures: ReadonlyMap<string, string>;
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

function findTreeItemByKey(root: ParentNode | null | undefined, key: string) {
  return [...(root?.querySelectorAll<HTMLElement>("[data-tree-key]") ?? [])]
    .find(item => item.dataset.treeKey === key);
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
  const groupIds = useRef(new Map<string, string>());
  const nextGroupId = useRef(0);

  const groupIdFor = (folderId: string) => {
    const existing = groupIds.current.get(folderId);
    if (existing) return existing;
    const id = `${treeId}-children-${nextGroupId.current++}`;
    groupIds.current.set(folderId, id);
    return id;
  };

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
    const pendingKey = workflowFolderTreeKey(pendingChildFocusId);
    if (loadFailures.has(pendingKey) || (children.has(pendingChildFocusId) && !loadingKeys.has(pendingKey))) {
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
    >{value === "all" ? <LayoutList size={14} aria-hidden="true" /> : <Inbox size={14} aria-hidden="true" />}<span>{label}</span></div>
  );

  const actionRow = (parentId: string | undefined, kind: "load-more" | "retry", label: string, announcement?: string) => {
    const parentKey = workflowFolderTreeKey(parentId);
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
          requestAnimationFrame(() => findTreeItemByKey(tree, survivorKey)?.focus());
          void (kind === "retry" ? onRetry(parentId) : onLoadMore(parentId));
        }}
      >
        {announcement ? <span role="alert">{announcement} </span> : null}
        {loading ? "Loading folders…" : label}
      </button>
    );
  };

  const folderRows = (items: WorkflowFolder[], level: number, parentId?: string): ReactNode => {
    const parentKey = workflowFolderTreeKey(parentId);
    const loading = loadingKeys.has(parentKey);
    // Preserve room for controls and a truncated name even at the supported 16 levels.
    const indentation = `clamp(0px, ${level * 14}px, calc(100% - 112px))`;
    return <>
      {items.map(folder => {
        const key = `folder:${folder.id}`;
        const groupId = groupIdFor(folder.id);
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
            <div className="wf-folder-tree-folder" style={{ paddingInlineStart: indentation }}>
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
              <span className="wf-folder-tree-row"><Folder size={14} /><span className="wf-folder-tree-label" title={folder.name}>{folder.name}</span></span>
            </div>
            {expanded.has(folder.id) ? (
              <div id={groupId} role="group" aria-busy={loadingKeys.has(workflowFolderTreeKey(folder.id))}>
                {folderRows(children.get(folder.id) ?? [], level + 1, folder.id)}
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
      {loadFailures.has(parentKey) ? (
        actionRow(parentId, "retry", "Retry loading folders", `Couldn't load folders. ${loadFailures.get(parentKey)}`)
      ) : continuations.get(parentKey) ? actionRow(parentId, "load-more", "Load more folders") : null}
    </>;
  };

  return (
    <div
      ref={treeRef}
      role="tree"
      aria-label="Workflow folders"
      aria-busy={loadingKeys.has(workflowFolderRootKey)}
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

function findFolder(roots: WorkflowFolder[], children: ReadonlyMap<string, WorkflowFolder[]>, id: string): WorkflowFolder | undefined {
  return [...roots, ...[...children.values()].flat()].find(folder => folder.id === id);
}

function FolderDrawer({ children, onClose, onCreate, managing = false }: { children: ReactNode; onClose(): void; onCreate?: () => void; managing?: boolean }) {
  const ref = useRef<HTMLElement | null>(null);
  const id = useId();
  useDialogFocus(ref, onClose);
  return <div className="wf-folder-drawer-backdrop" role="presentation"><aside ref={ref} id="workflow-folder-picker" className="wf-folder-drawer" role="dialog" aria-modal="true" aria-labelledby={id}><div><strong id={id}>{managing ? "Manage workflow folders" : "Workflow folders"}</strong><button type="button" aria-label="Close folder picker" onClick={onClose}><X size={16} aria-hidden="true" /></button></div>
    {onCreate ? <StudioButton aria-label="Create folder" onClick={() => void onCreate()}><FolderPlus size={16} aria-hidden="true" /> Create folder</StudioButton> : null}
    {children}
  </aside></div>;
}
