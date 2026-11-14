import { readActivityInputOptionsProvider, type StudioActivityInputDescriptor, type StudioExpressionDescriptor } from "@elsa-workflows/studio-sdk";
import type { ActivityDataContextResult, ActivityDataSourceFacts, ActivityDataSourceQuery, ActivityDataTarget } from "./api/activityDataSelection";
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

/**
 * The syntax-picker entry that opens Choose data on an eligible input that holds something else. Picking it switches
 * nothing: the input changes only once an output is chosen, so cancelling leaves it as it was.
 */
export const chooseActivityOutputDescriptor: StudioExpressionDescriptor = {
  ...activityResultExpressionDescriptor,
  displayName: "Activity output…"
};

/** What the host knows about an activity in the current document, for readable provenance. */
export interface ActivityDataProducerDescription {
  label: string;
  /** Display names of the producer's declared outputs, by output reference key. */
  outputLabels: Readonly<Record<string, string>>;
  /** True when another activity in the document has the same label, so the node id is shown beside it. */
  sharesLabel?: boolean;
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
  /** True while the owner's own save is in flight: a stale answer then means "wait", not "changed elsewhere". */
  saving: boolean;
  /** One page of the sources for the current document, starting at `skip`. */
  requestSources(target: ActivityDataTarget, signal: AbortSignal, skip: number, query?: ActivityDataSourceQuery): Promise<ActivityDataContextResult>;
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
  // Mirrors the server's connection rule: a declared connection type, or the connection reference type itself.
  if (input.connectionType?.trim() || unqualifiedTypeName(input.typeName) === connectionReferenceType) return false;
  if (propertyEditorId && !builtInPropertyEditorIds.has(propertyEditorId)) return false;
  return readActivityInputOptionsProvider(input) == null;
}

const connectionReferenceType = "Elsa.Activities.Runtime.Core.Models.ConnectionReference";

function unqualifiedTypeName(typeName: string | null | undefined) {
  return (typeName ?? "").split(",", 1)[0]!.trim();
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

/**
 * The label of an activity's whole result. The spec 1215 contracts fix no label; they speak of the "whole value" and
 * the producer's "whole result", and the delivery plan of the "whole output", which reads best beside named outputs.
 */
export const wholeOutputLabel = "Whole output";

/** Pages of 100 are read until the server has no more or this many pages were read (1,000 sources). */
export const sourcePageSize = 100;
export const maximumSourcePages = 10;

/**
 * Reads the sources for one input page by page. The server lists workflow inputs and variables before activity
 * outputs, so the first page can hold none of the outputs; reading on keeps a workflow with many inputs or variables
 * from looking as if it had no outputs. Every page answers the same request identity; the first non-listing answer
 * (stale, denied, …) or an aborted signal ends the read. A result still marked `hasMoreSources` stopped at the bound.
 */
export async function collectSourcePages(
  requestPage: (skip: number) => Promise<ActivityDataContextResult>,
  signal: AbortSignal
): Promise<ActivityDataContextResult> {
  let combined: ActivityDataContextResult | null = null;
  for (let page = 0; page < maximumSourcePages; page++) {
    const result = await requestPage(page * sourcePageSize);
    if (signal.aborted) throw new DOMException("The request was aborted.", "AbortError");
    if (result.state !== "supported" && result.state !== "empty") return result;
    // Pages share document/base/snapshot and capability facts. Context revisions include the query (including skip),
    // so they may differ legitimately and are never represented as one authority for a combined page set.
    if (combined && (result.documentRevision !== combined.documentRevision || result.baseRevision !== combined.baseRevision ||
      result.snapshotHash !== combined.snapshotHash || result.resourceId !== combined.resourceId ||
      result.resourceKind !== combined.resourceKind || result.target?.target?.nodeId !== combined.target?.target?.nodeId ||
      result.target?.target?.inputKey !== combined.target?.target?.inputKey || result.contractRevision !== combined.contractRevision ||
      (result.memberReadsEnabled === true) !== (combined.memberReadsEnabled === true))) {
      return { ...result, state: "stale", code: "activity-data.revision-changed", memberReadsEnabled: false, target: null, sources: [], hasMoreSources: false };
    }
    combined = combined
      ? { ...result, contextRevision: null, target: combined.target ?? result.target, sources: [...combined.sources, ...result.sources] }
      : result;
    if (!result.hasMoreSources) break;
  }
  return { ...combined!, state: combined!.sources.length > 0 ? "supported" : "empty" };
}

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
  /**
   * The node id, shown when another activity in the document or in the list has the same label, so the two stay
   * distinguishable here and on the stored reference alike.
   */
  disambiguation: string | null;
  /**
   * The reason, when none of the producer's listed outputs can be chosen and all for the same reason. It is shown once
   * for the producer; each option's own `unavailableReason` still holds it for assistive technology.
   */
  sharedUnavailableReason: string | null;
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
      group = { producerNodeId: identity.producerNodeId, label: producer?.label ?? identity.producerNodeId,
        disambiguation: producer?.sharesLabel ? identity.producerNodeId : null, sharedUnavailableReason: null, options: [] };
      groups.set(identity.producerNodeId, group);
    }
    group.options.push({
      source,
      producerNodeId: identity.producerNodeId,
      projectionKey: identity.projectionKey,
      label: describeOutput(identity.projectionKey, producer),
      typeLabel: source.type?.alias ? formatArgumentType(readArgumentType({ type: source.type })) : null,
      unavailableReason: source.binding ? null : describeUnavailableSource(source.reasonCode, identity.projectionKey)
    });
  }
  const result = [...groups.values()];
  const labelCounts = new Map<string, number>();
  for (const group of result) labelCounts.set(group.label, (labelCounts.get(group.label) ?? 0) + 1);
  for (const group of result) {
    if ((labelCounts.get(group.label) ?? 0) > 1 && group.label !== group.producerNodeId) group.disambiguation = group.producerNodeId;
    const reasons = new Set(group.options.map(option => option.unavailableReason));
    const [only] = reasons;
    if (group.options.length > 1 && reasons.size === 1 && only) group.sharedUnavailableReason = only;
  }
  return result;
}

