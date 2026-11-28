import { StudioHttpError } from "@elsa-workflows/studio-sdk";
import {
  ApiCapabilityUnavailableError,
  ApiCapabilityVersionMismatchError
} from "./api/capabilities";

export function proposalFailure(error: unknown): { phase: "unavailable" | "stale" | "failed"; message: string } {
  if (error instanceof ApiCapabilityUnavailableError || error instanceof ApiCapabilityVersionMismatchError)
    return { phase: "unavailable", message: "This backend does not advertise provider contract proposals. Studio will not infer or probe an alternate route." };
  if (isStaleProposal(error))
    return { phase: "stale", message: "The proposal binding became stale before generation completed. No changes were retained or applied." };
  if (error instanceof StudioHttpError && (error.status === 401 || error.status === 403 || error.status === 404))
    return { phase: "unavailable", message: "Provider proposals are unavailable in this authorized context. No hidden identity or proposal content is shown." };
  return { phase: "failed", message: "Provider proposal generation failed. Editing and autosave remain available; retry after the next saved revision." };
}

export function proposalApplyFailure(error: unknown) {
  if (error instanceof ApiCapabilityUnavailableError || error instanceof ApiCapabilityVersionMismatchError)
    return "Proposal apply is no longer advertised. Local content is unchanged and Studio did not try an alternate route.";
  if (error instanceof StudioHttpError && (error.status === 401 || error.status === 403 || error.status === 404))
    return "Proposal apply is unavailable in this authorized context. Local content is unchanged.";
  return "The selected proposal changes could not be applied. Local contract and implementation content remain unchanged.";
}

export function isStaleProposal(error: unknown) {
  return error instanceof StudioHttpError && error.status === 409;
}

export function isProposalAbort(error: unknown) {
  return error instanceof DOMException && error.name === "AbortError" ||
    error instanceof Error && error.name === "AbortError";
}
