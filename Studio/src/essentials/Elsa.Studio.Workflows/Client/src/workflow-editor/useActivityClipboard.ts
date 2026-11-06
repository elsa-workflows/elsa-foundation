import { useCallback, useEffect, useRef, useState } from "react";
import { flushSync } from "react-dom";
import type { ActivityCatalogItem, WorkflowDraft } from "../workflowTypes";
import { resolveScope, type ScopeFrame } from "../workflowAdapter";
import type { WorkflowDraftRecipe } from "./workflowDocument";
import {
  pasteActivitiesIntoDraft,
  ActivityCopyRefusal,
  snapshotSelectedActivities,
  type ActivityClipboardSnapshot
} from "./duplicateActivities";

interface ActivityClipboardParams {
  draft: WorkflowDraft | null;
  frames: ScopeFrame[];
  catalogByVersion: Map<string, ActivityCatalogItem>;
  selectedNodeIds: string[];
  disabled: boolean;
  definitionId: string;
  editDraft(recipe: WorkflowDraftRecipe): void;
  select(selectedNodeId: string | null): void;
  setStatus(value: string): void;
}

export function useActivityClipboard({
  draft,
  frames,
  catalogByVersion,
  selectedNodeIds,
  disabled,
  definitionId,
  editDraft,
  select,
  setStatus
}: ActivityClipboardParams) {
  const clipboardRef = useRef<ActivityClipboardSnapshot | null>(null);
  const [contextMenu, setContextMenu] = useState<{ x: number; y: number; nodeIds: string[] } | null>(null);

  useEffect(() => {
    clipboardRef.current = null;
    setContextMenu(null);
  }, [definitionId]);

  const snapshotFrom = useCallback((nodeIds: string[]) => {
    if (!draft || disabled) return null;
    const root = draft.state.rootActivity;
    if (!root) return null;
    const scope = resolveScope(root, frames, catalogByVersion);
    if (!scope) return null;
    try { return snapshotSelectedActivities({
      selectedNodeIds: nodeIds,
      scopeActivities: scope.slot.activities,
      scopeOwner: scope.owner,
      catalog: catalogByVersion,
      layout: draft.layout,
      presentation: draft.activityPresentation ?? []
    }); } catch (error) {
      if (!(error instanceof ActivityCopyRefusal)) throw error;
      setStatus(error.message); return null;
    }
  }, [catalogByVersion, disabled, draft, frames, setStatus]);

  const applySnapshot = useCallback((snapshot: ActivityClipboardSnapshot, status: string, insertAfterNodeId?: string) => {
    if (disabled) return false;
    let selectedNodeId: string | null = null;
    let applied = false;
    let refusal: string | null = null;
    // Window-level keydown is outside React's event system, so the document reducer
    // is scheduled instead of flushed. preventDefault and the status line need the
    // recipe result in this turn.
    flushSync(() => {
      editDraft(({ draft: current, frames: currentFrames }) => {
        if (!current) return null;
        let result: ReturnType<typeof pasteActivitiesIntoDraft>;
        try { result = pasteActivitiesIntoDraft({
          draft: current,
          frames: currentFrames,
          snapshot,
          catalog: catalogByVersion,
          insertAfterNodeId
        }); } catch (error) {
          if (!(error instanceof ActivityCopyRefusal)) throw error;
          refusal = error.message; return null;
        }
        if (!result) return null;
        selectedNodeId = result.selectedNodeIds[0] ?? null;
        applied = true;
        return result.draft;
      });
    });
    if (!applied) { if (refusal) setStatus(refusal); return false; }
    select(selectedNodeId);
    setStatus(status);
    return true;
  }, [catalogByVersion, disabled, editDraft, select, setStatus]);

  const copy = useCallback(() => {
    const snapshot = snapshotFrom(selectedNodeIds);
    if (!snapshot) return false;
    clipboardRef.current = snapshot;
    setStatus(snapshot.activities.length === 1 ? "Copied 1 activity." : `Copied ${snapshot.activities.length} activities.`);
    return true;
  }, [selectedNodeIds, setStatus, snapshotFrom]);

  const paste = useCallback(() => {
    const snapshot = clipboardRef.current;
    if (!snapshot || disabled) return false;
    return applySnapshot(
      snapshot,
      snapshot.activities.length === 1 ? "Pasted 1 activity." : `Pasted ${snapshot.activities.length} activities.`
    );
  }, [applySnapshot, disabled]);

  const duplicate = useCallback((nodeIds = selectedNodeIds) => {
    const snapshot = snapshotFrom(nodeIds);
    if (!snapshot) return false;
    clipboardRef.current = snapshot;
    return applySnapshot(
      snapshot,
      snapshot.activities.length === 1 ? "Duplicated 1 activity." : `Duplicated ${snapshot.activities.length} activities.`,
      snapshot.activities.at(-1)?.nodeId
    );
  }, [applySnapshot, selectedNodeIds, snapshotFrom]);

  const openContextMenu = useCallback((x: number, y: number, nodeIds: string[]) => {
    if (disabled || nodeIds.length === 0) return;
    setContextMenu({ x, y, nodeIds });
  }, [disabled]);

  const closeContextMenu = useCallback(() => setContextMenu(null), []);

  return {
    copy,
    paste,
    duplicate,
    contextMenu,
    openContextMenu,
    closeContextMenu
  };
}
