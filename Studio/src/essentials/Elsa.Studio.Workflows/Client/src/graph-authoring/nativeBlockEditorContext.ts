import { canonicalizeStateForWire, expandStateFromWire } from "../activityInputWire";
import { normalizeActivityStructures } from "../workflowAdapter";
import type { ActivityCatalogItem } from "../workflowTypes";
import type { NativeBlockContext, NativeBlockDocument } from "./nativeBlocks";

/** The pure planner reads wire bindings; resource editors own expanded input properties. */
export function nativeBlockPlannerContext(context: NativeBlockContext): NativeBlockContext {
  return { ...context, document: { ...context.document,
    rootActivity: canonicalizeStateForWire({ rootActivity: context.document.rootActivity }).rootActivity ?? context.document.rootActivity
  } };
}

export function nativeBlockEditorDocument(document: NativeBlockDocument, catalog: ActivityCatalogItem[]): NativeBlockDocument {
  // Match the host's ordinary structure normalization before its one history edit. In particular,
  // generated leaf transfers need explicit null structure, so the host effect adds no following edit.
  const root = normalizeActivityStructures(document.rootActivity, catalog) ?? document.rootActivity;
  return { ...document, rootActivity: expandStateFromWire({ rootActivity: root }).rootActivity ?? root };
}
