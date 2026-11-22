import { canonicalizeStateForWire, expandStateFromWire } from "../activityInputWire";
import { normalizeActivityStructures } from "../workflowAdapter";
import type { ActivityCatalogItem } from "../workflowTypes";
import type { NativeBlockContext, NativeBlockDocument } from "./nativeBlocks";

/** The pure planner reads wire bindings; resource editors own expanded input properties. */
export function nativeBlockPlannerContext(context: NativeBlockContext, createPosition: { x: number; y: number }): NativeBlockContext {
  return { ...context, sourceKey: JSON.stringify([context.sourceKey, createPosition.x, createPosition.y]), document: { ...context.document,
    rootActivity: canonicalizeStateForWire({ rootActivity: context.document.rootActivity }).rootActivity ?? context.document.rootActivity
  } };
}

export function nativeBlockEditorDocument(document: NativeBlockDocument, catalog: ActivityCatalogItem[]): NativeBlockDocument {
  // Match the host's ordinary structure normalization before its one history edit,
  // preserving absent leaf structures so the host effect adds no following edit.
  const root = normalizeActivityStructures(document.rootActivity, catalog) ?? document.rootActivity;
  return { ...document, rootActivity: expandStateFromWire({ rootActivity: root }).rootActivity ?? root };
}
