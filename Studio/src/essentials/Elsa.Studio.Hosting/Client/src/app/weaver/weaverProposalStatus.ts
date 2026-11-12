import type { WeaverProposalStatus } from "./weaverTypes";

// WeaverActionProposalStatus ordinals are a wire contract; append new values without reordering.
const proposalStatuses: readonly WeaverProposalStatus[] = [
  "draft", "awaiting-approval", "approved", "denied", "edited", "expired", "executed",
  "failed", "cancelled", "executing", "awaiting-client", "uncertain", "performed"
];

/** Reads supported server enum names/ordinals without inventing an approval or execution outcome. */
export function readWeaverProposalStatus(value: unknown): WeaverProposalStatus | undefined {
  if (typeof value === "number")
    return Number.isInteger(value) && value >= 0 ? proposalStatuses[value] : undefined;
  if (typeof value !== "string") return undefined;
  const normalized = value.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
  return proposalStatuses.find(status => status === normalized);
}
