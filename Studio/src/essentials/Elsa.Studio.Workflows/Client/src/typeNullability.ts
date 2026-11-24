/** Authored member nullability follows the selected type capability. CLR input descriptors keep their own metadata. */
export function typeAllowsNull(type: { alias: string; collectionKind: string; supportsNull?: boolean },
  capabilities: readonly { alias: string; supportsNull?: boolean }[] = []): boolean {
  if (type.supportsNull !== undefined) return type.supportsNull;
  if (type.collectionKind !== "Single" && type.collectionKind !== "Scalar") return true;
  const declared = capabilities.find(capability => capability.alias === type.alias)?.supportsNull;
  if (declared !== undefined) return declared;
  // The built-in fallback is used while authored/test-drive metadata is loading; contributed types use their catalog.
  return ["string", "any", "object", "jsonElement", "jsonNode", "uri", "text.json.jsonelement", "text.json.nodes.jsonnode"].map(alias => alias.toLowerCase())
    .includes(type.alias.replace(/^System\./, "").replace(/^Elsa\./, "").toLowerCase());
}
