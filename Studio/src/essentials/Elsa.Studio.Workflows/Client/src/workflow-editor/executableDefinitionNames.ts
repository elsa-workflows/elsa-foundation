import type { DefinitionListRequest } from "../api/workflowDesign";
import type { WorkflowDefinitionsResponse, WorkflowExecutableSummary } from "../workflowTypes";

type ListDefinitionsPage = (request: DefinitionListRequest) => Promise<WorkflowDefinitionsResponse>;

const definitionPageSize = 100;
const maximumDefinitionNamePages = 5;

function uniqueDefinitionIds(ids: Iterable<string | null | undefined>) {
  const result: string[] = [];
  const seen = new Set<string>();

  for (const id of ids) {
    if (typeof id !== "string" || id.trim().length === 0 || seen.has(id)) continue;
    seen.add(id);
    result.push(id);
  }

  return result;
}

/** Returns only workflow definition IDs; version and source identities remain searchable metadata. */
export function getExecutableDefinitionIds(executable: WorkflowExecutableSummary) {
  return uniqueDefinitionIds([
    executable.definitionId,
    ...(executable.references ?? []).map(reference => reference.definitionId)
  ]);
}

/** Loads names through a bounded number of Design pages; unresolved IDs remain usable as ID labels. */
export async function loadDefinitionNames(
  ids: Iterable<string | null | undefined>,
  listPage: ListDefinitionsPage
): Promise<ReadonlyMap<string, string>> {
  const wanted = uniqueDefinitionIds(ids);
  const names = new Map<string, string>();
  if (wanted.length === 0) return names;

  const unresolved = new Set(wanted);
  const seenContinuationTokens = new Set<string>();
  let continuationToken: string | undefined;

  try {
    for (let page = 1; unresolved.size > 0 && page <= maximumDefinitionNamePages; page += 1) {
      const response = await listPage({
        search: "",
        state: "all",
        page,
        pageSize: definitionPageSize,
        includeAllWhenUnpaged: true,
        ...(continuationToken ? { continuationToken } : {})
      });

      for (const definition of response.definitions) {
        if (!unresolved.has(definition.id)) continue;
        unresolved.delete(definition.id);
        const name = definition.name?.trim();
        if (name) names.set(definition.id, name);
      }

      if (!response.isPaged || unresolved.size === 0) break;

      const nextContinuationToken = response.nextContinuationToken;
      if (!nextContinuationToken || seenContinuationTokens.has(nextContinuationToken)) break;
      seenContinuationTokens.add(nextContinuationToken);
      continuationToken = nextContinuationToken;
    }
  } catch {
    // A denied or unavailable Design lookup must not make runtime artifacts unusable; keep names already found.
  }

  return names;
}

/** Matches the legacy definition/source fields plus resolved names and all reference identities. */
export function executableMatchesDefinitionSearch(
  executable: WorkflowExecutableSummary,
  search: string,
  names: ReadonlyMap<string, string>
) {
  const normalizedSearch = search.trim().toLowerCase();
  const values: Array<string | null | undefined> = [
    executable.definitionId,
    executable.definitionVersionId,
    executable.sourceId,
    executable.sourceVersion,
    ...getExecutableDefinitionIds(executable).map(id => names.get(id)),
    ...(executable.references ?? []).flatMap(reference => [
      reference.sourceReferenceId,
      reference.definitionId,
      reference.definitionVersionId,
      reference.sourceId,
      reference.sourceVersion
    ])
  ];

  return values.some(value => value?.toLowerCase().includes(normalizedSearch));
}
