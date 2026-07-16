import type { StudioActivityInputDescriptor } from "@elsa-workflows/studio-sdk";
import type {
  ActivityExecutionInspectionValueSnapshot,
  WorkflowExecutableAuthoredInput,
  WorkflowExecutableInputBinding
} from "./workflowTypes";

export type InputInspectionState =
  | "missingDeclaration"
  | "missingAuthoredSource"
  | "missingCompiledBinding"
  | "noEvaluation"
  | "orphanEvidence"
  | "compiledOnly"
  | "sourceUnavailable"
  | "duplicateKey"
  | "legacyIdentity"
  | "redacted"
  | "permissionHidden"
  | "metadataOnly"
  | "unavailable"
  | "unsupported"
  | "truncated"
  | "failedEvaluation";

export interface InputInspectionRow {
  rowKey: string;
  inputKey?: string;
  name: string;
  declaredType?: string;
  declaration?: StudioActivityInputDescriptor;
  authoredSource?: WorkflowExecutableAuthoredInput;
  compiledBinding?: WorkflowExecutableInputBinding;
  evaluations: ActivityExecutionInspectionValueSnapshot[];
  latestEvaluation?: ActivityExecutionInspectionValueSnapshot;
  states: InputInspectionState[];
}

export interface BuildInputInspectionRowsOptions {
  declarations?: StudioActivityInputDescriptor[];
  authoredSources?: WorkflowExecutableAuthoredInput[];
  compiledBindings?: WorkflowExecutableInputBinding[];
  evaluations?: ActivityExecutionInspectionValueSnapshot[];
  authoredSourceAccess?: string | null;
}

interface InputBucket {
  inputKey: string;
  declarations: StudioActivityInputDescriptor[];
  authoredSources: WorkflowExecutableAuthoredInput[];
  compiledBindings: WorkflowExecutableInputBinding[];
  evaluations: ActivityExecutionInspectionValueSnapshot[];
}

/**
 * Correlates only new-contract records that carry the stable activity input ReferenceKey.
 * Keyless compatibility records remain honest, independent rows; display names are never a join key.
 */
export function buildInputInspectionRows({
  declarations = [],
  authoredSources = [],
  compiledBindings = [],
  evaluations = [],
  authoredSourceAccess
}: BuildInputInspectionRowsOptions): InputInspectionRow[] {
  const buckets = new Map<string, InputBucket>();
  const legacyRows: InputInspectionRow[] = [];
  let legacyIndex = 0;

  const bucketFor = (inputKey: string) => {
    let bucket = buckets.get(inputKey);
    if (!bucket) {
      bucket = { inputKey, declarations: [], authoredSources: [], compiledBindings: [], evaluations: [] };
      buckets.set(inputKey, bucket);
    }
    return bucket;
  };

  for (const declaration of declarations) {
    const inputKey = normalizeKey(declaration.referenceKey);
    if (inputKey) bucketFor(inputKey).declarations.push(declaration);
    else legacyRows.push(legacyRow(`declaration:${legacyIndex++}`, declaration.displayName || declaration.name, declaration.typeName, { declaration }));
  }

  for (const source of authoredSources) {
    const inputKey = normalizeKey(source.inputKey);
    if (inputKey) bucketFor(inputKey).authoredSources.push(source);
    else legacyRows.push(legacyRow(`source:${legacyIndex++}`, "Unknown input", undefined, { authoredSource: source }));
  }

  for (const binding of compiledBindings) {
    const inputKey = normalizeKey(binding.inputKey);
    if (inputKey) bucketFor(inputKey).compiledBindings.push(binding);
    else legacyRows.push(legacyRow(`binding:${legacyIndex++}`, binding.inputName || "Unknown input", undefined, { compiledBinding: binding }));
  }

  const legacyEvidence = new Map<string, ActivityExecutionInspectionValueSnapshot[]>();
  for (const evaluation of evaluations) {
    const inputKey = normalizeKey(evaluation.inputKey);
    if (inputKey) bucketFor(inputKey).evaluations.push(evaluation);
    else {
      const identity = evaluation.name || "Unknown input";
      legacyEvidence.set(identity, [...(legacyEvidence.get(identity) ?? []), evaluation]);
    }
  }

  for (const [name, records] of legacyEvidence) {
    const ordered = orderEvaluations(records);
    legacyRows.push(legacyRow(`evidence:${legacyIndex++}`, name, runtimeType(ordered.at(-1)), {
      evaluations: ordered,
      latestEvaluation: ordered.at(-1),
      states: ["orphanEvidence", ...evaluationStates(ordered)]
    }));
  }

  const rows: InputInspectionRow[] = [];
  for (const bucket of buckets.values()) {
    const duplicateCount = Math.max(bucket.declarations.length, bucket.authoredSources.length, bucket.compiledBindings.length, 1);
    const orderedEvaluations = orderEvaluations(bucket.evaluations);
    for (let index = 0; index < duplicateCount; index++) {
      const declaration = bucket.declarations[index];
      const authoredSource = bucket.authoredSources[index];
      const compiledBinding = bucket.compiledBindings[index];
      const primary = index === 0;
      const rowEvaluations = primary ? orderedEvaluations : [];
      const latestEvaluation = rowEvaluations.at(-1);
      const states = deriveStates({
        declaration,
        authoredSource,
        compiledBinding,
        evaluations: rowEvaluations,
        hasDuplicate: duplicateCount > 1,
        authoredSourceAccess
      });

      rows.push({
        rowKey: index === 0 ? bucket.inputKey : `${bucket.inputKey}:duplicate:${index}`,
        inputKey: bucket.inputKey,
        name: declaration?.displayName || declaration?.name || compiledBinding?.inputName || latestEvaluation?.name || bucket.inputKey,
        declaredType: declaration?.typeName || runtimeType(latestEvaluation),
        declaration,
        authoredSource,
        compiledBinding,
        evaluations: rowEvaluations,
        latestEvaluation,
        states
      });
    }
  }

  return [...rows, ...legacyRows];
}

