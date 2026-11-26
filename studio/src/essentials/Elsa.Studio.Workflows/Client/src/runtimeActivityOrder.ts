import type { ActivityExecutionStateSummary } from "./workflowTypes";

/** Parses an ISO-8601 timestamp into 100ns ticks (matching .NET DateTimeOffset precision). */
export function parseIsoTimestampTicks(value?: string | null): bigint | null {
  if (!value) return null;
  const ms = Date.parse(value);
  if (Number.isNaN(ms)) return null;
  const fraction = /\.(\d+)(?:Z|[+-]\d{2}:?\d{2})?$/i.exec(value)?.[1] ?? "";
  const subMsTicks = BigInt(fraction.padEnd(7, "0").slice(3, 7));
  return BigInt(ms) * 10000n + subMsTicks;
}

export function compareIsoTimestampTicks(left?: string | null, right?: string | null): number {
  const a = parseIsoTimestampTicks(left);
  const b = parseIsoTimestampTicks(right);
  if (a === b) return 0;
  if (a === null) return 1;
  if (b === null) return -1;
  return a < b ? -1 : 1;
}

export function latestActivityExecution(activities: ActivityExecutionStateSummary[]) {
  return [...activities].sort((left, right) =>
    compareIsoTimestampTicks(right.scheduledAt, left.scheduledAt) ||
    (right.executionSequence ?? 0) - (left.executionSequence ?? 0) ||
    (left.activityExecutionId < right.activityExecutionId ? 1 : left.activityExecutionId > right.activityExecutionId ? -1 : 0))[0];
}
