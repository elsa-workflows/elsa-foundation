import { useEffect, useRef } from "react";
import type { StudioAgentContextAttachment, StudioAgentPreparedInvocation } from "@elsa-workflows/studio-sdk";
import type { WorkflowAgentController } from "../workflowAgent";
import { pageBindingProblem, pageReceipt } from "./workflowLibraryAgent";

/**
 * Publishes one page context (the visible list, the open create dialog or the template list) under a fresh client
 * generation for this page's lifetime, and handles that page's reviewed working-state tool: the change is applied only
 * when the exact binding and revision still match, the receipt repeats exactly the approved change, and a revert token
 * restores the state the change replaced. Nothing is saved.
 */
export function useWorkflowPageAgent<TState>({ controller, clientId, kind, toolId, resultType, build, apply, current, restore, describe }: {
  controller?: WorkflowAgentController;
  clientId: string;
  kind: string;
  toolId: string;
  resultType: string;
  /** The page's attachment for a visible state, or null while it has none to disclose. */
  build(state: TState, client: { clientId: string; generationId: string }): StudioAgentContextAttachment | null;
  /** The next page state for an approved change, or null when the change is not one this page supports. */
  apply(change: unknown): TState | null;
  current(): TState;
  restore(state: TState): void;
  describe: string;
}) {
  const library = controller?.library;
  const generation = useRef<string>(globalThis.crypto?.randomUUID?.() ?? `generation-${Date.now()}-${Math.random()}`);
  const latest = useRef({ build, apply, current, restore });
  latest.current = { build, apply, current, restore };
  const attachment = build(current(), { clientId, generationId: generation.current });
  const revision = attachment?.target?.revision ?? null;
  useEffect(() => {
    if (!library || !attachment) return;
    library.snapshots.set(kind, attachment);
    return () => { if (library.snapshots.get(kind) === attachment) library.snapshots.delete(kind); };
    // The attachment is rebuilt whenever its revision changes; the revision is its identity.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [library, kind, revision]);
  useEffect(() => {
    if (!library) return;
    const handler = async (invocation: StudioAgentPreparedInvocation, signal?: AbortSignal) => {
      const problem = signal?.aborted ? "The change was cancelled before it was applied." : pageBindingProblem(invocation, library.snapshots.get(kind), library.snapshots.get(kind)?.target?.resourceType ?? "");
      if (problem) return { outcome: "refused" as const, summary: problem };
      const args = invocation.arguments as { change?: unknown } | null;
      const before = latest.current.current();
      const next = latest.current.apply(args?.change);
      if (next === null) return { outcome: "refused" as const, summary: "The approved change is not one this page supports. Nothing was applied." };
      // The receipt's revision is the one the page discloses for the state it applies, computed from that state.
      const afterRevision = latest.current.build(next, { clientId, generationId: generation.current })?.target?.revision;
      if (!afterRevision || afterRevision === invocation.target?.revision)
        return { outcome: "refused" as const, summary: "The approved change produces no visible change. Nothing was applied." };
      latest.current.restore(next);
      const token = `workflow-page-undo-${invocation.invocationId}`;
      library.reverts.set(token, () => {
        library.reverts.delete(token);
        latest.current.restore(before);
        return "Reverted the change on this page.";
      });
      return pageReceipt(invocation, resultType, afterRevision, token, describe);
    };
    library.handlers.set(toolId, handler);
    return () => { if (library.handlers.get(toolId) === handler) library.handlers.delete(toolId); };
  }, [clientId, describe, kind, library, resultType, toolId]);
}
