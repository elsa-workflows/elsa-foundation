import { readActivityInputOptionsProvider, type StudioActivityInputDescriptor, type StudioExpressionDescriptor } from "@elsa-workflows/studio-sdk";
import type { ActivityDataContextResult, ActivityDataSourceFacts, ActivityDataTarget } from "./api/activityDataSelection";
import { acceptsOnlySecretReference } from "./activityProperties";
import { formatArgumentType } from "./workflowReferenceAuthoring";
import { readArgumentType } from "./workflowProperties";

/**
 * The model behind Choose data for whole activity outputs (spec 1215, #3094). It decides which inputs offer it,
 * groups the sources the server reported and explains the ones that cannot be chosen. It holds no state and never
 * builds a binding: a selection stores the server's binding unchanged.
 */

/** The canonical role of a whole activity-result read (ADR 0045); the expression type the server's binding carries. */
export const activityResultSyntax = "ActivityResult";

/**
 * The editing-mode descriptor for a stored activity output. The server's expression catalog does not list this role
 * (it is a reference chosen through Choose data, not a syntax to type in), so the properties panel adds it only for an
 * input that already holds one, which lets the syntax picker name the current mode and offer the way back to a fixed
 * value or an expression.
 */
export const activityResultExpressionDescriptor: StudioExpressionDescriptor = {
  type: activityResultSyntax,
  displayName: "Activity output",
  description: "The output of an earlier activity.",
  editingMode: "reference"
};

/** What the host knows about an activity in the current document, for readable provenance. */
export interface ActivityDataProducerDescription {
  label: string;
  /** Display names of the producer's declared outputs, by output reference key. */
  outputLabels: Readonly<Record<string, string>>;
}

/** A server finding about one input of the last saved document. */
export interface ActivityDataInputFinding {
  type: string;
  message: string;
}

/**
 * The document owner's side of Choose data. The owner keeps the document, its history and its saves; this only lets
 * the chooser ask about the current document, describe its activities and go to one.
 */
export interface ActivityDataSelectionHost {
  /** Changes with every authored edit, so facts for an older document are never shown as current. */
  documentRevision: string;
  /** The acknowledged saved revision a request is checked against; a stale answer is asked again once it moves. */
  baseRevision: string;
  requestSources(target: ActivityDataTarget, signal: AbortSignal): Promise<ActivityDataContextResult>;
  /** Null when the activity is not in the current document. */
  describeActivity(nodeId: string): ActivityDataProducerDescription | null;
  revealActivity?(nodeId: string): void;
  /** Server findings for the input, only while they describe the current document. */
  findingsFor(target: ActivityDataTarget): ActivityDataInputFinding[];
}

/** A stored whole activity-result read, as authored. Fields this client does not model are preserved by the caller. */
export interface ActivityResultReference {
  producerNodeId: string;
  projectionKey: string;
}

export function readActivityResultReference(value: unknown): ActivityResultReference | null {
  if (!value || typeof value !== "object" || Array.isArray(value)) return null;
  const { producerNodeId, projectionKey } = value as Record<string, unknown>;
  return typeof producerNodeId === "string" && producerNodeId.trim() && typeof projectionKey === "string" && projectionKey.trim()
    ? { producerNodeId, projectionKey }
    : null;
}

/**
 * Whether an input offers Choose data. Secret-only, masked, connection and options-provider inputs keep their own
 * source contracts, and a read-only input cannot change. An input a module edits with its own control (a compensation
 * target, a user-task assignee, a decision version) keeps that control's contract: its owner decides which sources it
 * permits, so the generic chooser stays off. The server applies the input's declared constraints again.
 */
export function offersActivityData(input: StudioActivityInputDescriptor, readOnly: boolean, propertyEditorId: string | undefined) {
  if (readOnly || input.isReadOnly === true || input.isWrapped === false) return false;
  if (acceptsOnlySecretReference(input) || input.isSensitive === true || input.uiHint?.toLowerCase() === "password") return false;
  if (input.connectionType?.trim()) return false;
  if (propertyEditorId && !builtInPropertyEditorIds.has(propertyEditorId)) return false;
  return readActivityInputOptionsProvider(input) == null;
}

/** The host's general-purpose value editors; the masked editor is excluded with masked inputs above. */
const builtInPropertyEditorIds = new Set([
  "studio.property.singleline",
  "studio.property.multiline",
  "studio.property.text-fallback",
  "studio.property.number",
  "studio.property.checkbox",
  "studio.property.timespan",
  "studio.property.dropdown",
  "studio.property.multiselect"
]);

