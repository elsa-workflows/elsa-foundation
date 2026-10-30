import React, { useState } from "react";
import type { StudioAgentResultRendererProps } from "@elsa-workflows/studio-sdk";
import type { WorkflowAgentController } from "./workflowAgent";

export function WorkflowLayoutResult({ controller, preparedInvocation, toolResult }: StudioAgentResultRendererProps & { controller: WorkflowAgentController }) {
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
