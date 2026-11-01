/** Loading/absent fallback for the Executable Inspector page title and canvas heading. */
export function executableInspectorFallbackTitle(artifactId: string | null | undefined) {
  const id = artifactId?.trim();
  return id ? `Executable ${id}` : "Executable";
}

/** Workflow display name when the source definition is known; otherwise the artifact fallback. */
export function resolveExecutableInspectorDisplayName(
  definitionName: string | null | undefined,
  artifactId: string
) {
  return definitionName?.trim() || executableInspectorFallbackTitle(artifactId);
}

/** Page H2: display name and version when resolved, otherwise `Executable` plus the artifact ID. */
export function formatExecutableInspectorPageTitle(
  definitionName: string | null | undefined,
  artifactId: string,
  version?: string | null
) {
  const name = definitionName?.trim();
  if (!name) return executableInspectorFallbackTitle(artifactId);
  const artifactVersion = version?.trim();
  return artifactVersion ? `${name} · ${artifactVersion}` : name;
}

export function describeExecutableReferenceSelection(selection: string, sourceReferenceId: string) {
  const normalized = selection.trim().toLowerCase();
  if (normalized === "requested") return `Showing requested reference ${sourceReferenceId}`;
  if (normalized === "newest-live") return `Showing newest live reference ${sourceReferenceId}`;
  if (normalized === "newest") return `Showing newest reference ${sourceReferenceId} (no live references remain)`;
  return `Showing reference ${sourceReferenceId}`;
}

export interface ExecutableInspectorIdentity {
  definitionName: string | null;
  artifactId: string;
  version: string | null;
}