/**
 * The whole result (`$result`) is the activity's whole value, named so it cannot be mistaken for a declared output
 * that happens to be called "Result"; a declared output uses its declared display name.
 */
export function describeOutput(projectionKey: string, producer: ActivityDataProducerDescription | null) {
  if (projectionKey === "$result") return wholeOutputLabel;
  return producer?.outputLabels[projectionKey] || projectionKey;
}

/**
 * Where an author keeps a value from a run they cannot name directly: the producer's Outputs tab captures an output
 * into a variable ("Capture into"), and a variable holds the latest value written to it.
 */
const variableAdvice = "To use its latest output, capture that output into a variable on the activity's Outputs tab.";

/** Plain explanations for the authoring contract's reason codes, in its order. */
export function describeUnavailableSource(reasonCode: string | null, projectionKey?: string): string {
  switch (reasonCode) {
    case "ConditionalOccurrence":
      return "This activity runs on only some paths to this input, so its output might be missing.";
    case "AvailabilityUnknown":
      return "Studio can't tell whether this activity always runs before this input.";
    case "UnavailableProducer":
      return "This activity doesn't always run before this input.";
    case "EarlierProducerRun":
      // Only declared outputs can be captured on the Outputs tab; the whole output cannot.
      return `This activity can run more than once before this input (for example in a loop), so Studio can't tell which run to use. ${projectionKey === "$result" ? "Use an expression for this input instead." : variableAdvice}`;
    case "NoTypedResult":
      return "This activity doesn't produce a whole output.";
    default:
      return "This output can't be used for this input.";
  }
}

/** Explains a server finding about a stored activity output; null for findings unrelated to its source. */
export function describeReferenceFinding(finding: ActivityDataInputFinding): string | null {
  switch (finding.type) {
    // The server reports a producer that is not certain to run first as UnavailableProducer, conditional or not.
    case "ValueFlow/UnavailableProducer":
      return "The source activity doesn't always run before this input. Replace it or clear it.";
    case "ValueFlow/EarlierProducerRun":
      return "The source activity can run more than once before this input, so Studio can't tell which run to use. Replace it or clear it.";
    case "ValueFlow/CyclicBackEdge":
      return "The source activity is reached again through a loop. Replace it or clear it.";
    case "ValueFlow/ScopeBoundary":
      return "The source activity is inside a container this input can't read from. Replace it or clear it.";
    case "ActivityData/UnknownInput":
      return "This source input is not declared. Declare it, replace this reference or clear it.";
    case "ActivityData/UnknownProjection":
    case "ActivityData/UnknownProducerContract":
      return "The source activity no longer has this output. Replace it or clear it.";
    default:
      return finding.type.startsWith("ValueFlow/") || finding.type.startsWith("ActivityData/")
        ? "This output can't be used here. Replace it or clear it."
        : null;
  }
}
