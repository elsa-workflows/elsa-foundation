import type { StudioWorkflowRunInputEditorContribution } from "@elsa-workflows/studio-sdk";
import type { ActivityInputContract } from "./activityDefinitionTypes";
import type {
  ActivityDraftTestRunInput,
  ActivityDraftTestRunView
} from "./api/publishing";
import {
  parseWorkflowRunInputs,
  type WorkflowRunInputContributionFailure
} from "./workflowRunInputs";
import type { CollectionKind, WorkflowInput } from "./workflowTypes";

export type ActivityTestRunInputPresence = "Absent" | "Null" | "Value";
export type ActivityTestRunStage =
  | "accepted"
  | "waiting"
  | "running"
  | "suspended"
  | "completed"
  | "faulted"
  | "cancelled"
  | "validation-rejected"
  | "dispatch-rejected"
  | "dispatch-ambiguous"
  | "expired";

export interface ActivityTestRunInputDraft {
  presence: ActivityTestRunInputPresence;
  draft: string;
}

export interface ActivityTestRunInputParseResult {
  inputs: Record<string, ActivityDraftTestRunInput>;
  errors: Record<string, string>;
  contributionFailures?: Record<string, WorkflowRunInputContributionFailure>;
}

export function toWorkflowRunInput(input: ActivityInputContract): WorkflowInput {
  return {
    ...input,
    displayName: input.displayName?.trim() || input.name,
    description: input.description ?? "",
    category: input.category ?? "",
    uiHint: input.uiHint ?? "",
    storageDriverType: input.storageDriverKey || null,
    type: {
      ...input.type,
      collectionKind: normalizeCollectionKind(input.type.collectionKind)
    },
    isRequired: input.isRequired
  };
}

export function parseActivityTestRunInputs(
  contractInputs: ActivityInputContract[],
  drafts: Record<string, ActivityTestRunInputDraft>,
  editors: StudioWorkflowRunInputEditorContribution[] = [],
  fallbackInputKeys: ReadonlySet<string> = new Set()
): ActivityTestRunInputParseResult {
  const inputs: Record<string, ActivityDraftTestRunInput> = {};
  const errors: Record<string, string> = {};
  const contributionFailures: Record<string, WorkflowRunInputContributionFailure> = {};

  for (const contractInput of contractInputs) {
    const input = toWorkflowRunInput(contractInput);
    const current = drafts[input.referenceKey] ?? { presence: "Absent", draft: "" };
    if (current.presence === "Absent") {
      if (input.isRequired && contractInput.default === null) {
        errors[input.referenceKey] = `${input.displayName || input.name} is required and has no default.`;
      } else {
        inputs[input.referenceKey] = { state: "Absent" };
      }
      continue;
    }
    if (current.presence === "Null") {
      if (!contractInput.isNullable) {
        errors[input.referenceKey] = `${input.displayName || input.name} does not allow null.`;
      } else {
        inputs[input.referenceKey] = { state: "Present", value: null };
      }
      continue;
    }

    const parsed = parseWorkflowRunInputs(
      [input],
      { [input.referenceKey]: current.draft },
      editors,
      fallbackInputKeys);
    if (parsed.errors[input.referenceKey]) {
      errors[input.referenceKey] = parsed.errors[input.referenceKey];
    } else if (Object.prototype.hasOwnProperty.call(parsed.values, input.name)) {
      inputs[input.referenceKey] = {
        state: "Present",
        value: parsed.values[input.name]
      };
    }
    if (parsed.contributionFailures?.[input.referenceKey]) {
      contributionFailures[input.referenceKey] = parsed.contributionFailures[input.referenceKey];
    }
  }

  return {
    inputs,
    errors,
    ...(Object.keys(contributionFailures).length > 0 ? { contributionFailures } : {})
  };
}

export function classifyActivityTestRun(
  view: ActivityDraftTestRunView,
  reconciledAfterAcceptance = false
): ActivityTestRunStage {
  const status = view.status.toLowerCase();
  if (isExpiredActivityTestRun(view)) return "expired";
  if (status === "validationrejected") return "validation-rejected";
  if (status === "dispatchrejected") return "dispatch-rejected";
  if (status === "dispatchambiguous") return "dispatch-ambiguous";
  if (status === "completed" || status === "finished") return "completed";
  if (status === "faulted" || status === "failed") return "faulted";
  if (status === "cancelled" || status === "canceled") return "cancelled";
  if (status === "suspended") return "suspended";
  if (status === "running") return "running";
  if (status === "pending" || status === "scheduled" || status === "dispatchdeferred") return "waiting";
  if (status === "dispatchaccepted") {
    return reconciledAfterAcceptance ? "waiting" : "accepted";
  }
  return view.outerActivityExecutionId ? "running" : "waiting";
}

export function isTerminalActivityTestRun(stage: ActivityTestRunStage) {
  return stage === "completed" ||
    stage === "faulted" ||
    stage === "cancelled" ||
    stage === "validation-rejected" ||
    stage === "dispatch-rejected" ||
    stage === "expired";
}

export function isExpiredActivityTestRun(view: ActivityDraftTestRunView) {
  return view.expiration.sourceReferenceExpired &&
    !view.expiration.sourceReferenceRetained &&
    !view.expiration.evidenceRetained &&
    !view.expiration.runStillActive;
}

export function activityTestRunWorkbenchUrl(view: ActivityDraftTestRunView) {
  const base = `/workflows/instances/${encodeURIComponent(view.workflowExecutionId)}`;
  return view.outerActivityExecutionId
    ? `${base}?activityExecutionId=${encodeURIComponent(view.outerActivityExecutionId)}`
    : base;
}

function normalizeCollectionKind(value: string): CollectionKind {
  if (value === "Array" || value === "List" || value === "HashSet") return value;
  return "Single";
}
