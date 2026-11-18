import { useCallback, useEffect, useRef, useState } from "react";
import { flushSync } from "react-dom";
import type { ActivityCatalogItem, WorkflowDraft } from "../workflowTypes";
import { resolveScope, type ScopeFrame } from "../workflowAdapter";
import type { WorkflowDraftRecipe } from "./workflowDocument";
import {
  pasteActivitiesIntoDraft,
  snapshotSelectedActivities,
  type ActivityClipboardSnapshot
} from "./duplicateActivities";

import type { ActivityReferenceResourceKind } from "./activityReferenceRemapping";

interface ActivityClipboardParams {
  resourceKind: ActivityReferenceResourceKind;
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

// Studio mounts a fresh editor for each workflow, so the snapshot itself never leaves the resource it was copied
// from. Only where the last copy came from outlives the editor (in this tab, across navigation and reloads), so a
// paste elsewhere can say why nothing happened. Cross-resource transfer is deferred to spec tasks T034/T035.
interface CopyOrigin { resourceKind: ActivityReferenceResourceKind; definitionId: string; count: number }
const copyOriginKey = "elsa.studio.activityClipboard.copyOrigin";
let copyOriginInMemory: CopyOrigin | null = null;

function rememberCopyOrigin(origin: CopyOrigin) {
  copyOriginInMemory = origin;
  try { sessionStorage.setItem(copyOriginKey, JSON.stringify(origin)); } catch { /* Storage is optional; memory still covers this page. */ }
}

function readCopyOrigin(): CopyOrigin | null {
  try {
    const stored = JSON.parse(sessionStorage.getItem(copyOriginKey) ?? "null") as Partial<CopyOrigin> | null;
    if (stored && typeof stored.definitionId === "string" && typeof stored.resourceKind === "string" && typeof stored.count === "number")
      return stored as CopyOrigin;
  } catch { /* Fall back to memory. */ }
  return copyOriginInMemory;
}

function refusedPasteStatus(count: number) {
  return count === 1
    ? "The copied activity came from another workflow, so it can't be pasted here yet."
    : "The copied activities came from another workflow, so they can't be pasted here yet.";
}

export function useActivityClipboard({
  draft,
  frames,
  catalogByVersion,
  selectedNodeIds,
  disabled,
  definitionId,
  resourceKind,
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
    return snapshotSelectedActivities({
      selectedNodeIds: nodeIds,
      scopeActivities: scope.slot.activities,
      scopeOwner: scope.owner,
      catalog: catalogByVersion,
      layout: draft.layout,
      presentation: draft.activityPresentation ?? []
    });
  }, [catalogByVersion, disabled, draft, frames]);

  const applySnapshot = useCallback((snapshot: ActivityClipboardSnapshot, status: string, insertAfterNodeId?: string) => {
    if (disabled) return false;
    let selectedNodeId: string | null = null;
    let applied = false;
    // Window-level keydown is outside React's event system, so the document reducer
    // is scheduled instead of flushed. preventDefault and the status line need the
    // recipe result in this turn.
    flushSync(() => {
      editDraft(({ draft: current, frames: currentFrames }) => {
        if (!current) return null;
        const result = pasteActivitiesIntoDraft({
          draft: current,
          frames: currentFrames,
          snapshot,
          resourceKind,
          catalog: catalogByVersion,
          insertAfterNodeId
        });
        if (!result) return null;
        selectedNodeId = result.selectedNodeIds[0] ?? null;
        applied = true;
        return result.draft;
      });
    });
    if (!applied) return false;
    select(selectedNodeId);
    setStatus(status);
    return true;
  }, [catalogByVersion, disabled, editDraft, resourceKind, select, setStatus]);

  const copy = useCallback(() => {
    const snapshot = snapshotFrom(selectedNodeIds);
    if (!snapshot) return false;
    clipboardRef.current = snapshot;
    rememberCopyOrigin({ resourceKind, definitionId, count: snapshot.activities.length });
    setStatus(snapshot.activities.length === 1 ? "Copied 1 activity." : `Copied ${snapshot.activities.length} activities.`);
    return true;
  }, [definitionId, resourceKind, selectedNodeIds, setStatus, snapshotFrom]);

  const paste = useCallback(() => {
    const snapshot = clipboardRef.current;
    if (disabled) return false;
    if (!snapshot) {
      const origin = readCopyOrigin();
      if (origin && (origin.definitionId !== definitionId || origin.resourceKind !== resourceKind)) setStatus(refusedPasteStatus(origin.count));
      return false;
    }
    return applySnapshot(
      snapshot,
      snapshot.activities.length === 1 ? "Pasted 1 activity." : `Pasted ${snapshot.activities.length} activities.`
    );
  }, [applySnapshot, definitionId, disabled, resourceKind, setStatus]);

  const duplicate = useCallback((nodeIds = selectedNodeIds) => {
    const snapshot = snapshotFrom(nodeIds);
    if (!snapshot) return false;
    clipboardRef.current = snapshot;
    rememberCopyOrigin({ resourceKind, definitionId, count: snapshot.activities.length });
    return applySnapshot(
      snapshot,
      snapshot.activities.length === 1 ? "Duplicated 1 activity." : `Duplicated ${snapshot.activities.length} activities.`,
      snapshot.activities.at(-1)?.nodeId
    );
  }, [applySnapshot, definitionId, resourceKind, selectedNodeIds, snapshotFrom]);

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
