import type { WorkflowDefinitionSummary } from "../workflowTypes";

export function resolveDefinitionPublicationStatus(definition: Pick<
  WorkflowDefinitionSummary,
  "draftId" | "latestVersionId" | "latestVersion" | "versionCount" | "deletedAt" | "publicationState"
>) {
  if (definition.deletedAt) return "deleted";
  const state = definition.publicationState;
  if (state === "retracted" || state === "retired" || state === "draft") return state;
  if (state === "published" || definition.latestVersionId || definition.latestVersion || definition.versionCount) {
    return definition.draftId ? "published-with-draft" : "published";
  }
  return "draft";
}
