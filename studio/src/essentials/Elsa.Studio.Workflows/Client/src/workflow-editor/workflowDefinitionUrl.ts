export function updateWorkflowDefinitionIdInUrl(source: URL, definitionId: string | null, requestedPanel: "artifacts" | null = null) {
  const url = new URL(source.toString());
  if (definitionId) url.searchParams.set("definition", definitionId);
  else url.searchParams.delete("definition");
  if (definitionId && requestedPanel) url.searchParams.set("panel", requestedPanel);
  else url.searchParams.delete("panel");
  return url;
}
