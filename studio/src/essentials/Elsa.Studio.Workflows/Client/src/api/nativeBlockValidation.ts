import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { canonicalizeStateForWire } from "../activityInputWire";
import { normalizeActivityPresentation } from "../activityPresentation";
import type { WorkflowDraft } from "../workflowTypes";
import { capabilityIds, resolveCapabilityLink } from "./capabilities";
import type { NativeBlockValidation } from "../graph-authoring/NativeBlockCommands";

export async function validateWorkflowCandidate(context: StudioEndpointContext, baseline: WorkflowDraft, candidate: WorkflowDraft): Promise<NativeBlockValidation> {
  const path = await resolveCapabilityLink(context, capabilityIds.workflowDesign, "workflow-draft-candidate-validation", { draftId: baseline.id });
  const result = await context.http.postJson<{ state: string; errors: { message: string }[] }>(path, {
    baseline: {
      id: baseline.id, definitionId: baseline.definitionId, sourceVersionId: baseline.sourceVersionId ?? null,
      authoringRevision: baseline.authoringRevision ?? "", state: canonicalizeStateForWire(baseline.state),
      layout: baseline.layout, activityPresentation: normalizeActivityPresentation(baseline.activityPresentation)
    },
    candidate: { state: canonicalizeStateForWire(candidate.state), layout: candidate.layout, activityPresentation: normalizeActivityPresentation(candidate.activityPresentation) }
  });
  if (!result || !Array.isArray(result.errors) || typeof result.state !== "string") throw new Error("Invalid candidate validation response.");
  return { state: result.state, messages: result.errors.map(item => item.message) };
}
