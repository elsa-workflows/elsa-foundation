import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { getDefinition, listDefinitions } from "../api/workflowDesign";

export function uniqueDefinitionIds(ids: Iterable<string | null | undefined>) {
  return [...new Set([...ids].map(id => id?.trim()).filter((id): id is string => Boolean(id)))];
}

export function resolveNamedIdentity(name: string | null | undefined, id: string) {
  const trimmed = name?.trim();
  return {
    label: trimmed || id,
    secondaryId: trimmed && trimmed !== id ? id : null
  };
}

export async function loadDefinitionDisplayNames(
  context: StudioEndpointContext,
  ids: Iterable<string | null | undefined>
) {
  const wanted = uniqueDefinitionIds(ids);
  const names = new Map<string, string>();
  if (wanted.length === 0) return names;

  try {
    const listed = await listDefinitions(context, {
      search: "",
      state: "all",
      page: 1,
      pageSize: Math.max(wanted.length, 100)
    });
    for (const definition of listed.definitions) {
      if (wanted.includes(definition.id) && definition.name?.trim()) {
        names.set(definition.id, definition.name.trim());
      }
    }
  } catch {
    // Fall through to per-id reads for anything still missing.
  }

  const missing = wanted.filter(id => !names.has(id));
  if (missing.length === 0) return names;

  const resolved = await Promise.all(missing.map(async id => {
    try {
      const details = await getDefinition(context, id);
      const name = details.definition.name?.trim();
      return name ? [id, name] as const : null;
    } catch {
      return null;
    }
  }));
  for (const entry of resolved) {
    if (entry) names.set(entry[0], entry[1]);
  }
  return names;
}