function legacyRow(
  rowKey: string,
  name: string,
  declaredType: string | undefined,
  values: Partial<InputInspectionRow>
): InputInspectionRow {
  return {
    rowKey: `legacy:${rowKey}`,
    name,
    declaredType,
    evaluations: [],
    ...values,
    states: uniqueStates(["legacyIdentity", ...(values.states ?? [])])
  };
}

function deriveStates({
  declaration,
  authoredSource,
  compiledBinding,
  evaluations,
  hasDuplicate,
  authoredSourceAccess
}: {
  declaration?: StudioActivityInputDescriptor;
  authoredSource?: WorkflowExecutableAuthoredInput;
  compiledBinding?: WorkflowExecutableInputBinding;
  evaluations: ActivityExecutionInspectionValueSnapshot[];
  hasDuplicate: boolean;
  authoredSourceAccess?: string | null;
}): InputInspectionState[] {
  const states: InputInspectionState[] = [];
  if (!declaration) states.push("missingDeclaration");
  if (!authoredSource) states.push("missingAuthoredSource");
  if (!compiledBinding) states.push("missingCompiledBinding");
  if (!authoredSource && compiledBinding) states.push("compiledOnly");
  if (evaluations.length === 0) states.push("noEvaluation");
  if (evaluations.length > 0 && !declaration && !authoredSource && !compiledBinding) states.push("orphanEvidence");
  if (hasDuplicate) states.push("duplicateKey");

  const sourceAccess = authoredSourceAccess?.toLowerCase();
  if (!authoredSource && sourceAccess && sourceAccess !== "visible" && sourceAccess !== "allowed") {
    states.push("sourceUnavailable");
    if (sourceAccess === "redacted") states.push("redacted");
    if (sourceAccess === "unavailable") states.push("unavailable");
    if (sourceAccess === "permissionhidden" || sourceAccess === "permission-hidden") states.push("permissionHidden");
  }

  states.push(...evaluationStates(evaluations));
  return uniqueStates(states);
}

function evaluationStates(evaluations: ActivityExecutionInspectionValueSnapshot[]): InputInspectionState[] {
  const states: InputInspectionState[] = [];
  for (const evaluation of evaluations) {
    const access = (evaluation.accessState ?? evaluation.access)?.toLowerCase();
    const state = evaluation.state?.toLowerCase();
    const snapshotKind = evaluation.snapshot?.kind.toLowerCase();
    if (access === "redacted" || snapshotKind === "redacted") states.push("redacted");
    if (access === "unavailable" || state === "unavailable") states.push("unavailable");
    if (access === "permissionhidden" || access === "permission-hidden" || snapshotKind === "permissionhidden") states.push("permissionHidden");
    if (state === "metadataonly") states.push("metadataOnly");
    if (snapshotKind === "unsupported") states.push("unsupported");
    if (snapshotKind === "truncated") states.push("truncated");
    if (evaluation.failure) states.push("failedEvaluation");
  }
  return uniqueStates(states);
}

function orderEvaluations(evaluations: ActivityExecutionInspectionValueSnapshot[]) {
  const seenEvaluationIds = new Set<string>();
  return evaluations
    .map((evaluation, index) => ({ evaluation, index }))
    .sort((left, right) => compareEvaluations(left.evaluation, right.evaluation) || left.index - right.index)
    .filter(({ evaluation }) => {
      const evaluationId = normalizeKey(evaluation.evaluationId);
      if (!evaluationId) return true;
      if (seenEvaluationIds.has(evaluationId)) return false;
      seenEvaluationIds.add(evaluationId);
      return true;
    })
    .map(({ evaluation }) => evaluation);
}

function compareEvaluations(left: ActivityExecutionInspectionValueSnapshot, right: ActivityExecutionInspectionValueSnapshot) {
  const leftSequence = evaluationSequence(left);
  const rightSequence = evaluationSequence(right);
  if (typeof leftSequence === "number" && typeof rightSequence === "number" && leftSequence !== rightSequence) {
    return leftSequence - rightSequence;
  }
  if (typeof leftSequence === "number" && typeof rightSequence !== "number") return -1;
  if (typeof leftSequence !== "number" && typeof rightSequence === "number") return 1;
  return Date.parse(left.capturedAt) - Date.parse(right.capturedAt);
}

export function evaluationSequence(evaluation: ActivityExecutionInspectionValueSnapshot) {
  return evaluation.evaluationSequence ?? evaluation.sequence;
}

export function evaluationPhase(evaluation: ActivityExecutionInspectionValueSnapshot) {
  return evaluation.evaluationPhase ?? evaluation.phase;
}

function runtimeType(evaluation: ActivityExecutionInspectionValueSnapshot | undefined) {
  return evaluation?.type?.displayName || evaluation?.type?.typeName || evaluation?.type?.alias || undefined;
}

function normalizeKey(value: string | null | undefined) {
  const key = value?.trim();
  return key || undefined;
}

function uniqueStates(states: InputInspectionState[]) {
  return [...new Set(states)];
}
