import { agentProviderKey, type AgentRow } from "./agentsApi";

/** The collection always opens the Agent workspace, which resolves its authorized current draft first. */
export function agentRowPath(row: AgentRow) {
  const parameters = new URLSearchParams();
  const published = publishedVersion(row);
  if (published) parameters.set("fallbackVersion", published.versionId);
  return `/agents/workspace/${encodeURIComponent(row.definition.definitionId)}${parameters.size ? `?${parameters.toString()}` : ""}`;
}

export function publishedVersion(row: AgentRow) {
  // Only a version attached to this Agent provider can serve as its published fallback.
  const { head, recommendation } = row.lifecycle;
  return [recommendation, head].find(version => version?.providerKey === agentProviderKey) ?? null;
}

export function activityDefinitionVersionPath(definitionId: string, versionId: string) {
  return `/agents/workspace/${encodeURIComponent(definitionId)}/versions/${encodeURIComponent(versionId)}`;
}

/** Where the Agent-owned workspace opens one definition, or one of its exact drafts. */
export function activityDefinitionPath(definitionId: string, draftId?: string) {
  return draftId ? agentWorkspaceDraftPath(definitionId, draftId) : `/agents/workspace/${encodeURIComponent(definitionId)}`;
}

export function agentWorkspaceDraftPath(definitionId: string, draftId: string) {
  return `/agents/workspace/${encodeURIComponent(definitionId)}/drafts/${encodeURIComponent(draftId)}`;
}