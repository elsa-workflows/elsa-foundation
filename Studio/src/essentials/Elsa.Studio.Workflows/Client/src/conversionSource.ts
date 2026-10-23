export type ConversionMode = "auto" | "none" | "json" | "xml" | "profile";

export interface ConversionProfileReference {
  id: string;
  version: string;
}

/** The profiles every foundation host ships; the fallback when no listing endpoint is available. */
export const builtInConversionProfiles: ConversionProfileReference[] = [
  { id: "elsa.json", version: "1" },
  { id: "elsa.xml", version: "1" }
];

/**
 * Describes the authored representation entering a conversion edge.
 *
 * This small helper is separated from the full conversion-settings module because activity node
 * summaries are part of the eager Workflows registration closure, while conversion controls are
 * inspector-only UI loaded with the deferred Workflow Editor.
 */
export function describeInferredSource(expressionType: string, value: unknown, mode: ConversionMode): string {
  if (mode === "json") return "JSON content";
  if (mode === "xml") return "XML content";
  if (mode === "profile") return "Formatted content";
  const type = expressionType.trim().toLowerCase();
  if (type === "" || type === "literal" || type === "object") {
    return value != null && typeof value === "object" ? "Structured value" : "Text";
  }
  if (type === "variable") return "Variable value";
  if (type === "input") return "Workflow input";
  return "Expression result";
}
