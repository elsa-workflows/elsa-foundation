import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type { DraftValidationError, DraftValidationsResponse } from "../workflowTypes";
import { ApiCapabilityUnavailableError, capabilityIds, resolveCapabilityLink } from "./capabilities";

// Editor-only draft-validation reads (foundation #937). Kept in a dedicated module — imported solely by
// the deferred workflow-editor surface — so it stays out of the authenticated Definitions landing bundle.

/**
 * Reports whether the backend advertises the draft-validations relation (foundation #937). Older
 * servers omit it; callers use this to hide the validations panel rather than erroring.
 */
export async function isDraftValidationsAvailable(context: StudioEndpointContext): Promise<boolean> {
  try {
    await resolveCapabilityLink(context, capabilityIds.workflowDesign, "workflow-draft-validations", { draftId: "probe" });
    return true;
  } catch (error) {
    if (error instanceof ApiCapabilityUnavailableError) return false;
    throw error;
  }
}

/**
 * Fetches the server-derived validation errors for a draft. Returns `null` when the backend does not
 * advertise the relation (graceful degradation for older servers) so the caller can hide the panel.
 */
export async function getDraftValidations(
  context: StudioEndpointContext,
  draftId: string,
  signal?: AbortSignal
): Promise<DraftValidationError[] | null> {
  let path: string;
  try {
    path = await resolveCapabilityLink(context, capabilityIds.workflowDesign, "workflow-draft-validations", { draftId });
  } catch (error) {
    if (error instanceof ApiCapabilityUnavailableError) return null;
    throw error;
  }
  const response = await context.http.getJson<DraftValidationsResponse>(path, { signal });
  return Array.isArray(response?.errors)
    ? response.errors.filter((error): error is DraftValidationError =>
      !!error && typeof error.message === "string" && typeof error.code === "string")
    : [];
}
