import { useCallback, useEffect, useRef, useState, type FormEvent } from "react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { StudioAlert, StudioButton, StudioDialog, StudioDialogFooter } from "@elsa-workflows/studio-ui";
import { deleteWorkflowFolder, previewWorkflowFolderDeletion, type FolderDeletionPreview } from "../api/workflowDesign";
import type { WorkflowFolder } from "../workflowTypes";

export function WorkflowFolderDeletionDialog({ context, folder, onClose, onDeleted }: {
  context: StudioEndpointContext;
  folder: WorkflowFolder;
  onClose(): void;
  onDeleted(preview: FolderDeletionPreview): Promise<void> | void;
}) {
  const [preview, setPreview] = useState<FolderDeletionPreview | null>(null);
  const [deleteWorkflows, setDeleteWorkflows] = useState(false);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const request = useRef(0);
  const current = useRef({ context, folderId: folder.id });
  current.current = { context, folderId: folder.id };

  const reload = useCallback(async () => {
    const generation = ++request.current;
    setLoading(true); setError(null); setPreview(null); setDeleteWorkflows(false);
    try {
      const next = await previewWorkflowFolderDeletion(context, folder.id);
      if (request.current === generation) setPreview(next);
    } catch (caught) {
      if (request.current === generation) setError(`Couldn't inspect this folder. ${caught instanceof Error ? caught.message : "Try again."}`);
    } finally {
      if (request.current === generation) setLoading(false);
    }
  }, [context, folder.id]);

  const invalidateRequests = useCallback(() => { request.current++; }, []);
  useEffect(() => {
    void reload();
    return invalidateRequests;
  }, [reload, invalidateRequests]);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (!preview || loading || busy || error) return;
    setBusy(true); setError(null);
    const identity = current.current;
    const generation = request.current;
    try {
      await deleteWorkflowFolder(context, folder.id, {
        previewRevision: preview.revision, deleteWorkflows, operationKey: crypto.randomUUID()
      });
      if (request.current !== generation || current.current.context !== identity.context || current.current.folderId !== identity.folderId) return;
      await onDeleted(preview);
    } catch (caught) {
      if (request.current !== generation || current.current.context !== identity.context || current.current.folderId !== identity.folderId) return;
      setError(`Couldn't delete this folder. ${caught instanceof Error ? caught.message : "The server rejected the request."} Reload the preview before confirming again.`);
      setPreview(null); setDeleteWorkflows(false);
    } finally {
      setBusy(false);
    }
  };

  const destination = preview?.parent ? `the parent folder “${preview.parent.name}”` : "Unfiled";
  const count = preview?.workflowCount ?? 0;
  return <StudioDialog
    open
    title={<>Delete folder “{preview?.folder.name ?? folder.name}”?</>}
    className="wf-tokens"
    onClose={onClose}
    dismissDisabled={busy}
    dismissOnOutsideClick={false}
    onSubmit={submit}
    footer={<StudioDialogFooter
      onCancel={onClose}
      cancelDisabled={busy}
      confirmVariant="danger"
      confirmDisabled={!preview || loading || busy || !!error}
      confirmLabel={busy ? "Deleting…" : deleteWorkflows ? `Delete folder and ${count} ${count === 1 ? "workflow" : "workflows"}` : "Delete folder"}
    />}
  >
    <div className="wf-folder-mutation-content">
      {loading ? <p role="status">Checking folder contents…</p> : null}
      {error ? <StudioAlert tone="danger">{error} <StudioButton size="sm" disabled={busy || loading} onClick={() => void reload()}>Reload preview</StudioButton></StudioAlert> : null}
      {preview ? <>
        <p>This folder contains {count} active {count === 1 ? "workflow" : "workflows"}.
          {count > 0 ? ` ${deleteWorkflows ? `They will be deleted and can be restored from the Recycle bin. Restored workflows will appear in ${destination}.` : `They will be moved to ${destination}.`}` : ""}</p>
        {preview.deletedWorkflowCount > 0 ? <p>{preview.deletedWorkflowCount} already deleted {preview.deletedWorkflowCount === 1 ? "workflow will" : "workflows will"} be moved to {destination} and remain restorable.</p> : null}
        {preview.childFolderCount > 0 ? <p>{preview.childFolderCount} {preview.childFolderCount === 1 ? "subfolder will" : "subfolders will"} move to {preview.parent ? `the parent folder “${preview.parent.name}”` : "the top level"}. Their hierarchy and workflows will be preserved.</p> : null}
        {count > 0 ? <label className="wf-folder-delete-choice"><input type="checkbox" checked={deleteWorkflows} disabled={busy} onChange={event => setDeleteWorkflows(event.target.checked)} /> Also delete {count === 1 ? "the workflow" : `the ${count} workflows`} directly in this folder</label> : null}
        <p>The folder itself cannot be restored.</p>
      </> : null}
    </div>
  </StudioDialog>;
}
