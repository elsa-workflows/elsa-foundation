import { useEffect, useRef } from "react";
import type { StudioWeaverContextAttachment, StudioWeaverPreparedInvocation } from "@elsa-workflows/studio-sdk";
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
  build(state: TState, client: { clientId: string; generationId: string }): StudioWeaverContextAttachment | null;
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
  // Keyed by this page instance's generation, so two mounted pages of one kind never replace or remove each other's entries.
  const snapshotKey = `${kind}:${generation.current}`;
  useEffect(() => {
    if (!library || !attachment) return;
    library.snapshots.set(snapshotKey, attachment);
    return () => { if (library.snapshots.get(snapshotKey) === attachment) library.snapshots.delete(snapshotKey); };
    // The attachment is rebuilt whenever its revision changes; the revision is its identity.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [library, snapshotKey, revision]);
  useEffect(() => {
    if (!library) return;
    const handler = async (invocation: StudioWeaverPreparedInvocation, signal?: AbortSignal) => {
      const problem = signal?.aborted ? "The change was cancelled before it was applied." : pageBindingProblem(invocation, library.snapshots.get(snapshotKey), library.snapshots.get(snapshotKey)?.target?.resourceType ?? "");
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
    // The tool contract dispatches by the invocation's client generation: exactly this page instance's handler.
    const handlerKey = workflowPageHandlerKey(toolId, generation.current);
    library.handlers.set(handlerKey, handler);
    return () => { if (library.handlers.get(handlerKey) === handler) library.handlers.delete(handlerKey); };
  }, [clientId, describe, library, resultType, snapshotKey, toolId]);
}

/** The handler of one page instance for one working-state tool, by its client generation. */
export function workflowPageHandlerKey(toolId: string, generationId: string | undefined) {
  return `${toolId}:${generationId ?? ""}`;
}
