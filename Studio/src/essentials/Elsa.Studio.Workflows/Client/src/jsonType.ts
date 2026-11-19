/** The existing portable alias used by JSON activity contracts and the selectable JSON type. */
export const jsonTypeAlias = "System.Text.Json.JsonElement";

export function isJsonTypeName(typeName: string) {
  return typeName.trim().replace(/^global::/, "").replace(/\?$/, "").toLowerCase() === jsonTypeAlias.toLowerCase();
}
