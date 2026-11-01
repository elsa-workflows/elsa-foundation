import type { BpmnElement } from "./bpmnTypes";

export const directBindingProperty = "elsa.direct.binding";
export const directHumanBinding = JSON.stringify({ formatVersion: 1, kind: "human", claimRequired: false });
export type DirectHumanMode = "human" | "unbound" | "native" | "unsupported";

/** Read an owned binding without normalizing or rewriting its original JSON. */
export function directHumanMode(element: BpmnElement): DirectHumanMode {
  const raw = element.properties?.[directBindingProperty];
  if (element.childNodeId) return raw === undefined ? "native" : "unsupported";
  if (raw === undefined) return element.elementType === "userTask" ? "human" : "unbound";
  if (typeof raw !== "string" || raw.length > 16_384 || new TextEncoder().encode(raw).length > 16_384) return "unsupported";
  try {
    const value: unknown = JSON.parse(raw);
    if (!value || typeof value !== "object" || Array.isArray(value)) return "unsupported";
    const binding = value as Record<string, unknown>;
    const keys = Object.keys(binding);
    // This closed human schema contains only scalar values. Counting key tokens also refuses
    // duplicate/escaped duplicate keys that JSON.parse would otherwise silently overwrite.
    const keyTokens = raw.match(/"(?:[^"\\]|\\.)*"\s*:/g) ?? [];
    return keys.every(key => ["formatVersion", "kind", "claimRequired"].includes(key)) && keyTokens.length === keys.length
      && binding.formatVersion === 1 && binding.kind === "human"
      && (binding.claimRequired === undefined || binding.claimRequired === false)
      && element.elementType === "userTask" ? "human" : "unsupported";
  } catch { return "unsupported"; }
}

/** An explicit author edit. Native children and unrecognized meaning require correction before rebinding. */
export function directHumanPatch(element: BpmnElement, mode: "human" | "unbound"): Partial<BpmnElement> {
  const current = directHumanMode(element);
  if (current === "native" || current === "unsupported") return {};
  const properties = { ...element.properties };
  if (mode === "human") properties[directBindingProperty] = directHumanBinding;
  else delete properties[directBindingProperty];
  return { elementType: mode === "human" ? "userTask" : "task", properties };
}
