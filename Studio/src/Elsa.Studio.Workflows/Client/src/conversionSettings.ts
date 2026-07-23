/**
 * Authored value-conversion settings for a binding edge — the Studio view of the backend's
 * `ArgumentState.conversion` (`AuthoredValueConversionRequest`, foundation #782/PR #818).
 *
 * Wire semantics this module encodes:
 *
 * - An absent request is the legacy default (Auto for activity inputs), so the editor writes a
 *   request only when it differs from that default and clears it when the author returns to it.
 * - Authored `mode` values serialize camelCase ("json"); compiled `conversionPlan` inspection views
 *   serialize their enum values PascalCase ("Json", "TextValue"). The server reads case-insensitively,
 *   so readers here accept both and writers emit the authored camelCase form.
 * - Requests may carry fields Studio doesn't model yet (`limits`, `options`, future additions). All
 *   mutation helpers spread the previous record so unknown fields survive an edit.
 */

import type { ConversionMode, ConversionProfileReference } from "./conversionSource";

export { builtInConversionProfiles, describeInferredSource } from "./conversionSource";
export type { ConversionMode, ConversionProfileReference } from "./conversionSource";

export interface ConversionModeDescriptor {
  mode: ConversionMode;
  displayName: string;
  description: string;
}

export const conversionModeDescriptors: ConversionModeDescriptor[] = [
  { mode: "auto", displayName: "Auto", description: "Deterministic default conversions only." },
  { mode: "none", displayName: "None", description: "Require an exact source and target contract match." },
  { mode: "json", displayName: "JSON", description: "Decode formatted content with the built-in elsa.json@1 profile." },
  { mode: "xml", displayName: "XML", description: "Decode formatted content with the built-in elsa.xml@1 profile." },
  { mode: "profile", displayName: "Profile", description: "Use a registered named conversion profile." }
];

const conversionModes = new Set<string>(conversionModeDescriptors.map(descriptor => descriptor.mode));

export function readConversionMode(request: unknown): ConversionMode {
  if (!isRecord(request)) return "auto";
  const mode = typeof request.mode === "string" ? request.mode.trim().toLowerCase() : "";
  return conversionModes.has(mode) ? mode as ConversionMode : "auto";
}

/**
 * True when the request is absent or the pure legacy default (Auto with nothing else authored) —
 * the same shape `withConversionMode` collapses to null. Requests carrying preserved unknown
 * fields (`limits`, `options`) are not default so the editor never hides them.
 */
export function isDefaultConversion(request: unknown): boolean {
  if (!isRecord(request)) return true;
  return readConversionMode(request) === "auto" && Object.keys(request).every(key => key === "mode");
}

export function readConversionProfile(request: unknown): ConversionProfileReference | null {
  if (!isRecord(request) || !isRecord(request.profile)) return null;
  const id = readString(request.profile.id);
  const version = readString(request.profile.version);
  return id || version ? { id, version } : null;
}

/**
 * The next authored request after selecting `mode`, or null when the result is the pure legacy
 * default (Auto with nothing else authored) — callers then remove the request from the argument.
 */
export function withConversionMode(request: unknown, mode: ConversionMode): Record<string, unknown> | null {
  const next: Record<string, unknown> = isRecord(request) ? { ...request } : {};
  if (mode !== "profile") delete next.profile;
  if (mode === "auto" && Object.keys(next).every(key => key === "mode")) return null;
  next.mode = mode;
  return next;
}

export function withConversionProfile(request: unknown, profile: ConversionProfileReference): Record<string, unknown> {
  const base: Record<string, unknown> = isRecord(request) ? { ...request } : {};
  return { ...base, mode: "profile", profile: { id: profile.id, version: profile.version } };
}

// --- compiled conversion-plan inspection (Executable Inspector) ------------------------------------

/** The display-ready projection of a compiled `ValueConversionPlan` from executable inspection. */
export interface ConversionPlanSummary {
  mode: string;
  operation: string | null;
  profile: ConversionProfileReference | null;
  fingerprint: string | null;
  sourceRepresentation: string | null;
  sourceContract: string | null;
  targetContract: string | null;
}

export function readConversionPlan(value: unknown): ConversionPlanSummary | null {
  if (!isRecord(value)) return null;
  const mode = readString(value.mode);
  if (!mode) return null;
  return {
    mode,
    operation: readString(value.operation) || null,
    profile: readConversionProfile({ profile: value.profile }),
    fingerprint: readString(value.fingerprint) || null,
    sourceRepresentation: formatValueRepresentation(readString(value.sourceRepresentation)),
    sourceContract: formatTypeContract(value.sourceType),
    targetContract: formatTypeContract(value.targetType)
  };
}

/** "String (List)" style label from a wire `ValueTypeDescriptor { alias, collectionKind }`. */
export function formatTypeContract(value: unknown): string | null {
  if (!isRecord(value)) return null;
  const alias = readString(value.alias);
  if (!alias) return null;
  const collectionKind = readString(value.collectionKind);
  return collectionKind && collectionKind.toLowerCase() !== "single" ? `${alias} (${collectionKind})` : alias;
}

const valueRepresentationLabels: Record<string, string> = {
  typedvalue: "Typed value",
  structuredvalue: "Structured value",
  textvalue: "Text",
  formattedcontent: "Formatted content",
  binarycontent: "Binary content",
  durablereference: "Durable reference",
  transientresource: "Transient resource"
};

function formatValueRepresentation(value: string): string | null {
  if (!value) return null;
  return valueRepresentationLabels[value.toLowerCase()] ?? value;
}

/** "sha256:8a808515…" shortened for chips; pair with the full value in a title attribute. */
export function shortenFingerprint(fingerprint: string): string {
  const separator = fingerprint.indexOf(":");
  const digest = separator >= 0 ? fingerprint.slice(separator + 1) : fingerprint;
  const prefix = separator >= 0 ? fingerprint.slice(0, separator + 1) : "";
  return digest.length > 12 ? `${prefix}${digest.slice(0, 12)}…` : fingerprint;
}

const conversionDiagnosticCode = "VF-COER-001";

/**
 * Publish/preflight conversion rejections arrive as plain 400 message text prefixed with the
 * VF-COER-001 code. Append actionable guidance so the author knows the fix lives on the binding's
 * Conversion setting rather than in the activity implementation.
 */
export function decorateConversionDiagnostic(message: string): string {
  if (!message.includes(conversionDiagnosticCode)) return message;
  return `${message} Review the affected input's Conversion setting in the activity properties panel — choose an explicit mode or profile that matches the source content, or set None to require identical contracts.`;
}

function readString(value: unknown): string {
  return typeof value === "string" ? value.trim() : "";
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}
