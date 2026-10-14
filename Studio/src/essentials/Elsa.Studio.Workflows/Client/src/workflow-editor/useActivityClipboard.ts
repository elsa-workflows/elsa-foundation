import { useCallback, useEffect, useRef, useState } from "react";
import type { ActivityCatalogItem, WorkflowDraft } from "../workflowTypes";
import { resolveScope, type ScopeFrame } from "../workflowAdapter";
import type { WorkflowDraftRecipe } from "./workflowDocument";
import {
  applyActivityDuplicatePlan,
  planActivityDuplicate,
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
  editDraftAndSelect(recipe: WorkflowDraftRecipe, selectedNodeId: string | null): void;
  setStatus(value: string): void;
}

export function useActivityClipboard({
  draft,
  frames,
  catalogByVersion,
  selectedNodeIds,
  disabled,
  definitionId,
  editDraftAndSelect,
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
    return snapshotSelectedActivities({
      selectedNodeIds: nodeIds,
      scopeActivities: scope.slot.activities,
      scopeOwner: scope.owner,
      catalog: catalogByVersion,
      layout: draft.layout,
      presentation: draft.activityPresentation ?? []
    });
  }, [catalogByVersion, disabled, draft, frames]);

  const applySnapshot = useCallback((snapshot: ActivityClipboardSnapshot, status: string) => {
    if (!draft) return false;
    const plan = planActivityDuplicate({
      snapshot,
      draft,
      frames,
      catalog: catalogByVersion
    });
    if (!plan) return false;
    editDraftAndSelect(
      ({ draft: current, frames: currentFrames }) => current
        ? applyActivityDuplicatePlan(current, currentFrames, plan, catalogByVersion)
        : null,
      plan.selectedNodeIds[0] ?? null
    );
    setStatus(status);
    return true;
  }, [catalogByVersion, draft, editDraftAndSelect, frames, setStatus]);

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
      snapshot.activities.length === 1 ? "Duplicated 1 activity." : `Duplicated ${snapshot.activities.length} activities.`
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
