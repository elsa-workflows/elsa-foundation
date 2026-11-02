import type { BpmnElement } from "./bpmnTypes";
import type { BpmnPin } from "../api/bpmnRuntime";

export const directBindingProperty = "elsa.direct.binding";
export const directHumanBinding = JSON.stringify({ formatVersion: 1, kind: "human", claimRequired: false });
export type DirectHumanMode = "human" | "service" | "unbound" | "native" | "unsupported";
export interface DirectServiceBinding {
  formatVersion: 1;
  kind: "nativeService";
  sourceReferenceId: string;
  executable: BpmnPin;
  businessErrorCode: string;
  faultPolicy: { formatVersion: 1; disposition: "FinalFault" | "HoldForRetry"; maxAttempts: number };
}

function readBinding(raw: unknown): Record<string, unknown> | null {
  if (typeof raw !== "string" || raw.length > 16_384 || new TextEncoder().encode(raw).length > 16_384) return null;
  try {
    const value: unknown = JSON.parse(raw);
    return value && typeof value === "object" && !Array.isArray(value) ? value as Record<string, unknown> : null;
  } catch { return null; }
}

function hasKeys(value: unknown, keys: string[]): value is Record<string, unknown> {
  return !!value && typeof value === "object" && !Array.isArray(value)
    && Object.keys(value).length === keys.length && keys.every(key => Object.hasOwn(value, key));
}

/** Only complete current service metadata is editable. Unknown meaning remains byte-for-byte retained. */
export function directServiceBinding(element: BpmnElement): DirectServiceBinding | null {
  if (element.elementType !== "serviceTask" || element.childNodeId || element.listenerNodeId) return null;
  const raw = element.properties?.[directBindingProperty];
  const binding = readBinding(raw);
  if (!hasKeys(binding, ["formatVersion", "kind", "sourceReferenceId", "executable", "businessErrorCode", "faultPolicy"])
    || binding.formatVersion !== 1 || binding.kind !== "nativeService"
    || !hasKeys(binding.executable, ["artifactId", "definitionId", "definitionVersionId", "artifactVersion", "artifactHash"])
    || !hasKeys(binding.faultPolicy, ["formatVersion", "disposition", "maxAttempts"])) return null;
  const policy = binding.faultPolicy;
  // Consume complete string tokens, including escaped quotes in values, before counting property keys.
  const keys = [...(raw as string).matchAll(/"(?:[^"\\]|\\.)*"\s*(:)?/g)].filter(token => token[1] === ":");
  if (keys.length !== 14 || ![binding.sourceReferenceId, binding.businessErrorCode, ...Object.values(binding.executable)]
    .every(value => typeof value === "string" && !!value.trim()) || policy.formatVersion !== 1
    || !["FinalFault", "HoldForRetry"].includes(String(policy.disposition)) || !Number.isInteger(policy.maxAttempts)
    || (policy.maxAttempts as number) < 1 || (policy.maxAttempts as number) > 32
    || policy.disposition === "FinalFault" && policy.maxAttempts !== 1) return null;
  return binding as unknown as DirectServiceBinding;
}

export function directServicePatch(element: BpmnElement, binding: DirectServiceBinding): Partial<BpmnElement> {
  const current = directHumanMode(element);
  if (current === "native" || current === "unsupported") return {};
  const properties = { ...element.properties, [directBindingProperty]: JSON.stringify(binding) };
  const candidate = { ...element, elementType: "serviceTask", properties };
  return directServiceBinding(candidate) ? { elementType: "serviceTask", properties } : {};
}

/** Read an owned binding without normalizing or rewriting its original JSON. */
export function directHumanMode(element: BpmnElement): DirectHumanMode {
  const raw = element.properties?.[directBindingProperty];
  if (element.childNodeId || element.listenerNodeId) return raw === undefined ? "native" : "unsupported";
  if (raw === undefined) return element.elementType === "userTask" ? "human" : "unbound";
  if (directServiceBinding(element)) return "service";
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
      && (binding.claimRequired === undefined || typeof binding.claimRequired === "boolean")
      && element.elementType === "userTask" ? "human" : "unsupported";
  } catch { return "unsupported"; }
}

/** Whether a current direct human binding requires an exact claim before completion. */
export function directHumanClaimRequired(element: BpmnElement): boolean {
  if (directHumanMode(element) !== "human") return false;
  return readBinding(element.properties?.[directBindingProperty])?.claimRequired === true;
}

/** An explicit author edit of a current human binding; it writes the complete closed binding JSON. */
export function directHumanClaimPatch(element: BpmnElement, claimRequired: boolean): Partial<BpmnElement> {
  if (directHumanMode(element) !== "human") return {};
  return { elementType: "userTask", properties: { ...element.properties,
    [directBindingProperty]: JSON.stringify({ formatVersion: 1, kind: "human", claimRequired }) } };
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
