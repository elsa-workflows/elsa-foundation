import { useEffect, useRef } from "react";
import type { WorkflowWeaverController } from "../workflowWeaver";
import { workflowExecutableAttachment, workflowRunAttachment, workflowVersionAttachment } from "../workflowWeaverContext";

/**
 * Publishes one read-only page context (spec 1193 stories 8 and 9) for this page instance's lifetime: the exact Executable
 * the Executable Inspector shows, the exact workflow version selected in the version history, or the exact Run the run page
 * shows with its selected activity execution. It is keyed by a per-mount client generation, replaced when the identity
 * changes (another artifact, a reloaded one with another hash, another version, another selected activity execution) and
 * removed when the page unmounts or has nothing loaded. Nothing here can change the resource.
 */
export function useWorkflowInspectionContext({ controller, clientId, kind, identity }: {
  controller?: WorkflowWeaverController;
  clientId: string;
  kind: "executable" | "version" | "run";
  /** The loaded identity, or null while nothing is loaded. */
  identity: Record<string, unknown> | null;
}) {
  const generation = useRef<string>(globalThis.crypto?.randomUUID?.() ?? `generation-${Date.now()}-${Math.random()}`);
  const store = kind === "executable" ? controller?.inspection?.executables : kind === "run" ? controller?.inspection?.runs : controller?.inspection?.versions;
  const client = { clientId, generationId: generation.current };
  const attachment = !identity ? null : kind === "executable" ? workflowExecutableAttachment(identity, client)
    : kind === "run" ? workflowRunAttachment(identity, client) : workflowVersionAttachment(identity, client);
  // The attachment's identity: its id and bound revision (the Artifact Hash, the version id or the selected activity execution) and its content.
  const key = attachment ? JSON.stringify([attachment.id, attachment.target?.revision, attachment.content]) : null;
  useEffect(() => {
    if (!store || !attachment) return;
    const slot = `${kind}:${generation.current}`;
    store.set(slot, attachment);
    return () => { if (store.get(slot) === attachment) store.delete(slot); };
    // The attachment is rebuilt whenever its identity changes; the identity is its key.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [store, kind, key, clientId]);
}
