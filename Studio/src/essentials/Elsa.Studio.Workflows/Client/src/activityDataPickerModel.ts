import type { StudioExpressionDescriptor } from "@elsa-workflows/studio-sdk";
import type { ActivityDataBinding, ActivityDataContextResult, ActivityDataShape, ActivityDataSourceFacts, ActivityDataType } from "./api/activityDataSelection";
import { describeOutput, describeReferenceFinding, describeUnavailableSource, type ActivityDataProducerDescription } from "./activityDataSelection";

/** Pure, currently unwired member-picker model (spec 1215, #4309 S2). No function constructs an authored binding. */
export type ActivityDataFamily = "results" | "inputs" | "request" | "variables" | "other";
type MemberKind = "ActivityResultMember" | "VariableMember" | "WorkflowRequestMember" | "InputMember";
export interface ActivityDataMemberReference {
  kind: MemberKind;
  memberPath: readonly string[];
  producerNodeId?: string;
  projectionKey?: string;
  isOptional?: boolean;
  referenceKey?: string;
  declaringScopeId?: string;
  memberKey?: string;
}
interface SourceIdentity {
  role: "ActivityResult" | "Input" | "WorkflowRequest" | "Variable";
  key: string;
  scope?: string;
  projection?: string;
}
export interface ActivityDataPickerLabels {
  describeActivity(nodeId: string): ActivityDataProducerDescription | null;
  describeScope?(scopeId: string): string | null;
  readOnly?: boolean;
}
export interface ActivityDataPickerOption {
  source: ActivityDataSourceFacts;
  label: string;
  disambiguation: string | null;
  shadowed: boolean;
  typeLabel: string | null;
  unavailableReason: string | null;
}

export const activityDataMemberDescriptors: readonly StudioExpressionDescriptor[] = [
  ["ActivityResultMember", "Activity output member"], ["VariableMember", "Variable member"],
  ["WorkflowRequestMember", "Request member"], ["InputMember", "Input member"]
].map(([type, displayName]) => ({ type: type!, displayName: displayName!, editingMode: "reference" }));

/** Bounds match ActivityDataMemberPath: UTF-16 units, not code points; punctuation and empty names are literal. */
export function isActivityDataMemberPath(value: unknown, allowWhole = false): value is string[] {
  return Array.isArray(value) && (allowWhole || value.length > 0) && value.length <= 64 &&
    Array.from(value).every(segment => typeof segment === "string") && value.reduce((size, segment) => size + segment.length, 0) <= 4096;
}
export function appendActivityDataMember(path: readonly string[], segment: string): string[] | null {
  const next = [...path, segment];
  return isActivityDataMemberPath(path, true) && isActivityDataMemberPath(next) ? next : null;
}
export function parentActivityDataMemberPath(path: readonly string[]): string[] | null {
  return isActivityDataMemberPath(path, true) ? path.slice(0, -1) : null;
}
export function activityDataBreadcrumbs(path: readonly string[]) {
  if (!isActivityDataMemberPath(path, true)) return [];
  return [{ key: "[]", label: "Whole value", memberPath: [] as string[] }, ...path.map((segment, index) => {
    const memberPath = path.slice(0, index + 1);
    return { key: JSON.stringify(memberPath), label: memberLabel(segment), memberPath };
  })];
}

export function readActivityDataMemberReference(binding: ActivityDataBinding): ActivityDataMemberReference | null {
  const value = record(binding.value);
  if (!value || Object.prototype.hasOwnProperty.call(value, "path") || !isActivityDataMemberPath(value.memberPath)) return null;
  const memberPath = [...value.memberPath];
  switch (binding.expressionType) {
    case "ActivityResultMember":
      return text(value.producerNodeId) && text(value.projectionKey) && value.producerScopeId == null &&
        (value.isOptional === undefined || typeof value.isOptional === "boolean")
        ? { kind: binding.expressionType, producerNodeId: value.producerNodeId, projectionKey: value.projectionKey, isOptional: value.isOptional === true, memberPath } : null;
    case "VariableMember":
      return text(value.referenceKey) && text(value.declaringScopeId)
        ? { kind: binding.expressionType, referenceKey: value.referenceKey, declaringScopeId: value.declaringScopeId, memberPath } : null;
    case "WorkflowRequestMember":
      return text(value.memberKey) ? { kind: binding.expressionType, memberKey: value.memberKey, memberPath } : null;
    case "InputMember":
      return text(value.referenceKey) ? { kind: binding.expressionType, referenceKey: value.referenceKey, memberPath } : null;
    default: return null;
  }
}

