import type { BpmnElement } from "./bpmnTypes";
import type { BpmnPin } from "../api/bpmnRuntime";
import { parseTriggerResponse, serializeTriggerPayload } from "../standalone-triggers/json";
import type { JsonValue } from "../standalone-triggers/types";

export const directBindingProperty = "elsa.direct.binding";
export const directHumanBinding = JSON.stringify({ formatVersion: 1, kind: "human", claimRequired: false });
export type DirectHumanMode = "human" | "service" | "unbound" | "native" | "unsupported";
interface DirectServiceBindingBase {
  kind: "nativeService";
  sourceReferenceId: string;
  executable: BpmnPin;
  businessErrorCode: string;
  faultPolicy: { formatVersion: 1; disposition: "FinalFault" | "HoldForRetry"; maxAttempts: number };
}
export type DirectWorkerInput = { kind: "Input"; name: string } | { kind: "Literal"; value: JsonValue };
export type DirectServiceBinding = DirectServiceBindingBase & ({ formatVersion: 1; inputs?: never } | { formatVersion: 2; inputs: Record<string, DirectWorkerInput> });

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
  const version = binding?.formatVersion;
  if (!hasKeys(binding, ["formatVersion", "kind", "sourceReferenceId", "executable", "businessErrorCode", "faultPolicy", ...(version === 2 ? ["inputs"] : [])])
    || binding.formatVersion !== 1 && binding.formatVersion !== 2 || binding.kind !== "nativeService"
    || !hasKeys(binding.executable, ["artifactId", "definitionId", "definitionVersionId", "artifactVersion", "artifactHash"])
    || !hasKeys(binding.faultPolicy, ["formatVersion", "disposition", "maxAttempts"])) return null;
  const policy = binding.faultPolicy;
  // Consume complete string tokens, including escaped quotes in values, before counting property keys.
  const keys = [...(raw as string).matchAll(/"(?:[^"\\]|\\.)*"\s*(:)?/g)].filter(token => token[1] === ":");
  if (keys.length !== countKeys(binding) || ![binding.sourceReferenceId, binding.businessErrorCode, ...Object.values(binding.executable)]
    .every(value => typeof value === "string" && !!value.trim()) || policy.formatVersion !== 1
    || !["FinalFault", "HoldForRetry"].includes(String(policy.disposition)) || !Number.isInteger(policy.maxAttempts)
    || (policy.maxAttempts as number) < 1 || (policy.maxAttempts as number) > 32
    || policy.disposition === "FinalFault" && policy.maxAttempts !== 1) return null;
  if (version === 2 && (!binding.inputs || typeof binding.inputs !== "object" || Array.isArray(binding.inputs)
    || Object.keys(binding.inputs).length === 0 || Object.entries(binding.inputs).some(([name, source]) => !name.trim()
      || !(hasKeys(source, ["kind", "name"]) && source.kind === "Input" && typeof source.name === "string" && !!source.name.trim()
        || hasKeys(source, ["kind", "value"]) && source.kind === "Literal")))) return null;
  return parseTriggerResponse<DirectServiceBinding>(raw as string);
}

function countKeys(value: unknown): number {
  if (Array.isArray(value)) return value.reduce((total, item) => total + countKeys(item), 0);
  if (!value || typeof value !== "object") return 0;
  return Object.entries(value).reduce((total, [, item]) => total + 1 + countKeys(item), 0);
}

export function directServicePatch(element: BpmnElement, binding: DirectServiceBinding): Partial<BpmnElement> {
  const current = directHumanMode(element);
  if (current === "native" || current === "unsupported") return {};
  const properties = { ...element.properties, [directBindingProperty]: serializeTriggerPayload(binding) };
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
