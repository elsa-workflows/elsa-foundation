import type { AgentProposalStatus } from "./agentTypes";

// AgentActionProposalStatus ordinals are a wire contract; append new values without reordering.
const proposalStatuses: readonly AgentProposalStatus[] = [
  "draft", "awaiting-approval", "approved", "denied", "edited", "expired", "executed",
  "failed", "cancelled", "executing", "awaiting-client", "uncertain", "performed"
];

/** Reads supported server enum names/ordinals without inventing an approval or execution outcome. */
export function readAgentProposalStatus(value: unknown): AgentProposalStatus | undefined {
  if (typeof value === "number")
    return Number.isInteger(value) && value >= 0 ? proposalStatuses[value] : undefined;
  if (typeof value !== "string") return undefined;
  const normalized = value.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
  return proposalStatuses.find(status => status === normalized);
}
