import type { ActivityExecutionStateSummary } from "./workflowTypes";

function compareScheduledAt(left: string, right: string) {
  const ticks = (value: string) => BigInt(Date.parse(value) || 0) * 10000n +
    BigInt((/\.(\d+)(?:Z|[+-]\d{2}:\d{2})$/.exec(value)?.[1] ?? "").padEnd(7, "0").slice(3, 7));
  const a = ticks(left), b = ticks(right);
  return a < b ? -1 : a > b ? 1 : 0;
}

export function latestActivityExecution(activities: ActivityExecutionStateSummary[]) {
  return [...activities].sort((left, right) =>
    compareScheduledAt(right.scheduledAt, left.scheduledAt) ||
    (right.executionSequence ?? 0) - (left.executionSequence ?? 0) ||
    (left.activityExecutionId < right.activityExecutionId ? 1 : left.activityExecutionId > right.activityExecutionId ? -1 : 0))[0];
}
