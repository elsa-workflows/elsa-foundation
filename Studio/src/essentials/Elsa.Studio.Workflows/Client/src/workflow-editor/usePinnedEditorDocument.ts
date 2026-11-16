import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { authSessionEndedEvent, authSessionStartedEvent, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { resolveScope, type ScopeFrame } from "../workflowAdapter";
import type { ActivityCatalogItem } from "../workflowTypes";
import { getDraftSignature, isRejectedTestRun } from "./editorHelpers";
import type { WorkflowDocumentApi } from "./workflowDocument";
import { runtimeValueContextId } from "./RuntimeValueEvidence";

/** Historical presentation never replaces the editable document or its persistence/history inputs. */
export function usePinnedEditorDocument({ document, context, definitionId, catalog }: {
  document: WorkflowDocumentApi; context: StudioEndpointContext; definitionId: string; catalog: ActivityCatalogItem[];
}) {
  const run = document.state.testRun;
  const [session, setSession] = useState({ authorized: true, revision: 0 });
  const key = JSON.stringify([runtimeValueContextId(context), definitionId, run?.view.testRunId, run?.view.artifactId, run?.draftSignature, session.revision]);
  const valid = Boolean(session.authorized && run?.snapshot && run.endpointContext === context && run.snapshot.definitionId === definitionId
    && run.view.definitionId === definitionId && !isRejectedTestRun(run.view)
    && getDraftSignature(run.snapshot) === run.draftSignature);
  const [presentation, setPresentation] = useState<{ key: string; active: boolean; frames: ScopeFrame[]; selectedNodeId: string | null } | null>(null);
  const pinned = valid && presentation?.key === key && presentation.active;
  const readOnly = useRef(pinned); readOnly.current = pinned;
  const currentKey = useRef(key); currentKey.current = key;
  useEffect(() => {
    const end = () => { readOnly.current = true; setSession(value => ({ authorized: false, revision: value.revision + 1 })); setPresentation(null); };
    const start = () => { readOnly.current = true; setSession(value => ({ authorized: true, revision: value.revision + 1 })); setPresentation(null); };
    window.addEventListener(authSessionEndedEvent, end);
    window.addEventListener(authSessionStartedEvent, start);
    return () => {
      window.removeEventListener(authSessionEndedEvent, end);
      window.removeEventListener(authSessionStartedEvent, start);
    };
  }, []);
  const { loadDraft, replaceDraftByBatch, compareAndReplaceDraftByBatch, editDraft, editDraftAndSelect } = document;
  const actions = useMemo(() => ({
    loadDraft: (...args: Parameters<WorkflowDocumentApi["loadDraft"]>) => { if (!pinned && currentKey.current === key && !readOnly.current) loadDraft(...args); },
    compareAndReplaceDraftByBatch: (...args: Parameters<WorkflowDocumentApi["compareAndReplaceDraftByBatch"]>) => { if (!pinned && currentKey.current === key && !readOnly.current) compareAndReplaceDraftByBatch(...args); },
    replaceDraftByBatch: (...args: Parameters<WorkflowDocumentApi["replaceDraftByBatch"]>) => { if (!pinned && currentKey.current === key && !readOnly.current) replaceDraftByBatch(...args); },
    editDraft: (...args: Parameters<WorkflowDocumentApi["editDraft"]>) => { if (!pinned && currentKey.current === key && !readOnly.current) editDraft(...args); },
    editDraftAndSelect: (...args: Parameters<WorkflowDocumentApi["editDraftAndSelect"]>) => { if (!pinned && currentKey.current === key && !readOnly.current) editDraftAndSelect(...args); }
  }), [compareAndReplaceDraftByBatch, editDraft, editDraftAndSelect, key, loadDraft, pinned, replaceDraftByBatch]);
  const { navigateToScope: navigateEditable, select: selectEditable } = document;
  const navigateToScope: WorkflowDocumentApi["navigateToScope"] = useCallback((frames, selectedNodeId = null) => {
    if (pinned) setPresentation(value => value?.key === key ? { ...value, frames, selectedNodeId } : value);
    else navigateEditable(frames, selectedNodeId);
  }, [key, navigateEditable, pinned]);
  const select: WorkflowDocumentApi["select"] = useCallback(selectedNodeId => {
    if (pinned) setPresentation(value => value?.key === key ? { ...value, selectedNodeId } : value);
    else selectEditable(selectedNodeId);
  }, [key, pinned, selectEditable]);
  const resetToRoot = useCallback(() => navigateToScope([], null), [navigateToScope]);
  const canEdit = useCallback(() => !pinned && currentKey.current === key && !readOnly.current, [key, pinned]);
  return {
    ...document,
    ...actions,
    canEdit,
    state: pinned ? { ...document.state, draft: run!.snapshot!, frames: presentation!.frames, selectedNodeId: presentation!.selectedNodeId } : document.state,
    pinned,
    select,
    navigateToScope,
    resetToRoot,
    showSnapshot: () => {
      if (!valid || !run?.snapshot?.state.rootActivity) return false;
      const versions = new Map(catalog.map(item => [item.activityVersionId, item]));
      const frames = resolveScope(run.snapshot.state.rootActivity, document.state.frames, versions) ? document.state.frames : [];
      readOnly.current = true;
      setPresentation(value => value?.key === key ? { ...value, active: true } : { key, active: true, frames, selectedNodeId: document.state.selectedNodeId });
      return true;
    },
    returnToEditing: () => setPresentation(value => value?.key === currentKey.current ? { ...value, active: false } : null)
  };
}
