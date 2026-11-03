import type { StudioActivityDescriptor, StudioActivityInputDescriptor } from "@elsa-workflows/studio-sdk";
import type { ActivityCatalogItem, ActivityNode, ValidationError } from "../workflowTypes";
import { getInputPropertyName, isEmptyExpressionValue, secretSyntax } from "../activityProperties";
import { isMaskedInput, readSecretReference, type SecretReferenceView } from "../maskedInput";
import { errorType, parseValidationErrorPath } from "../validationDiagnostics";
import { isPlainRecord } from "../workflowProperties";

// Spec 1193 slice D (FR-014, FR-016–FR-018): what an explanation of the active draft may read, beyond its graph. The server
// admits each part strictly (WorkflowAgentExplainContext), so the shapes and bounds here are the same contract.

/** The diagnostics one snapshot discloses; beyond this the count of the rest is reported. */
export const workflowContextMaxDiagnostics = 64;
const maxDiagnosticMessage = 1024;
/** The declared properties of the selected activity one snapshot discloses. */
const maxProperties = 64;
/** The longest content one property discloses; longer content is cut and marked truncated. */
export const workflowPropertyContentLimit = 2048;
/** The content all disclosed properties may carry together. */
export const workflowPropertyContentBudget = 16384;
const maxGraphActivities = 64;

export interface WorkflowContextDiagnostic {
  severity: "error" | "warning";
  type?: string;
  nodeId?: string;
  property?: string;
  message: string;
}

/**
 * The designer's validation, as the validation panel shows it, by activity and input. Only these are disclosed: Weaver
 * explains the designer's own findings and runs no validation of its own (FR-018). Messages are resource content.
 */
export function collectWorkflowContextDiagnostics(errors: ValidationError[]) {
  const diagnostics = errors.slice(0, workflowContextMaxDiagnostics).map(error => {
    const path = parseValidationErrorPath(error.path);
    const type = bounded(errorType(error), 128);
    const property = path.nodeId && path.bag === "inputs" ? bounded(path.referenceKey, 256) : null;
    const nodeId = bounded(path.nodeId, 256);
    const diagnostic: WorkflowContextDiagnostic = {
      // A missing severity reads as an error, as the designer's own gate reads it (spec 261 FR-515).
      severity: String(error.severity ?? "Error").toLowerCase() === "warning" ? "warning" : "error",
      message: bounded(error.message, maxDiagnosticMessage) ?? "Workflow validation issue."
    };
    if (type) diagnostic.type = type;
    if (nodeId) diagnostic.nodeId = nodeId;
    if (nodeId && property) diagnostic.property = property;
    return diagnostic;
  });
  const omitted = Math.max(0, errors.length - diagnostics.length);
  return { diagnostics, omitted };
}

export type WorkflowPropertyValue =
  | { kind: "unset" }
  | { kind: "expression"; expressionType: string; content: string; truncated: boolean; length: number }
  | { kind: "withheld"; expressionType: string }
  | { kind: "secretReference"; secretReference: SecretReferenceView };

export interface WorkflowContextProperty {
  name: string;
  referenceKey: string;
  typeName: string;
  isCredential: boolean;
  isSensitive: boolean;
  value: WorkflowPropertyValue;
}

export interface WorkflowSelectedActivityContext {
  nodeId: string;
  activityVersionId: string;
  truncated: boolean;
  properties: WorkflowContextProperty[];
}

/**
 * The selected activity's declared properties (FR-016, FR-017): each input's expression type and bounded content. A
 * masked input (sensitive, credential or password-hinted) never discloses what it holds: a value is reported present and
 * withheld, and a bound Secret Reference by name, type and scope only. A Secret syntax that names no Secret is withheld too.
 */