export interface ActivityDataSourceOption {
  source: ActivityDataSourceFacts;
  producerNodeId: string;
  projectionKey: string;
  label: string;
  typeLabel: string | null;
  /** Null when the source can be chosen. */
  unavailableReason: string | null;
}

export interface ActivityDataProducerGroup {
  producerNodeId: string;
  label: string;
  /** The node id, shown when another listed activity has the same label, so the two stay distinguishable. */
  disambiguation: string | null;
  options: ActivityDataSourceOption[];
}

/**
 * Groups the activity outputs the server reported by producer, in the server's order. Sources of other roles are left
 * out: this release chooses activity outputs, and workflow inputs and variables keep their own reference editors.
 */
export function groupActivityOutputSources(
  sources: ActivityDataSourceFacts[],
  describeActivity: (nodeId: string) => ActivityDataProducerDescription | null
): ActivityDataProducerGroup[] {
  const groups = new Map<string, ActivityDataProducerGroup>();
  for (const source of sources) {
    const identity = source.activityResultSource;
    if (!identity || source.binding && source.binding.expressionType !== activityResultSyntax) continue;
    const producer = describeActivity(identity.producerNodeId);
    let group = groups.get(identity.producerNodeId);
    if (!group) {
      group = { producerNodeId: identity.producerNodeId, label: producer?.label ?? identity.producerNodeId, disambiguation: null, options: [] };
      groups.set(identity.producerNodeId, group);
    }
    group.options.push({
      source,
      producerNodeId: identity.producerNodeId,
      projectionKey: identity.projectionKey,
      label: describeOutput(identity.projectionKey, producer),
      typeLabel: source.type?.alias ? formatArgumentType(readArgumentType({ type: source.type })) : null,
      unavailableReason: source.binding ? null : describeUnavailableSource(source.reasonCode)
    });
  }
  const result = [...groups.values()];
  const labelCounts = new Map<string, number>();
  for (const group of result) labelCounts.set(group.label, (labelCounts.get(group.label) ?? 0) + 1);
  for (const group of result) {
    if ((labelCounts.get(group.label) ?? 0) > 1 && group.label !== group.producerNodeId) group.disambiguation = group.producerNodeId;
  }
  return result;
}

/** The whole result is the activity's own value; a declared output uses its declared display name. */
export function describeOutput(projectionKey: string, producer: ActivityDataProducerDescription | null) {
  if (projectionKey === "$result") return "Result";
  return producer?.outputLabels[projectionKey] || projectionKey;
}

/** Plain explanations for the authoring contract's reason codes, in its order. */
export function describeUnavailableSource(reasonCode: string | null): string {
  switch (reasonCode) {
    case "ConditionalOccurrence":
      return "Runs only on some paths to this input, so its output might be missing.";
    case "AvailabilityUnknown":
      return "Studio can't tell whether this activity always runs before this input.";
    case "UnavailableProducer":
      return "This activity doesn't run before this input.";
    case "EarlierProducerRun":
      return "Can run more than once before this input, for example in a loop. Pass its output through a variable instead.";
    case "NoTypedResult":
      return "This activity doesn't declare an output value.";
    default:
      return "This output can't be chosen for this input.";
  }
}

/** Explains a server finding about a stored activity output; null for findings unrelated to its source. */
export function describeReferenceFinding(finding: ActivityDataInputFinding): string | null {
  switch (finding.type) {
    case "ValueFlow/UnavailableProducer":
      return "The source activity doesn't run on every path to this input. Choose another output or clear it.";
    case "ValueFlow/EarlierProducerRun":
      return "The source activity can run more than once before this input. Choose another output or clear it.";
    case "ValueFlow/CyclicBackEdge":
      return "The source activity is reached again through a loop. Choose another output or clear it.";
    case "ValueFlow/ScopeBoundary":
      return "The source activity is inside a container this input can't read from. Choose another output or clear it.";
    case "ActivityData/UnknownProjection":
    case "ActivityData/UnknownProducerContract":
      return "The source activity no longer declares this output. Choose another output or clear it.";
    default:
      return finding.type.startsWith("ValueFlow/") || finding.type.startsWith("ActivityData/")
        ? finding.message || "This output can't be read here. Choose another output or clear it."
        : null;
  }
}
