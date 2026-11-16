import type { NativeBlockDocument } from "./nativeBlocks";
import { promotionMaterialJson } from "./blockPromotionTarget";

/**
 * A Workflow editor's resource-owned references are its draft state without the root. The planner returns them with
 * any references to block results rewritten to the call; the replacement applies them with the new root in one edit.
 */
export function workflowPromotionState<TState extends { rootActivity?: unknown }>(state: TState, document: NativeBlockDocument, referenceContext: unknown[]): TState {
  const rewritten = referenceContext[0];
  if (!rewritten || typeof rewritten !== "object" || Array.isArray(rewritten))
    throw new Error("The promotion did not return the workflow's own declarations.");
  return { ...state, ...(rewritten as Partial<TState>), rootActivity: document.rootActivity };
}

/**
 * An Activity Definition graph keeps its public contract outside the graph editor, so a proposal that would change it
 * is refused (null). Graph-owned members besides the root (variables, output and outcome mappings) take their
 * rewritten values; the root and layout come from the replaced document.
 */
export function activityGraphPromotionPayload(contract: unknown, payload: Record<string, unknown>, rootActivity: unknown, referenceContext: unknown[]): Record<string, unknown> | null {
  if (promotionMaterialJson(referenceContext[0]) !== promotionMaterialJson(contract)) return null;
  const rewritten = referenceContext[1];
  const rest = rewritten && typeof rewritten === "object" && !Array.isArray(rewritten) ? rewritten as Record<string, unknown> : {};
  return { ...payload, ...rest, rootActivity };
}