export function collectSelectedActivityContext(node: ActivityNode, descriptor: StudioActivityDescriptor): WorkflowSelectedActivityContext {
  const inputs = (descriptor.inputs ?? []) as StudioActivityInputDescriptor[];
  const properties: WorkflowContextProperty[] = [];
  const keys = new Set<string>();
  let budget = workflowPropertyContentBudget;
  for (const input of inputs) {
    if (properties.length >= maxProperties) break;
    const referenceKey = bounded(getInputPropertyName(input), 256);
    if (!referenceKey || keys.has(referenceKey)) continue;
    keys.add(referenceKey);
    const isCredential = input.isCredential === true;
    const isSensitive = isMaskedInput(input);
    const value = describeValue(node, input, isSensitive, budget);
    if (value.kind === "expression") budget -= value.content.length;
    properties.push({
      name: bounded(input.name, 256) ?? referenceKey, referenceKey,
      typeName: clip(readTypeName(input) ?? "", 256), isCredential, isSensitive, value
    });
  }
  const truncated = properties.length < new Set(inputs.map(input => getInputPropertyName(input))).size
    || properties.some(property => property.value.kind === "expression" && property.value.truncated);
  return { nodeId: node.nodeId, activityVersionId: node.activityVersionId, truncated, properties };
}

function describeValue(node: ActivityNode, input: StudioActivityInputDescriptor, masked: boolean, budget: number): WorkflowPropertyValue {
  const stored = node[getInputPropertyName(input)];
  let expressionType: string;
  let value: unknown;
  if (input.isWrapped === false) {
    if (stored === undefined || stored === null) return { kind: "unset" };
    expressionType = "Literal";
    value = stored;
  } else {
    const expression = isPlainRecord(stored) && isPlainRecord(stored.expression) ? stored.expression : null;
    if (!expression) return { kind: "unset" };
    expressionType = typeof expression.type === "string" && expression.type.trim() ? clip(expression.type, 128) : "Literal";
    value = expression.value;
  }
  if (expressionType === secretSyntax) {
    const reference = readSecretReference(expressionType, value);
    if (reference) return { kind: "secretReference", secretReference: boundedReference(reference) };
    return isEmptyExpressionValue(value) ? { kind: "unset" } : { kind: "withheld", expressionType };
  }
  if (masked) return isEmptyExpressionValue(value) ? { kind: "unset" } : { kind: "withheld", expressionType };
  if (value === undefined || value === null) return { kind: "unset" };
  const text = typeof value === "string" ? value : JSON.stringify(value) ?? "";
  const content = clip(text, Math.max(0, Math.min(workflowPropertyContentLimit, budget)));
  return { kind: "expression", expressionType, content, truncated: text.length > content.length, length: text.length };
}

export interface WorkflowGraphActivityReference {
  activityDefinitionId: string;
  activityVersionId: string;
  version: string;
  nodes: number[];
}

/** The provenance a design-authored (graph) activity version carries: it is published from an Activity Definition draft. */
export const designAuthoredSourceKind = "ActivityDefinitionDraft";

/**
 * The graph activities the draft uses (FR-014), by their activity definition identity and exact version, with the indexes
 * of the activities that use each. A graph activity's body is its definition's, so it is never inlined here. Only
 * design-authored versions count (provenance `ActivityDefinitionDraft`). A CLR activity also has a definition identity,
 * which recommended definitions decorate its catalog row with, but its behavior is code. A Json-sourced row may be a
 * provider-defined composite or a plain activity, and the catalog view carries no provider identity to tell them apart,
 * so it is not listed either: listing a plain activity as a graph activity is the error this rule prevents. Nothing is
 * inlined either way; a Json composite is only not labelled as a graph activity.
 */
export function collectGraphActivityReferences(activities: Array<{ id: string }>, versionOf: (nodeId: string) => string | undefined,
  catalogByVersion: Map<string, ActivityCatalogItem>): WorkflowGraphActivityReference[] {
  const references = new Map<string, WorkflowGraphActivityReference>();
  activities.forEach((activity, index) => {
    const versionId = versionOf(activity.id);
    const item = versionId ? catalogByVersion.get(versionId) : undefined;
    const definitionId = bounded(item?.activityDefinitionId, 256);
    if (!versionId || !item || !definitionId || item.provenance?.sourceKind !== designAuthoredSourceKind) return;
    const existing = references.get(versionId);
    if (existing) existing.nodes.push(index);
    else if (references.size < maxGraphActivities)
      references.set(versionId, { activityDefinitionId: definitionId, activityVersionId: versionId,
        version: bounded(item.activityDefinitionVersion ?? item.version, 256) ?? versionId, nodes: [index] });
  });
  return [...references.values()];
}

