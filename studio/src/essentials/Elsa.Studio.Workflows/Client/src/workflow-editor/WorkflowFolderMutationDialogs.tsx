import { StudioAlert, StudioButton, StudioDialog, StudioDialogFooter, StudioField } from "@elsa-workflows/studio-ui";
import { ChevronDown, ChevronRight, Folder, LoaderCircle } from "lucide-react";
import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState, type FormEvent, type ReactNode } from "react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { getWorkflowFolder, moveWorkflowFolder, renameWorkflowFolder } from "../api/workflowDesign";
import type { WorkflowFolder } from "../workflowTypes";
import { useNotificationGuard } from "./notifications";
import { useWorkflowFolderTree, workflowFolderTreeKey } from "./useWorkflowFolderTree";

export function RenameWorkflowFolderDialog({ context, folder, onClose, onRenamed }: {
  context: StudioEndpointContext;
  folder: WorkflowFolder;
  onClose(restoreFocus?: boolean): void;
  onRenamed(canNotify: () => boolean): Promise<void> | void;
}) {
  const initialFocusRef = useRef<HTMLInputElement>(null);
  const [name, setName] = useState(folder.name);
  const [expectedRevision, setExpectedRevision] = useState(folder.revision);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const mutationScope = useMemo(() => ({ context, folderId: folder.id }), [context, folder.id]);
  const captureMutation = useNotificationGuard(mutationScope);
  const busyScope = useRef(mutationScope);
  useLayoutEffect(() => {
    if (busyScope.current !== mutationScope) { busyScope.current = mutationScope; setBusy(false); }
  }, [mutationScope]);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    const normalized = name.trim();
    if (!normalized || busy) return;
    setBusy(true);
    setError(null);
    const isCurrent = captureMutation();
    try {
      await renameWorkflowFolder(context, folder.id, normalized, expectedRevision);
      if (!isCurrent.isOwned()) return;
      await onRenamed(isCurrent);
      if (isCurrent.isOwned()) onClose(isCurrent());
    } catch (caught) {
      if (isCurrent.isOwned()) setError(`Couldn't rename this folder. ${caught instanceof Error ? caught.message : "The server rejected the name."} Correct the name or permissions, then try again.`);
    } finally {
      if (isCurrent.isOwned()) setBusy(false);
    }
  };
  const reload = async () => {
    setBusy(true); setError(null);
    try { const current = await getWorkflowFolder(context, folder.id); if (!current) throw new Error("Folder unavailable"); setName(current.folder.name); setExpectedRevision(current.folder.revision); }
    catch (caught) { setError(`Couldn't reload the folder. ${caught instanceof Error ? caught.message : "Try again."}`); }
    finally { setBusy(false); }
  };

  return <StudioDialog
    open
    title="Rename folder"
    description="The folder keeps its existing identity and contents."
    className="wf-tokens"
    onClose={onClose}
    initialFocusRef={initialFocusRef}
    dismissDisabled={busy}
    dismissOnOutsideClick={false}
    onSubmit={submit}
    footer={<StudioDialogFooter
      onCancel={() => onClose()}
      cancelDisabled={busy}
      confirmDisabled={busy || !name.trim()}
      confirmLabel={busy ? "Renaming…" : "Rename"}
    />}
  >
    <div className="wf-folder-mutation-content">
      {error ? <StudioAlert tone="danger">{error} <StudioButton disabled={busy} onClick={() => void reload()}>Reload folder</StudioButton></StudioAlert> : null}
      <StudioField className="wf-form-field" label={<><span>Name</span></>}><input ref={initialFocusRef} aria-label="Folder name" value={name} onChange={event => setName(event.target.value)} disabled={busy} /></StudioField>
    </div>
  </StudioDialog>;
}

type Eligibility = "eligible" | "descendant";

