import { useEffect, useRef } from "react";
import type { WorkflowAgentController } from "../workflowAgent";
import { workflowExecutableAttachment, workflowVersionAttachment } from "../workflowAgentContext";

/**
 * Publishes one read-only page context (spec 1193 story 8) for this page instance's lifetime: the exact Executable the
 * Executable Inspector shows, or the exact workflow version selected in the version history. It is keyed by a per-mount
 * client generation, replaced when the identity changes (another artifact, a reloaded one with another hash, another
 * version) and removed when the page unmounts or has nothing loaded. Nothing here can change the resource.
 */
export function useWorkflowInspectionContext({ controller, clientId, kind, identity }: {
  controller?: WorkflowAgentController;
  clientId: string;
  kind: "executable" | "version";
  /** The loaded identity, or null while nothing is loaded. */
  identity: Record<string, unknown> | null;
}) {
  const generation = useRef<string>(globalThis.crypto?.randomUUID?.() ?? `generation-${Date.now()}-${Math.random()}`);
  const store = kind === "executable" ? controller?.inspection?.executables : controller?.inspection?.versions;
  const client = { clientId, generationId: generation.current };
  const attachment = identity ? (kind === "executable" ? workflowExecutableAttachment(identity, client) : workflowVersionAttachment(identity, client)) : null;
  // The attachment's identity: its id and bound revision (the Artifact Hash, or the version id) and its content.
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
