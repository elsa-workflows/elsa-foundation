export function updateWorkflowDefinitionIdInUrl(source: URL, definitionId: string | null) {
  const url = new URL(source.toString());
  if (definitionId) url.searchParams.set("definition", definitionId);
  else url.searchParams.delete("definition");
  return url;
}
