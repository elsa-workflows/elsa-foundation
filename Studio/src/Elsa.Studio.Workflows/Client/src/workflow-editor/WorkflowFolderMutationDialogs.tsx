import { ChevronDown, ChevronRight, Folder, LoaderCircle } from "lucide-react";
import { useCallback, useEffect, useId, useRef, useState, type FormEvent, type ReactNode } from "react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { getWorkflowFolder, listWorkflowFolders, moveWorkflowFolder, renameWorkflowFolder } from "../api/workflowDesign";
import type { WorkflowFolder } from "../workflowTypes";
import { useDialogFocus } from "./useDialogFocus";

export function RenameWorkflowFolderDialog({ context, folder, onClose, onRenamed }: {
  context: StudioEndpointContext;
  folder: WorkflowFolder;
  onClose(): void;
  onRenamed(): Promise<void> | void;
}) {
  const titleId = useId();
  const ref = useRef<HTMLElement>(null);
  const [name, setName] = useState(folder.name);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  useDialogFocus(ref, busy ? null : onClose);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    const normalized = name.trim();
    if (!normalized || busy) return;
    setBusy(true);
    setError(null);
    try {
      await renameWorkflowFolder(context, folder.id, normalized);
      await onRenamed();
      onClose();
    } catch (caught) {
      setError(`Couldn't rename this folder. ${caught instanceof Error ? caught.message : "The server rejected the name."} Correct the name or permissions, then try again.`);
    } finally {
      setBusy(false);
    }
  };

  return <div className="wf-dialog-backdrop" role="presentation">
    <section ref={ref} className="wf-dialog wf-folder-mutation-dialog" role="dialog" aria-modal="true" aria-labelledby={titleId} tabIndex={-1}>
      <form onSubmit={submit}>
        <div className="wf-dialog-heading"><div><h3 id={titleId}>Rename folder</h3><p>The folder keeps its existing identity and contents.</p></div></div>
        <div className="wf-folder-mutation-content">
          {error ? <p className="wf-dialog-error" role="alert">{error}</p> : null}
          <label className="wf-form-field"><span>Name</span><input aria-label="Folder name" value={name} onChange={event => setName(event.target.value)} disabled={busy} /></label>
        </div>
        <div className="wf-dialog-actions"><button type="button" onClick={onClose} disabled={busy}>Cancel</button><button type="submit" disabled={busy || !name.trim()}>{busy ? "Renaming…" : "Rename"}</button></div>
      </form>
    </section>
  </div>;
}

type Eligibility = "checking" | "eligible" | "descendant" | "unavailable";

