/**
 * The alias of the dynamic kind: any valid JSON value, offered and shown as JSON (spec 1228 FR-017). `Object`,
 * `System.Object` and `Elsa.Any` are older names the server reads as `Any`; activities may still declare the .NET
 * type `System.Text.Json.JsonElement`, which is shown as JSON too.
 */
export const jsonTypeAlias = "Any";

const jsonTypeNames = new Set(["any", "object", "system.object", "elsa.any", "system.text.json.jsonelement"]);

export function isJsonTypeName(typeName: string) {
  return jsonTypeNames.has(typeName.trim().replace(/^global::/, "").replace(/\?$/, "").toLowerCase());
}
