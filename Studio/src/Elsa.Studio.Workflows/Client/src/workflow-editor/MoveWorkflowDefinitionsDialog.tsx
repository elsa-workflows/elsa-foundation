import { ChevronDown, ChevronRight, Folder, LoaderCircle } from "lucide-react";
import { useEffect, useId, useRef, useState, type FormEvent, type ReactNode } from "react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { moveWorkflowDefinitions } from "../api/workflowDesign";
import type { WorkflowFolder } from "../workflowTypes";
import { useDialogFocus } from "./useDialogFocus";
import { useWorkflowFolderTree, workflowFolderRootKey } from "./useWorkflowFolderTree";

export function MoveWorkflowDefinitionsDialog({ context, definitionIds, onClose, onMoved }: {
  context: StudioEndpointContext;
  definitionIds: string[];
  onClose(): void;
  onMoved(): Promise<void> | void;
}) {
  const titleId = useId();
  const dialogRef = useRef<HTMLElement>(null);
  const [expanded, setExpanded] = useState<Set<string>>(() => new Set());
  const [destinationId, setDestinationId] = useState<string | null>(null);
  const [moving, setMoving] = useState(false);
  const [moveError, setMoveError] = useState<string | null>(null);
  const { roots, children, continuations, loadedKeys, loadingKeys, loadFailures, loadPage } = useWorkflowFolderTree({
    context,
    unavailableMessage: "Workflow folders are not available for this server."
  });

  useDialogFocus(dialogRef, moving ? null : onClose);

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
    const key = parentId ?? workflowFolderRootKey;
    if (loadingKeys.has(key)) return <p className="wf-folder-loading" role="status"><LoaderCircle size={14} /> Loading folders…</p>;
    if (loadFailures[key]) return <p className="wf-folder-load-error" role="alert">{loadFailures[key]} <button type="button" onClick={() => void loadPage(parentId)}>Retry loading folders</button></p>;
    if (continuations[key]) return <button type="button" className="wf-folder-load-more" onClick={() => void loadPage(parentId, continuations[key], { append: true })}>Load more folders</button>;
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
        <div className="wf-move-dialog-content">
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
        </div>
        <div className="wf-dialog-actions">
          <button type="button" onClick={onClose} disabled={moving}>Cancel</button>
          <button type="submit" disabled={moving}>{moving ? "Moving…" : "Move"}</button>
        </div>
      </form>
    </section>
  </div>;
}