/** Family order is presentation; order within each family is the server's, including nearest-scope-first variables. */
export function groupActivityDataSources(result: ActivityDataContextResult, labels: ActivityDataPickerLabels) {
  if (!current(result, labels)) return [];
  const families: { family: ActivityDataFamily; label: string; options: ActivityDataPickerOption[] }[] = [
    { family: "results", label: "Activity outputs", options: [] }, { family: "inputs", label: "Workflow inputs", options: [] },
    { family: "request", label: "Request members", options: [] }, { family: "variables", label: "Variables", options: [] },
    { family: "other", label: "Unavailable sources", options: [] }
  ];
  const nearest = new Map<string, string>();
  for (const source of result.sources) {
    if (protectedShape(source.shape)) continue;
    const identity = sourceIdentity(source.binding);
    const family = familyOf(source, identity);
    const scope = identity?.scope;
    const option: ActivityDataPickerOption = {
      source, label: sourceLabel(source, labels), disambiguation: null,
      shadowed: family === "variables" && !!scope && nearest.has(source.displayName) && nearest.get(source.displayName) !== scope,
      typeLabel: activityDataTypeLabel(source.type), unavailableReason: sourceReason(result, source)
    };
    if (family === "variables" && scope && !nearest.has(source.displayName)) nearest.set(source.displayName, scope);
    families.find(group => group.family === family)!.options.push(option);
  }
  for (const family of families) for (const option of family.options) {
    const duplicate = family.options.some(other => other !== option && other.label === option.label);
    const identity = sourceIdentity(option.source.binding);
    if (duplicate || identity?.role === "ActivityResult" && labels.describeActivity(identity.key)?.sharesLabel) {
      const sameScope = family.options.some(other => other !== option && other.label === option.label &&
        sourceIdentity(other.source.binding)?.scope === identity?.scope);
      const sameScopeLabel = identity?.scope && family.options.some(other => {
        const otherScope = sourceIdentity(other.source.binding)?.scope;
        return other !== option && other.label === option.label && otherScope && otherScope !== identity.scope &&
          scopeLabel(otherScope, labels) === scopeLabel(identity.scope!, labels);
      });
      option.disambiguation = identity?.scope
        ? `${scopeLabel(identity.scope, labels)}${sameScopeLabel ? ` (${identity.scope})` : ""}${sameScope ? ` · ${identity.key}` : ""}` : option.source.id;
    }
    if (family.options.some(other => other !== option && other.source.id === option.source.id))
      option.unavailableReason = "This source identity is ambiguous.";
  }
  return families.filter(family => family.options.length > 0);
}

/** The supplied shape describes the focused path, not a root type locally treated as a leaf contract. */
export function activityDataMemberBrowser(result: ActivityDataContextResult, sourceId: string,
  path: readonly string[], labels: ActivityDataPickerLabels) {
  const candidates = result.sources.filter(source => source.id === sourceId);
  const source = candidates.length === 1 ? candidates[0] : undefined;
  const shape = source?.shape;
  const identity = sourceIdentity(source?.binding ?? null);
  const value = record(source?.binding?.value);
  const returnedPath = source?.binding ? readActivityDataMemberReference(source.binding)?.memberPath ?? [] : [];
  const unsupported = identity?.role === "ActivityResult" && value?.producerScopeId != null ||
    identity?.role === "WorkflowRequest" && value?.path != null;
  const reason = !current(result, labels) ? "This input cannot select data here."
    : result.memberReadsEnabled !== true ? "Member selection is not enabled on this server."
    : !isActivityDataMemberPath(path, true) ? "The member path exceeds the supported bounds."
    : !source ? "This source is no longer available unambiguously."
    : JSON.stringify(returnedPath) !== JSON.stringify(path) ? "Load the current member path before browsing it."
    : protectedShape(shape) ? "This source is protected."
    : sourceReason(result, source) ?? (unsupported ? "This source does not support member selection." : null);
  const container = shape?.kind !== "scalar" && shape?.kind !== "collection";
  const allowLiteral = !reason && container && (shape?.kind === "map" || shape?.state !== "known");
  const members = reason || !container ? [] : (shape?.members ?? []).flatMap(member => {
    const memberPath = appendActivityDataMember(path, member.serializedName);
    return memberPath && !protectedShape(member.shape) ? [{ ...member, memberPath, key: JSON.stringify(memberPath) }] : [];
  });
  return { members, allowLiteral, reason, state: reason ? "unavailable" : shape?.state ?? "unknown",
    information: reason ?? (shape?.kind === "collection" ? "Collections are selected as a whole; indexing and fan-out are not supported."
      : shape?.kind === "scalar" ? "This value has no object members."
      : allowLiteral ? "Enter one exact member name. Dots and empty names are kept literally; names are case-sensitive."
      : shape?.state === "known" ? null : "The member shape is not known."),
    typeLabel: reason ? null : activityDataTypeLabel(shape?.type),
    typeKnowledge: !reason && identity?.role === "ActivityResult" && shape?.state === "known" && shape.type ? "known" : "deferred"
  };
}