/**
 * The serialized length the server's default JSON writer gives this value: it escapes non-ASCII and `&'+<>\`` as six
 * characters, and a quote inside a string as `\u0022` rather than `\"`.
 */
export function hostSerializedLength(value: unknown) {
  const text = JSON.stringify(value) ?? "";
  let length = text.length;
  for (let index = 0; index < text.length; index += 1) {
    const code = text.charCodeAt(index);
    if (code === 0x5c) {
      if (text.charCodeAt(index + 1) === 0x22) length += 4;
      index += 1;
    } else if (code > 0x7e || code === 0x26 || code === 0x27 || code === 0x2b || code === 0x3c || code === 0x3e || code === 0x60) length += 5;
  }
  return length;
}

/**
 * The host-measured length of the server's admitted projection of this snapshot content (`WorkflowAgentSnapshot.TryRead`):
 * only the admitted fields, activities as id, type and display name, every connection with both ports, and the server's
 * interpretation status on each non-literal expression. The host bounds that projection, not what the editor sends, so the
 * size guard measures this.
 */
export function admittedSnapshotLength(content: Record<string, unknown>) {
  // As the server bounds them (`value[..256]`), which may cut a pair; its writer escapes a lone surrogate, never throws.
  const text = (value: unknown) => typeof value === "string" ? value.slice(0, 256) : "";
  const admitted: Record<string, unknown> = {
    workflowDefinitionId: content.workflowDefinitionId, draftId: content.draftId, revision: content.revision, selectedNodeId: content.selectedNodeId ?? null
  };
  if (content.workingState !== undefined) admitted.workingState = content.workingState;
  admitted.activities = ((content.activities as Array<Record<string, unknown>> | undefined) ?? [])
    .map(activity => ({ id: activity.id, type: activity.type, displayName: text(activity.displayName) }));
  if (content.structure !== undefined) admitted.structure = content.structure;
  admitted.connections = ((content.connections as Array<Record<string, unknown>> | undefined) ?? [])
    .map(edge => ({ source: edge.source, target: edge.target, sourcePort: text(edge.sourcePort), targetPort: text(edge.targetPort) }));
  admitted.positions = content.positions ?? [];
  for (const key of ["secretReferences", "diagnostics", "diagnosticsOmitted", "graphActivities"])
    if (content[key] !== undefined) admitted[key] = content[key];
  const selected = content.selectedActivity as WorkflowSelectedActivityContext | undefined;
  if (selected) admitted.selectedActivity = { ...selected, properties: selected.properties.map(property => property.value.kind === "expression"
    && property.value.expressionType !== "Literal" ? { ...property, value: { ...property.value, interpretation: "unavailable" } } : property) };
  return hostSerializedLength(admitted);
}

function readTypeName(input: StudioActivityInputDescriptor) {
  const record = input as unknown as Record<string, unknown>;
  const value = typeof record.typeName === "string" ? record.typeName : typeof record.type === "string" ? record.type : null;
  return value;
}

function boundedReference(reference: SecretReferenceView): SecretReferenceView {
  return { name: clip(reference.name, 256), typeName: bounded(reference.typeName, 256), scope: bounded(reference.scope, 256) };
}

function bounded(value: string | null | undefined, limit: number) {
  if (typeof value !== "string" || !value.trim()) return null;
  return clip(value, limit);
}

/**
 * Cuts text to at most `limit` UTF-16 units without splitting a surrogate pair: a lone surrogate would reach the server as
 * malformed UTF-16, which it refuses.
 */
export function clip(text: string, limit: number) {
  if (text.length <= limit) return text;
  const cut = text.slice(0, limit);
  const last = cut.charCodeAt(cut.length - 1);
  return last >= 0xd800 && last <= 0xdbff ? cut.slice(0, -1) : cut;
}
