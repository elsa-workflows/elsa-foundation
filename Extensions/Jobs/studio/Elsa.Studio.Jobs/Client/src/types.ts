export type GateKind = "queue" | "jobType" | "workflowInstance" | "workflowDefinition";
export interface Gate { kind: GateKind; name: string; modelKind?: string | null }
export interface Access { tenantId: string; subjectId: string; permissions: string[] }
export interface Page<T> { items: T[]; next?: string | null }
export interface Schema { alias: string; version: number }
export interface Payload { schema: Schema; value: unknown }
export interface Policy {
  retry: { maximumAttempts: number; initialBackoff: string; maximumBackoff: string; backoffMultiplier: number; jitterFraction: number; retryTimeouts: boolean };
  attemptTimeout: string; minimumKeyRecognition: string;
}
export interface Invocation {
  handler: { jobType: string; version: string }; input: Payload; resultSchema: Schema;
  queue: string; policy: Policy; notBefore?: string | null;
}
export interface Association { modelKind: string; definitionId: string; definitionVersion: string; executionId: string; mode: string }
export interface Permission { attemptId: string; number: number; owner: string; implementationIdentity: string; grantedAt: string; expiresAt: string; deadline: string }
export interface Job {
  jobId: string; handler?: Invocation["handler"] | null; inputSchema?: Schema | null; resultSchema?: Schema | null;
  queue?: string | null; state: "accepted" | "attemptActive" | "terminal"; revision: number; acceptedAt: string; eligibleAt: string;
  activePermission?: Permission | null; outcome?: string | null; resultId?: string | null; completedAt?: string | null;
  progressFraction?: number | null; cancellationRequested: boolean; retriedFromJobId?: string | null;
  association?: Association | null; diagnostic?: string | null; consumptionState: "pending-result" | "unknown" | "owed" | "settled" | "not-applicable";
  inputPayloadState?: "available" | "expired"; resultPayloadState?: "available" | "notProduced" | "expired";
}
export interface Attempt { permission: Permission; state: string; physicalStartedAt?: string | null; physicalExitedAt?: string | null }
export interface Receipt { receiptId: string; attemptId: string; owner: string; fence: number; outcome: string; retryable: boolean; reportedAt: string; payloadState?: string; expiredAt?: string | null }
export interface Hold { gate: Gate; holdId: string; revision: number; actor: string; reason: string; requestedAt: string; releasedAt?: string | null; releasedBy?: string | null }
export interface GateHolds { gate: Gate; items: Hold[]; next?: string | null }
export interface JobHolds { gates: GateHolds[]; complete: boolean }
export interface Capacity { gate: Gate; limit?: number | null; revision: number }
export interface Queue { name: string; acceptedCount: number; activeLeaseCount: number; heldCount: number }
export interface Schedule {
  scheduleId: string; revision: number; enabled: boolean; nextOccurrence: string; createdAt: string; updatedAt: string;
  handler?: Invocation["handler"] | null; inputSchema?: Schema | null; resultSchema?: Schema | null;
  queue?: string | null; policy?: Policy | null; notBefore?: string | null;
  recurrence?: { kind: "interval" | "cron"; expression: string } | null;
  firstOccurrence?: string | null; missedPolicy?: "coalesceOne" | "replayEveryOccurrence" | null;
  association?: Association | null; diagnostic?: string | null;
}
export interface Operation { operationId: string; reason: string }
export interface Transition { status: string; revision?: number | null; jobId?: string | null; resultId?: string | null }
export interface Acceptance { jobId: string; acceptedAt: string; recognizeUntil: string; revision: number }
export interface InputView { state: string; input?: Payload | null; valueJson?: string | null; schema?: Schema | null; payloadSha256?: string | null; expiredAt?: string | null }
export interface ResultView { state: string; resultId?: string | null; outcome?: string | null; payload?: Payload | null; completedAt?: string | null; valueJson?: string | null; schema?: Schema | null; payloadSha256?: string | null; expiredAt?: string | null }
export interface ScheduleSave extends Operation {
  invocation: Invocation; recurrence: NonNullable<Schedule["recurrence"]>; firstOccurrence: string;
  missedPolicy: "coalesceOne" | "replayEveryOccurrence"; expectedRevision: number;
}
export const permissions = {
  read: "jobs:read", audit: "jobs/audit:read", submit: "jobs:submit", inputs: "jobs/inputs:read", results: "jobs/results:read",
  pause: "jobs:pause", cancel: "jobs:cancel", retry: "jobs:retry", schedules: "jobs/schedules:manage", capacity: "jobs/capacity:manage"
} as const;
export const defaultPolicy: Policy = {
  retry: { maximumAttempts: 3, initialBackoff: "00:00:05", maximumBackoff: "00:05:00", backoffMultiplier: 2, jitterFraction: 0.1, retryTimeouts: true },
  attemptTimeout: "00:05:00", minimumKeyRecognition: "30.00:00:00"
};

export interface OperationAudit {
  operationId: string; kind?: string | null; actor?: string | null; reason?: string | null; recordedAt?: string | null;
  target?: { jobId?: string | null; gate?: Gate | null; holdId?: string | null; scheduleId?: string | null;
    expectedRevision?: number | null; capacityLimit?: number | null; enabled?: boolean | null } | null;
  confirmation?: Transition | null; diagnostic?: string | null;
}