export function describeActivityDataMember(binding: ActivityDataBinding, sources: ActivityDataSourceFacts[],
  labels: ActivityDataPickerLabels, selectedShape?: ActivityDataShape | null) {
  const member = readActivityDataMemberReference(binding);
  const identity = sourceIdentity(binding);
  if (!member || !identity) return null;
  const source = sources.find(candidate => !protectedShape(candidate.shape) && sameIdentity(identity, sourceIdentity(candidate.binding)));
  let label = referenceSourceLabel(source, identity, labels);
  if (identity.role === "ActivityResult" && labels.describeActivity(identity.key)?.sharesLabel || sources.some(candidate => {
    const other = sourceIdentity(candidate.binding);
    return other && !sameIdentity(identity, other) && referenceSourceLabel(candidate, other, labels) === label;
  })) label += ` · ${identity.scope ? `${identity.scope}:` : ""}${identity.key}${identity.projection ? `:${identity.projection}` : ""}`;
  const typeLabel = identity.role === "ActivityResult" && selectedShape?.state === "known" && !protectedShape(selectedShape) ? activityDataTypeLabel(selectedShape.type) : null;
  return { member, sourceLabel: label, text: [label, ...member.memberPath.map(memberLabel)].join(" › "), typeLabel,
    typeKnowledge: typeLabel ? "known" : "deferred" };
}

export function describeActivityDataMemberFinding(finding: { type: string | null; message?: string | null }) {
  switch (finding.type) {
    case "ActivityData/UnsupportedMemberBinding": return "This server cannot use this member reference.";
    case "ActivityData/InvalidSource": return "This source does not support member selection.";
    case "ActivityData/InvalidMemberPath": return "Use at most 64 exact member names and 4096 characters in total.";
    case "ActivityData/SourceRestricted": return "This input does not accept this source.";
    case "ActivityData/SelectedMemberContractUnknown": return "The member type cannot be established for this conversion.";
    default: return describeReferenceFinding({ type: finding.type ?? "", message: "" }) ?? "This source cannot be used here.";
  }
}

export function activityDataTypeLabel(type: ActivityDataType | null | undefined): string | null {
  if (!type?.alias) return null;
  switch (type.collectionKind) {
    case "Single": return type.alias;
    case "Array": return `${type.alias}[]`;
    case "List": return `List<${type.alias}>`;
    case "HashSet": return `HashSet<${type.alias}>`;
    case "Dictionary": return `Dictionary<String, ${type.alias}>`;
    default: return `${type.alias} (collection kind unknown)`;
  }
}

