import React, { useEffect, useState } from "react";
import type { StudioWeaverResultRendererProps } from "@elsa-workflows/studio-sdk";
import type { WorkflowAgentController } from "./workflowAgent";

export function WorkflowLayoutResult({ controller, preparedInvocation, toolResult }: StudioWeaverResultRendererProps & { controller: WorkflowAgentController }) {
  const [status, setStatus] = useState("");
  const [pending, setPending] = useState(false);
  const [undone, setUndone] = useState(false);
  const data = toolResult?.payload?.data ?? preparedInvocation?.preview.data;
  const record = data && typeof data === "object" ? data as Record<string, unknown> : {};
  const token = typeof record.undoToken === "string" ? record.undoToken : null;
  async function undo() {
    if (pending || undone || !token) return;
    if (!controller.undo) { setStatus("The originating workflow editor is unavailable."); return; }
    setPending(true); setStatus("");
    try {
      setStatus(await controller.undo(token));
      setUndone(true);
    } catch (error) {
      setStatus(error instanceof Error ? error.message : String(error));
    } finally { setPending(false); }
  }
  return <section aria-label="Workflow layout change"><p>{toolResult?.content ?? "Review the activity position change in the working draft."}</p>
    <pre>{JSON.stringify(data, null, 2)}</pre>
    {token ? <button type="button" disabled={!controller.undo || pending || undone} onClick={() => void undo()}>Undo whole change</button> : null}
    {status ? <p role="status">{status}</p> : null}</section>;
}

/**
 * The whole-change Undo of a reviewed Operation Batch (spec 1193 FR-009). It is designer-history Undo of the batch's
 * own entry, so a later history Undo can never apply the batch twice. It acts only while that entry is the current
 * position; after a later edit it is disabled with the reason, and designer history remains the path.
 */
export function WorkflowBatchUndoControl({ controller, toolResult }: StudioWeaverResultRendererProps & { controller: WorkflowAgentController }) {
  const data = toolResult?.payload?.data;
  const token = data && typeof data === "object" && typeof (data as Record<string, unknown>).undoToken === "string"
    ? (data as Record<string, unknown>).undoToken as string : null;
  const [, setVersion] = useState(0);
  const [status, setStatus] = useState("");
  const [pending, setPending] = useState(false);
  const batch = controller.batch;
  useEffect(() => batch?.subscribe(() => setVersion(version => version + 1)), [batch]);
  if (!token) return null;
  const availability = batch?.state(token) ?? { status: "unavailable" as const, persisted: null, reason: "The originating workflow editor is unavailable." };
  async function undo() {
    if (pending || !token || !batch) return;
    setPending(true); setStatus("");
    try { setStatus(await batch.undo(token)); }
    catch (error) { setStatus(error instanceof Error ? error.message : String(error)); }
    finally { setPending(false); }
  }
  return <div className="wf-agent-batch-undo">
    {/* FR-011: the live outcome. Persisted once the user's explicit save covered it; "not saved" only while this editor's
        history holds the batch as its current, un-undone entry; unknown (another editor's history) is said neutrally. Anything
        else says only why, never claiming unsaved. */}
    {availability.persisted === true ? <p data-outcome="persisted">Persisted by your explicit save.</p>
      : availability.status === "available" ? <p data-outcome="applied-locally">Not saved: the change is in the unsaved working draft. Saving and publishing stay with you.</p>
        : availability.persisted === null ? <p data-outcome="unknown">Whether this change was saved is not known here.</p>
          : null}
    <button type="button" disabled={pending || availability.status !== "available"} title={availability.reason} onClick={() => void undo()}>Undo whole change</button>
    {availability.status !== "available" && availability.reason ? <p role="note">{availability.reason}</p> : null}
    {status ? <p role="status">{status}</p> : null}
  </div>;
}

/** Reverts a reviewed list or create-dialog change on its originating page (spec 1193 FR-020, FR-023); nothing was saved. */
export function WorkflowPageRevertControl({ controller, toolResult }: StudioWeaverResultRendererProps & { controller: WorkflowAgentController }) {
  const data = toolResult?.payload?.data;
  const token = data && typeof data === "object" && typeof (data as Record<string, unknown>).undoToken === "string" ? (data as Record<string, unknown>).undoToken as string : null;
  const [status, setStatus] = useState("");
  const [done, setDone] = useState(false);
  if (!token) return null;
  const revert = controller.library?.revert;
  return <div className="wf-agent-page-revert">
    <p data-outcome="applied-locally">Applied on this page only; nothing was saved.</p>
    <button type="button" disabled={!revert || done} onClick={() => { const message = revert?.(token) ?? "The originating page is unavailable."; setStatus(message); setDone(message.startsWith("Reverted")); }}>Revert</button>
    {status ? <p role="status">{status}</p> : null}
  </div>;
}