export function MoveWorkflowFolderDialog({ context, folder, onClose, onMoved }: {
  context: StudioEndpointContext;
  folder: WorkflowFolder;
  onClose(restoreFocus?: boolean): void;
  onMoved(parentId: string | null, previousParentId: string | null, canNotify: () => boolean): Promise<void> | void;
}) {
  const initialFocusRef = useRef<HTMLInputElement>(null);
  const descendantIds = useRef(new Set([folder.id]));
  const previousParentId = useRef(folder.parentId ?? null);
  const [expanded, setExpanded] = useState<Set<string>>(() => new Set());
  const [eligibility, setEligibility] = useState<Record<string, Eligibility>>({});
  const [destinationId, setDestinationId] = useState<string | null>(folder.parentId ?? null);
  const [expectedRevision, setExpectedRevision] = useState(folder.revision);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const mutationScope = useMemo(() => ({ context, folderId: folder.id }), [context, folder.id]);
  const captureMutation = useNotificationGuard(mutationScope);
  const busyScope = useRef(mutationScope);
  useLayoutEffect(() => {
    if (busyScope.current !== mutationScope) { busyScope.current = mutationScope; setBusy(false); }
  }, [mutationScope]);

  const classify = useCallback((items: WorkflowFolder[], parentId?: string) => {
    const discoveredDescendants = new Set(descendantIds.current);
    if (parentId && discoveredDescendants.has(parentId)) {
      for (const item of items) discoveredDescendants.add(item.id);
    } else {
      let changed = true;
      while (changed) {
        changed = false;
        for (const item of items) {
          if (!discoveredDescendants.has(item.id) && item.parentId && discoveredDescendants.has(item.parentId)) {
            discoveredDescendants.add(item.id);
            changed = true;
          }
        }
      }
    }
    const classified = Object.fromEntries(items.map(item => {
      const state: Eligibility = discoveredDescendants.has(item.id) ? "descendant" : "eligible";
      if (state === "descendant") descendantIds.current.add(item.id);
      return [item.id, state];
    }));
    setEligibility(current => ({ ...current, ...classified }));
  }, []);

  const { roots, children, continuations, loadedKeys, loadingKeys, loadFailures: loadErrors, loadPage } = useWorkflowFolderTree({
    context,
    unavailableMessage: "Workflow folders are unavailable.",
    onPageLoaded: classify
  });

  useEffect(() => { void loadPage(); }, [loadPage]);

  const toggle = async (candidate: WorkflowFolder) => {
    if (expanded.has(candidate.id)) {
      setExpanded(current => { const next = new Set(current); next.delete(candidate.id); return next; });
      return;
    }
    setExpanded(current => new Set(current).add(candidate.id));
    if (!loadedKeys.has(candidate.id)) await loadPage(candidate.id);
  };

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (busy) return;
    setBusy(true);
    setError(null);
    const isCurrent = captureMutation();
    try {
      await moveWorkflowFolder(context, folder.id, destinationId, expectedRevision);
      if (!isCurrent.isOwned()) return;
      await onMoved(destinationId, previousParentId.current, isCurrent);
      if (isCurrent.isOwned()) onClose(isCurrent());
    } catch (caught) {
      if (isCurrent.isOwned()) setError(`Couldn't move this folder. ${caught instanceof Error ? caught.message : "The server rejected the destination."} Choose another available destination or check permissions, then try again.`);
    } finally {
      if (isCurrent.isOwned()) setBusy(false);
    }
  };

  const controls = (parentId?: string) => {
    const key = workflowFolderTreeKey(parentId);
    if (loadingKeys.has(key)) return <p role="status" className="wf-folder-loading"><LoaderCircle size={14} /> Loading folders…</p>;
    if (loadErrors.has(key)) return <p role="alert" className="wf-folder-load-error">{loadErrors.get(key)} <StudioButton onClick={() => void loadPage(parentId)}>Retry loading folders</StudioButton></p>;
    if (continuations.get(key)) return <StudioButton className="wf-folder-load-more" onClick={() => void loadPage(parentId, continuations.get(key), { append: true })}>Load more folders</StudioButton>;
    return null;
  };
  const reload = async () => {
    setBusy(true); setError(null);
    try {
      const current = await getWorkflowFolder(context, folder.id);
      if (!current) throw new Error("Folder unavailable");
      setExpectedRevision(current.folder.revision); previousParentId.current = current.folder.parentId ?? null;
      await loadPage(undefined, undefined, { force: true });
    } catch (caught) { setError(`Couldn't reload the folder. ${caught instanceof Error ? caught.message : "Try again."}`); }
    finally { setBusy(false); }
  };

  const rows = (items: WorkflowFolder[]): ReactNode => items.map(candidate => {
    const state = eligibility[candidate.id] ?? "checking";
    if (state !== "eligible") return null;
    const open = expanded.has(candidate.id);
    return <div className="wf-move-folder-node" key={candidate.id}>
      <div className="wf-move-folder-row">
        <StudioButton className="wf-move-folder-expand" aria-label={`${open ? "Collapse" : "Expand"} ${candidate.name}`} aria-expanded={open} onClick={() => void toggle(candidate)}>{open ? <ChevronDown size={15} /> : <ChevronRight size={15} />}</StudioButton>
        <label className="wf-move-folder-row">
          <input type="radio" name="folder-parent" value={candidate.id} checked={destinationId === candidate.id} disabled={busy} onChange={() => setDestinationId(candidate.id)} />
          <Folder size={15} /> {candidate.name}
        </label>
      </div>
      {open ? <div className="wf-move-folder-children">{rows(children.get(candidate.id) ?? [])}{controls(candidate.id)}</div> : null}
    </div>;
  });

  return <StudioDialog
    open
    title="Move folder"
    description={<>Choose a new parent for {folder.name}. The folder keeps its identity.</>}
    className="wf-tokens"
    onClose={onClose}
    initialFocusRef={initialFocusRef}
    dismissDisabled={busy}
    dismissOnOutsideClick={false}
    onSubmit={submit}
    footer={<StudioDialogFooter
      onCancel={() => onClose()}
      cancelDisabled={busy}
      confirmDisabled={busy || (destinationId !== null && eligibility[destinationId] !== "eligible")}
      confirmLabel={busy ? "Moving…" : "Move"}
    />}
  >
    <div className="wf-folder-mutation-content">
      {error ? <StudioAlert tone="danger">{error} <StudioButton disabled={busy} onClick={() => void reload()}>Reload folder</StudioButton></StudioAlert> : null}
      <fieldset className="wf-move-destination" disabled={busy}><legend>New parent</legend><div className="wf-move-folder-list">
        <label className="wf-move-folder-row"><input ref={initialFocusRef} type="radio" name="folder-parent" checked={destinationId === null} onChange={() => setDestinationId(null)} /> Top level</label>
        {rows(roots)}{controls()}
      </div></fieldset>
    </div>
  </StudioDialog>;
}
