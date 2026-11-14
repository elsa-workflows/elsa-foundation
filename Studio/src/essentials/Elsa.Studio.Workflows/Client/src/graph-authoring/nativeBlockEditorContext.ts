import { canonicalizeStateForWire, expandStateFromWire } from "../activityInputWire";
import type { NativeBlockContext, NativeBlockDocument } from "./nativeBlocks";

/** The pure planner reads wire bindings; resource editors own expanded input properties. */
export function nativeBlockPlannerContext(context: NativeBlockContext): NativeBlockContext {
  return { ...context, document: { ...context.document,
    rootActivity: canonicalizeStateForWire({ rootActivity: context.document.rootActivity }).rootActivity ?? context.document.rootActivity
  } };
}

export function nativeBlockEditorDocument(document: NativeBlockDocument): NativeBlockDocument {
  return { ...document, rootActivity: expandStateFromWire({ rootActivity: document.rootActivity }).rootActivity ?? document.rootActivity };
}
