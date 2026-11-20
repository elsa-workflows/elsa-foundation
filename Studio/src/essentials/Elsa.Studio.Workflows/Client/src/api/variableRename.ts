import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { canonicalizeStateForWire } from "../activityInputWire";
import type { VariableRenamePlan } from "../variableRename";
import type { WorkflowDefinitionState } from "../workflowTypes";
import { capabilityIds, hasCapabilityLink, resolveCapabilityLink } from "./capabilities";

// Kept apart from workflowDesign.ts, which the Definitions landing path loads: only the editor plans renames.
/**
 * Plans how the draft's scripts follow a variable rename (spec 1222 FR-021, FR-022). `state` must still carry the
 * variable's old name. Resolves to null when the host does not advertise the rename plan: Studio then renames the
 * declaration alone and tells the author the scripts were not checked.
 */
export async function planVariableRename(
  context: StudioEndpointContext,
  state: WorkflowDefinitionState,
  scopeId: string,
  referenceKey: string,
  newName: string,
  signal?: AbortSignal
): Promise<VariableRenamePlan | null> {
  if (!await hasCapabilityLink(context, capabilityIds.workflowDesign, "scoped-variable-rename-plan")) return null;
  const path = await resolveCapabilityLink(context, capabilityIds.workflowDesign, "scoped-variable-rename-plan");
  const response = await context.http.postJson<Partial<VariableRenamePlan>>(
    path,
    { state: canonicalizeStateForWire(state), scopeId, referenceKey, newName },
    { signal });
  return {
    oldName: typeof response?.oldName === "string" ? response.oldName : "",
    newName: typeof response?.newName === "string" ? response.newName : newName,
    edits: Array.isArray(response?.edits) ? response.edits : [],
    flags: Array.isArray(response?.flags) ? response.flags : []
  };
}
