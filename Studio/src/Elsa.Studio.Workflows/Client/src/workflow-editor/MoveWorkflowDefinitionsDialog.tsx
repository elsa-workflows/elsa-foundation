import { ChevronDown, ChevronRight, Folder, LoaderCircle } from "lucide-react";
import { useCallback, useEffect, useId, useRef, useState, type FormEvent, type ReactNode } from "react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { listWorkflowFolders, moveWorkflowDefinitions } from "../api/workflowDesign";
import type { WorkflowFolder } from "../workflowTypes";
import { useDialogFocus } from "./useDialogFocus";

const rootKey = "root";

type FolderLists = Record<string, WorkflowFolder[]>;

export function MoveWorkflowDefinitionsDialog({ context, definitionIds, onClose, onMoved }: {
  context: StudioEndpointContext;
  definitionIds: string[];
  onClose(): void;
  onMoved(): Promise<void> | void;
}) {
  const titleId = useId();
  const dialogRef = useRef<HTMLElement>(null);
  const pendingKeys = useRef(new Set<string>());
  const [roots, setRoots] = useState<WorkflowFolder[]>([]);
  const [children, setChildren] = useState<FolderLists>({});
  const [continuations, setContinuations] = useState<Record<string, string | null>>({});
  const [loadedKeys, setLoadedKeys] = useState<Set<string>>(() => new Set());
  const [expanded, setExpanded] = useState<Set<string>>(() => new Set());
  const [loadingKeys, setLoadingKeys] = useState<Set<string>>(() => new Set());
  const [loadFailures, setLoadFailures] = useState<Record<string, string>>({});
  const [destinationId, setDestinationId] = useState<string | null>(null);
  const [moving, setMoving] = useState(false);
  const [moveError, setMoveError] = useState<string | null>(null);

  useDialogFocus(dialogRef, moving ? null : onClose);

  const loadPage = useCallback(async (parentId?: string, continuationToken?: string | null, append = false) => {
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
      if (!page) {
        setLoadFailures(current => ({ ...current, [key]: "Workflow folders are not available for this server." }));
        return false;
      }
      const merge = (current: WorkflowFolder[]) => append ? [...current, ...page.items] : page.items;
      if (parentId) setChildren(current => ({ ...current, [parentId]: merge(current[parentId] ?? []) }));
      else setRoots(current => merge(current));
      setContinuations(current => ({ ...current, [key]: page.nextContinuationToken }));
      setLoadedKeys(current => new Set(current).add(key));
      return true;
    } catch (error) {
      setLoadFailures(current => ({
        ...current,
        [key]: error instanceof Error ? error.message : "Couldn't load folders."
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
    void loadPage();
  }, [loadPage]);

  const toggleFolder = async (folder: WorkflowFolder) => {
    if (expanded.has(folder.id)) {
      setExpanded(current => {
        const next = new Set(current);
        next.delete(folder.id);
        return next;
      });
      return;
    }
    setExpanded(current => new Set(current).add(folder.id));
    if (!loadedKeys.has(folder.id)) await loadPage(folder.id);
  };

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (moving) return;
    setMoving(true);
    setMoveError(null);
    try {
      await moveWorkflowDefinitions(context, { definitionIds, folderId: destinationId });
      await onMoved();
      onClose();
    } catch (error) {
      const detail = error instanceof Error ? error.message : "The server couldn't complete the move.";
      setMoveError(`Couldn't move the selected workflow definitions. ${detail} Check your access and destination, then try again.`);
    } finally {
      setMoving(false);
    }
  };

  const renderPageControls = (parentId?: string) => {
    const key = parentId ?? rootKey;
    if (loadingKeys.has(key)) return <p className="wf-folder-loading" role="status"><LoaderCircle size={14} /> Loading folders…</p>;
    if (loadFailures[key]) return <p className="wf-folder-load-error" role="alert">{loadFailures[key]} <button type="button" onClick={() => void loadPage(parentId)}>Retry loading folders</button></p>;
    if (continuations[key]) return <button type="button" className="wf-folder-load-more" onClick={() => void loadPage(parentId, continuations[key], true)}>Load more folders</button>;
    return null;
  };

  const renderFolder = (folder: WorkflowFolder): ReactNode => {
    const isExpanded = expanded.has(folder.id);
    const childFolders = children[folder.id] ?? [];
    return <div className="wf-move-folder-node" key={folder.id}>
      <div className="wf-move-folder-row">
        <button
          type="button"
          className="wf-move-folder-expand"
          aria-label={`${isExpanded ? "Collapse" : "Expand"} ${folder.name}`}
          aria-expanded={isExpanded}
          onClick={() => void toggleFolder(folder)}
        >
          {isExpanded ? <ChevronDown size={15} /> : <ChevronRight size={15} />}
        </button>
        <label className="wf-move-folder-row">
          <input
            type="radio"
            name="workflow-folder-destination"
            data-folder-id={folder.id}
            checked={destinationId === folder.id}
            onChange={() => setDestinationId(folder.id)}
          />
          <Folder size={15} /> {folder.name}
        </label>
      </div>
      {isExpanded ? <div className="wf-move-folder-children">
        {childFolders.map(child => renderFolder(child))}
        {renderPageControls(folder.id)}
      </div> : null}
    </div>;
  };

  return <div className="wf-dialog-backdrop" role="presentation">
    <section ref={dialogRef} className="wf-dialog wf-move-definitions-dialog" role="dialog" aria-modal="true" aria-labelledby={titleId} tabIndex={-1}>
      <form onSubmit={submit}>
        <div className="wf-dialog-heading">
          <div>
            <h3 id={titleId}>Move workflow definitions</h3>
            <p>Choose where to place {definitionIds.length === 1 ? "the selected workflow definition" : `${definitionIds.length} selected workflow definitions`}.</p>
          </div>
        </div>
        {moveError ? <p className="wf-dialog-error" role="alert">{moveError}</p> : null}
        <fieldset className="wf-move-destination" disabled={moving}>
          <legend>Destination</legend>
          <div className="wf-move-folder-list" aria-label="Workflow folder destinations">
            <div className="wf-move-folder-node">
              <label className="wf-move-folder-row">
                <input type="radio" name="workflow-folder-destination" data-destination="unfiled" checked={destinationId === null} onChange={() => setDestinationId(null)} />
                Unfiled
              </label>
            </div>
            {roots.map(folder => renderFolder(folder))}
            {renderPageControls()}
          </div>
        </fieldset>
        <div className="wf-dialog-actions">
          <button type="button" onClick={onClose} disabled={moving}>Cancel</button>
          <button type="submit" disabled={moving}>{moving ? "Moving…" : "Move"}</button>
        </div>
      </form>
    </section>
  </div>;
}
