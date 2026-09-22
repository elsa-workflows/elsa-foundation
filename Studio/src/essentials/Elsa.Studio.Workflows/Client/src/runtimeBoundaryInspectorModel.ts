import type {
  ActivityExecutionHierarchyItem,
  ActivityExecutionHierarchyPage,
  ActivityExecutionInspectionValueSnapshot
} from "./workflowTypes";

export type BoundaryEvidenceStaleReason =
  | "snapshot-expired"
  | "query-changed"
  | "access-changed"
  | "boundary-changed"
  | "watermark-changed"
  | "unknown";

export interface BoundaryEvidenceSnapshot {
  committedThroughSequence: number | null;
  effectiveLimit: number;
  items: ActivityExecutionHierarchyItem[];
  nextCursor: string | null;
  staleReason: BoundaryEvidenceStaleReason | null;
  staleMessage: string;
}

export interface BoundaryCursorProblem {
  recoverable: boolean;
  reason: BoundaryEvidenceStaleReason;
  message: string;
}

export type RuntimeEvidencePresentation =
  | "captured"
  | "redacted"
  | "not-captured"
  | "capture-failed"
  | "payload-reference";

export const emptyBoundaryEvidenceSnapshot = (): BoundaryEvidenceSnapshot => ({
  committedThroughSequence: null,
  effectiveLimit: 100,
  items: [],
  nextCursor: null,
  staleReason: null,
  staleMessage: ""
});

export function mergeBoundaryEvidencePage(
  current: BoundaryEvidenceSnapshot,
  page: ActivityExecutionHierarchyPage
): BoundaryEvidenceSnapshot {
  if (
    current.committedThroughSequence !== null &&
    current.committedThroughSequence !== page.committedThroughSequence
  ) {
    return {
      ...current,
      nextCursor: null,
      staleReason: "watermark-changed",
      staleMessage: "The server returned a different committed-through watermark. Restart to review a new snapshot."
    };
  }

  const byId = new Map(current.items.map(item => [item.activityExecutionId, item]));
  page.items.forEach(item => byId.set(item.activityExecutionId, item));
  const items = [...byId.values()].sort(compareHierarchyItems);

  return {
    committedThroughSequence: page.committedThroughSequence,
    effectiveLimit: page.effectiveLimit,
    items,
    nextCursor: page.nextCursor ?? null,
    staleReason: null,
    staleMessage: ""
  };
}

export function markBoundaryEvidenceStale(
  current: BoundaryEvidenceSnapshot,
  problem: BoundaryCursorProblem
): BoundaryEvidenceSnapshot {
  return {
    ...current,
    nextCursor: null,
    staleReason: problem.reason,
    staleMessage: problem.message
  };
}

export function classifyBoundaryCursorProblem(error: unknown): BoundaryCursorProblem | null {
  const payload = readErrorPayload(error);
  const errorCode = readString(payload, "errorCode");
  if (errorCode !== "activity.cursor.expired" && errorCode !== "activity.cursor.binding-mismatch") {
    return null;
  }

  const message = readString(payload, "detail") || errorMessage(error);
  const reason: BoundaryEvidenceStaleReason =
    errorCode === "activity.cursor.expired"
      ? "snapshot-expired"
      : "unknown";

  return {
    recoverable: true,
    reason,
    message
  };
}

export function compareHierarchyItems(
  left: Pick<ActivityExecutionHierarchyItem, "executionSequence" | "activityExecutionId">,
  right: Pick<ActivityExecutionHierarchyItem, "executionSequence" | "activityExecutionId">
) {
  return left.executionSequence - right.executionSequence ||
    left.activityExecutionId.localeCompare(right.activityExecutionId);
}

export function executionOccurrenceLabel(
  item: Pick<ActivityExecutionHierarchyItem, "executionSequence" | "activityExecutionId" | "attempt" | "iterationId" | "branchId">
) {
  const identity = [
    `#${item.executionSequence}`,
    item.attempt ? `attempt ${item.attempt.attemptNumber}` : null,
    item.iterationId ? `iteration ${item.iterationId}` : null,
    item.branchId ? `branch ${item.branchId}` : null
  ].filter(Boolean).join(" · ");
  return `${identity} · ${item.activityExecutionId}`;
}

export function runtimeEvidencePresentation(
  snapshot: ActivityExecutionInspectionValueSnapshot
): RuntimeEvidencePresentation {
  if (snapshot.failure || normalize(snapshot.state) === "capturefailed") return "capture-failed";

  const node = snapshot.snapshot && typeof snapshot.snapshot === "object"
    ? snapshot.snapshot as { kind?: unknown }
    : null;
  const kind = normalize(typeof node?.kind === "string" ? node.kind : "");
  const access = normalize(snapshot.accessState ?? snapshot.access ?? "");

  if (kind === "payloadreference") return "payload-reference";
  if (kind === "redacted" || kind === "permissionhidden" || access === "redacted" || access === "permissionhidden") {
    return "redacted";
  }
  if (normalize(snapshot.state) === "notcaptured" || normalize(snapshot.captureMode) === "none") {
    return "not-captured";
  }
  return "captured";
}

function readErrorPayload(error: unknown) {
  if (!error || typeof error !== "object") return {};
  const payload = (error as { payload?: unknown }).payload;
  return payload && typeof payload === "object" ? payload as Record<string, unknown> : {};
}

function readString(record: Record<string, unknown>, key: string) {
  const value = record[key];
  return typeof value === "string" ? value : "";
}

function errorMessage(error: unknown) {
  return error instanceof Error ? error.message : String(error);
}

function normalize(value: string | null | undefined) {
  return (value ?? "").replace(/[\s_-]+/g, "").toLowerCase();
}
