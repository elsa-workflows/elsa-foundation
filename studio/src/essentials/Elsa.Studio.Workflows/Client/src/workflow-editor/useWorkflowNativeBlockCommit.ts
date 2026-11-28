import { useEffect, useLayoutEffect, useRef } from "react";
import type { WorkflowDraft } from "../workflowTypes";
import type { ScopeFrame } from "../workflowAdapter";
import type { WorkflowDocumentApi } from "./workflowDocument";
import type { useDraftHistory } from "./useDraftHistory";
import { getDraftContentRevision } from "./editorHelpers";

/** React may defer the reducer. Only its receipt acknowledges an atomic ordinary document edit. */
export function useWorkflowNativeBlockCommit(document: WorkflowDocumentApi, history: Pick<ReturnType<typeof useDraftHistory>, "prepareEdit" | "recordEdit" | "cancelPreparedEdit">) {
  const pending = useRef<{ id: string; candidate: WorkflowDraft; resolve(applied: boolean): void } | null>(null);
  const historyRef = useRef(history); historyRef.current = history;
  const documentRef = useRef(document); documentRef.current = document;
  useLayoutEffect(() => {
    const item = pending.current;
    const receipt = document.state.editTransaction;
    if (!item || receipt?.id !== item.id) return;
    pending.current = null;
    if (receipt.applied) historyRef.current.recordEdit(item.candidate);
    else historyRef.current.cancelPreparedEdit();
    item.resolve(receipt.applied);
  }, [document.state.editTransaction]);
  useEffect(() => () => {
    const item = pending.current;
    pending.current = null;
    if (item) {
      const receipt = documentRef.current.state.editTransaction;
      const applied = receipt?.id === item.id && receipt.applied;
      if (applied) historyRef.current.recordEdit(item.candidate);
      else historyRef.current.cancelPreparedEdit();
      item.resolve(applied);
    }
  }, []);
  return (base: WorkflowDraft, frames: ScopeFrame[], selectedNodeId: string | null, candidate: WorkflowDraft, selection: string[], isCurrent: () => boolean) => {
    if (pending.current || !isCurrent()) return Promise.resolve(false);
    historyRef.current.prepareEdit();
    if (!isCurrent()) { historyRef.current.cancelPreparedEdit(); return Promise.resolve(false); }
    return new Promise<boolean>((resolve, reject) => {
      const id = crypto.randomUUID();
      pending.current = { id, candidate, resolve };
      try {
        document.compareAndEditDraft(id, base.definitionId, getDraftContentRevision(base), selectedNodeId, candidate,
          selection.at(-1) ?? null, structuredClone(frames), structuredClone(frames), () => pending.current?.id === id && isCurrent());
      } catch (error) {
        pending.current = null;
        historyRef.current.cancelPreparedEdit();
        reject(error);
      }
    });
  };
}