export function MoveWorkflowFolderDialog({ context, folder, onClose, onMoved }: {
  context: StudioEndpointContext;
  folder: WorkflowFolder;
  onClose(): void;
  onMoved(parentId: string | null): Promise<void> | void;
}) {
  const titleId = useId();
  const ref = useRef<HTMLElement>(null);
  const pendingKeys = useRef(new Set<string>());
  const classificationGenerations = useRef<Record<string, number>>({});
  const [roots, setRoots] = useState<WorkflowFolder[]>([]);
  const [children, setChildren] = useState<Record<string, WorkflowFolder[]>>({});
  const [continuations, setContinuations] = useState<Record<string, string | null>>({});
  const [expanded, setExpanded] = useState<Set<string>>(() => new Set());
  const [eligibility, setEligibility] = useState<Record<string, Eligibility>>({});
  const [loadingKeys, setLoadingKeys] = useState<Set<string>>(() => new Set());
  const [loadErrors, setLoadErrors] = useState<Record<string, string>>({});
  const [destinationId, setDestinationId] = useState<string | null>(folder.parentId ?? null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  useDialogFocus(ref, busy ? null : onClose);

  const classify = useCallback(async (items: WorkflowFolder[]) => {
    setEligibility(current => ({
      ...current,
      ...Object.fromEntries(items.map(item => [item.id, item.id === folder.id ? "descendant" : "checking"]))
    }));
    await Promise.all(items.filter(item => item.id !== folder.id).map(async item => {
      const generation = (classificationGenerations.current[item.id] ?? 0) + 1;
      classificationGenerations.current[item.id] = generation;
      try {
        const detail = await getWorkflowFolder(context, item.id);
        if (!detail) throw new Error("Folder detail is unavailable.");
        const state: Eligibility = detail.ancestors.some(ancestor => ancestor.id === folder.id) ? "descendant" : "eligible";
        if (classificationGenerations.current[item.id] === generation) {
          setEligibility(current => ({ ...current, [item.id]: state }));
        }
      } catch {
        if (classificationGenerations.current[item.id] === generation) {
          setEligibility(current => ({ ...current, [item.id]: "unavailable" }));
        }
      }
    }));
  }, [context, folder.id]);

  const loadPage = useCallback(async (parentId?: string, token?: string | null, append = false) => {
    const key = parentId ?? "root";
    if (pendingKeys.current.has(key)) return;
    pendingKeys.current.add(key);
    setLoadingKeys(current => new Set(current).add(key));
    setLoadErrors(current => { const next = { ...current }; delete next[key]; return next; });
    try {
      const page = await listWorkflowFolders(context, { parentId, continuationToken: token });
      if (!page) throw new Error("Workflow folders are unavailable.");
      if (parentId) setChildren(current => ({ ...current, [parentId]: append ? [...(current[parentId] ?? []), ...page.items] : page.items }));
      else setRoots(current => append ? [...current, ...page.items] : page.items);
      setContinuations(current => ({ ...current, [key]: page.nextContinuationToken }));
      await classify(page.items);
    } catch (caught) {
      setLoadErrors(current => ({ ...current, [key]: caught instanceof Error ? caught.message : "Couldn't load destinations." }));
    } finally {
      pendingKeys.current.delete(key);
      setLoadingKeys(current => { const next = new Set(current); next.delete(key); return next; });
    }
  }, [classify, context]);

  useEffect(() => { void loadPage(); }, [loadPage]);

  const toggle = async (candidate: WorkflowFolder) => {
    if (expanded.has(candidate.id)) {
      setExpanded(current => { const next = new Set(current); next.delete(candidate.id); return next; });
      return;
    }
    setExpanded(current => new Set(current).add(candidate.id));
    if (!(candidate.id in children)) await loadPage(candidate.id);
  };

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (busy) return;
    setBusy(true);
    setError(null);
    try {
      await moveWorkflowFolder(context, folder.id, destinationId);
      await onMoved(destinationId);
      onClose();
    } catch (caught) {
      setError(`Couldn't move this folder. ${caught instanceof Error ? caught.message : "The server rejected the destination."} Choose another available destination or check permissions, then try again.`);
    } finally {
      setBusy(false);
    }
  };

  const controls = (parentId?: string) => {
    const key = parentId ?? "root";
    if (loadingKeys.has(key)) return <p role="status" className="wf-folder-loading"><LoaderCircle size={14} /> Loading folders…</p>;
    if (loadErrors[key]) return <p role="alert" className="wf-folder-load-error">{loadErrors[key]} <button type="button" onClick={() => void loadPage(parentId)}>Retry loading folders</button></p>;
    if (continuations[key]) return <button type="button" className="wf-folder-load-more" onClick={() => void loadPage(parentId, continuations[key], true)}>Load more folders</button>;
    return null;
  };

  const rows = (items: WorkflowFolder[]): ReactNode => items.map(candidate => {
    const state = eligibility[candidate.id] ?? "checking";
    if (state !== "eligible") return null;
    const open = expanded.has(candidate.id);
    return <div className="wf-move-folder-node" key={candidate.id}>
      <div className="wf-move-folder-row">
        <button type="button" className="wf-move-folder-expand" aria-label={`${open ? "Collapse" : "Expand"} ${candidate.name}`} aria-expanded={open} onClick={() => void toggle(candidate)}>{open ? <ChevronDown size={15} /> : <ChevronRight size={15} />}</button>
        <label className="wf-move-folder-row">
          <input type="radio" name="folder-parent" value={candidate.id} checked={destinationId === candidate.id} disabled={busy} onChange={() => setDestinationId(candidate.id)} />
          <Folder size={15} /> {candidate.name}
        </label>
      </div>
      {open ? <div className="wf-move-folder-children">{rows(children[candidate.id] ?? [])}{controls(candidate.id)}</div> : null}
    </div>;
  });

  return <div className="wf-dialog-backdrop" role="presentation">
    <section ref={ref} className="wf-dialog wf-folder-mutation-dialog" role="dialog" aria-modal="true" aria-labelledby={titleId} tabIndex={-1}>
      <form onSubmit={submit}>
        <div className="wf-dialog-heading"><div><h3 id={titleId}>Move folder</h3><p>Choose a new parent for {folder.name}. The folder keeps its identity.</p></div></div>
        <div className="wf-folder-mutation-content">
          {error ? <p className="wf-dialog-error" role="alert">{error}</p> : null}
          {Object.values(eligibility).some(state => state === "checking") ? <p className="wf-folder-loading" role="status"><LoaderCircle size={14} /> Verifying destinations…</p> : null}
          {Object.values(eligibility).some(state => state === "unavailable") ? <p className="wf-dialog-error" role="alert">Some destinations could not be verified and were excluded. Retry by reopening this dialog.</p> : null}
          <fieldset className="wf-move-destination" disabled={busy}><legend>New parent</legend><div className="wf-move-folder-list">
            <label className="wf-move-folder-row"><input type="radio" name="folder-parent" checked={destinationId === null} onChange={() => setDestinationId(null)} /> Top level</label>
            {rows(roots)}{controls()}
          </div></fieldset>
        </div>
        <div className="wf-dialog-actions"><button type="button" onClick={onClose} disabled={busy}>Cancel</button><button type="submit" disabled={busy || (destinationId !== null && eligibility[destinationId] !== "eligible")}>{busy ? "Moving…" : "Move"}</button></div>
      </form>
    </section>
  </div>;
}