function sourceIdentity(binding: ActivityDataBinding | null): SourceIdentity | null {
  if (!binding) return null;
  const value = record(binding.value);
  if (!value || binding.expressionType.endsWith("Member") && !readActivityDataMemberReference(binding)) return null;
  switch (binding.expressionType) {
    case "ActivityResult": case "ActivityResultMember":
      return text(value.producerNodeId) && text(value.projectionKey) ? { role: "ActivityResult", key: value.producerNodeId, projection: value.projectionKey } : null;
    case "Variable": case "VariableMember":
      return text(value.referenceKey) && text(value.declaringScopeId) ? { role: "Variable", key: value.referenceKey, scope: value.declaringScopeId } : null;
    case "Input": case "InputMember": return text(value.referenceKey) ? { role: "Input", key: value.referenceKey } : null;
    case "WorkflowRequest": case "WorkflowRequestMember": return text(value.memberKey) ? { role: "WorkflowRequest", key: value.memberKey } : null;
    default: return null;
  }
}
function familyOf(source: ActivityDataSourceFacts, identity: SourceIdentity | null): ActivityDataFamily {
  if (identity?.role === "ActivityResult" || source.activityResultSource) return "results";
  if (identity?.role === "Variable") return "variables";
  if (identity?.role === "Input") return "inputs";
  // These are server family prefixes, not parsed source keys: a workflow input is also a WorkflowRequest binding.
  if (identity?.role === "WorkflowRequest" && source.id.startsWith("input:")) return "inputs";
  if (identity?.role === "WorkflowRequest" && source.id.startsWith("request:")) return "request";
  return "other";
}
function sourceLabel(source: ActivityDataSourceFacts, labels: ActivityDataPickerLabels) {
  const identity = source.activityResultSource;
  if (!identity) return source.displayName;
  const producer = labels.describeActivity(identity.producerNodeId);
  return `${producer?.label ?? identity.producerNodeId} › ${describeOutput(identity.projectionKey, producer)}`;
}
function referenceSourceLabel(source: ActivityDataSourceFacts | undefined, identity: SourceIdentity, labels: ActivityDataPickerLabels) {
  const label = source ? sourceLabel(source, labels) : identity.role === "ActivityResult"
    ? `${labels.describeActivity(identity.key)?.label ?? identity.key} › ${describeOutput(identity.projection!, labels.describeActivity(identity.key))}` : identity.key;
  return identity.role === "Variable" ? `${label} (${scopeLabel(identity.scope!, labels)})` : label;
}

function sourceReason(result: ActivityDataContextResult, source: ActivityDataSourceFacts): string | null {
  const identity = sourceIdentity(source.binding);
  if (!source.binding) return source.activityResultSource ? describeUnavailableSource(source.reasonCode, source.activityResultSource.projectionKey)
    : "This source cannot be selected for this input.";
  if (!identity || familyOf(source, identity) === "other") return "This source type is not supported by this version of Studio.";
  if (identity.role === "ActivityResult" && source.activityResultSource &&
    (identity.key !== source.activityResultSource.producerNodeId || identity.projection !== source.activityResultSource.projectionKey))
    return "The source identity is inconsistent.";
  // The server-published binding admits the read; conditional availability/reasons can describe an optional read.
  if (source.binding.expressionType.endsWith("Member") && result.memberReadsEnabled !== true) return "Member selection is not enabled on this server.";
  const allowed = result.target?.constraint?.allowedExpressionTypes;
  return allowed && !allowed.some(role => role.toLowerCase() === identity.role.toLowerCase()) ? "This input does not accept this source." : null;
}
function current(result: ActivityDataContextResult, labels: ActivityDataPickerLabels) {
  return (result.state === "supported" || result.state === "empty") && !!result.target && !labels.readOnly &&
    result.target.isSensitive !== true && result.target.isCredential !== true;
}
function protectedShape(shape: ActivityDataShape | null | undefined) { return shape?.isSensitive === true || shape?.isCredential === true; }
function sameIdentity(a: SourceIdentity, b: SourceIdentity | null) { return !!b && a.role === b.role && a.key === b.key && a.scope === b.scope && a.projection === b.projection; }
function scopeLabel(scope: string, labels: ActivityDataPickerLabels) { return labels.describeScope?.(scope) || (scope === "workflow" ? "Workflow" : scope === "activity-graph" ? "Activity graph" : scope); }
function memberLabel(segment: string) { return /^[A-Za-z_$][A-Za-z0-9_$]*$/.test(segment) ? segment : JSON.stringify(segment); }
function text(value: unknown): value is string { return typeof value === "string" && value.length > 0; }
function record(value: unknown): Record<string, unknown> | null { return value && typeof value === "object" && !Array.isArray(value) ? value as Record<string, unknown> : null; }
