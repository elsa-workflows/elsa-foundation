import type { RuntimeHistoryBookmark, RuntimeHistoryCoverage, RuntimeHistoryEvent, RuntimeHistoryPage } from "../runtimeHistoryTypes";

export const historyCoverage: RuntimeHistoryCoverage = { state: "captured", policyId: "runtime-history-v1", policyVersion: 1,
  supportedKinds: ["WorkflowStarted", "BookmarkCreated", "BookmarkConsumed", "ActivityCompleted"], capturedFromStart: true,
  hasGaps: false, firstSequence: 1, lastSequence: 3, settledSequence: 3, purgedThroughSequence: 0 };
export function historyEvent(sequence = 1, overrides: Partial<RuntimeHistoryEvent> = {}): RuntimeHistoryEvent {
  return { historyEventId: `event-${sequence}`, workflowExecutionId: "run", sequence, schemaVersion: 1,
    name: "BookmarkCreated", category: "BookmarkLifecycle", authority: "CommittedTransition", occurredAt: "2026-11-05T09:00:00Z",
    recordedAt: "2026-11-05T09:00:01Z", activityExecutionId: "activity-1", bookmarkId: "bookmark-1", checkpointId: `checkpoint-${sequence}`,
    checkpointOrdinal: 0, physicalCommitId: `physical-${sequence}`, metadata: {}, ...overrides };
}
export function historyPage(items: RuntimeHistoryEvent[] = [historyEvent()], overrides: Partial<RuntimeHistoryPage> = {}): RuntimeHistoryPage {
  return { coverage: historyCoverage, items, upperSequence: Math.max(3, ...items.map(item => item.sequence)), scannedThroughSequence: 3,
    scannedCount: 3, nextContinuationToken: null, catchUpToken: "tail-3", scopeComplete: true,
    readAt: "2026-11-05T09:02:00Z", ...overrides };
}
export function historyBookmark(overrides: Partial<RuntimeHistoryBookmark> = {}): RuntimeHistoryBookmark {
  return { bookmark: { identity: { bookmarkId: "bookmark-1", workflowExecutionId: "run", activityExecutionId: "activity-1", resumeTargetId: "resume" },
    stimulusType: "Elsa.UserTaskCompleted", stimulusHash: "safe-hash", createdAt: "2026-11-05T09:00:00Z", expiresAt: "2026-11-05T09:01:00Z" },
    status: "Active", createdHistoryEventId: "event-1", endedHistoryEventId: null, lastHistoryEventId: "event-1", lastSequence: 1,
    firstRecordedSequence: 1, priorState: "CreationEvent", eligibility: "expired", eligibilityObservedAt: "2026-11-05T09:02:00Z", ...overrides };
}
