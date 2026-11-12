import { qualifyStudioBackendPath } from "@elsa-workflows/studio-sdk";

export interface SecretBrowse { search: string; query: string; type: string; store: string; scope: string; status: string }
export function readSecretBrowse(search: string): SecretBrowse {
  const query = new URLSearchParams(search);
  return { search: query.get("search") ?? "", query: query.get("query") ?? query.get("search") ?? "", type: query.get("type") ?? "", store: query.get("store") ?? "", scope: query.get("scope") ?? "", status: query.get("status") ?? "" };
}
export function secretPath(browse: SecretBrowse, edit?: string, notice?: "created" | "updated", focus?: string, locationHref?: string, focusCreate = false) {
  const query = new URLSearchParams();
  for (const key of ["search", "type", "store", "scope", "status"] as const) if (browse[key]) query.set(key, browse[key]);
  if (browse.query !== browse.search) query.set("query", browse.query);
  if (notice) query.set("notice", notice);
  if (focus) query.set("focusSecret", focus);
  if (focusCreate) query.set("focusCreate", "1");
  const path = edit === undefined ? "/security/secrets" : edit === "" ? "/security/secrets/new" : `/security/secrets/${encodeURIComponent(edit)}/edit`;
  return qualifyStudioBackendPath(path + (query.size ? `?${query}` : ""), locationHref);
}
