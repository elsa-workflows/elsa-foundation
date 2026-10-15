export function resolveDefinitionPublicationStatus(definition: {
  draftId?: string | null;
  latestVersionId?: string | null;
  latestVersion?: string | null;
  versionCount: number;
  deletedAt?: string | null;
  publicationState?: string | null;
}) {
  if (definition.deletedAt) return "deleted";
  const state = definition.publicationState;
  if (state === "retracted" || state === "retired" || state === "draft") return state;
  if (state === "published") {
    return definition.draftId ? "published-with-draft" : "published";
  }
  if (state) return "draft";
  if (definition.latestVersionId || definition.latestVersion || definition.versionCount) {
    return definition.draftId ? "published-with-draft" : "published";
  }
  return "draft";
}
