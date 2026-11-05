/** Safe production history views. Payloads remain behind the existing value-evidence permission boundary. */
export interface RuntimeHistoryCoverage {
  state: "captured" | "partial" | "pending" | "uncaptured" | "disabled" | "unsupported" | "invalid" | string;
  limitation?: string | null;
  policyId?: string | null;
  policyVersion?: number | null;
  supportedKinds: string[];
  capturedFromStart: boolean;
  hasGaps: boolean;
  firstSequence: number;
  lastSequence: number;
  settledSequence: number;
  purgedThroughSequence: number;
  lastPhysicalCommitId?: string | null;
  lastCommittedAt?: string | null;
  captureAvailable?: boolean | null;
  captureLimitation?: string | null;
  sourceHistoryPending?: boolean | null;
  sourceHistoryLimitation?: string | null;
}

export interface RuntimeHistoryCausation {
  historyEventId?: string | null;
  workflowExecutionId?: string | null;
  deliveryId?: string | null;
  commandId?: string | null;
  schedulerWorkItemId?: string | null;
}

export interface RuntimeHistoryEvent {
  historyEventId: string;
  workflowExecutionId: string;
  sequence: number;
  schemaVersion: number;
  name: string;
  category: string;
  authority: string;
  occurredAt: string;
  recordedAt?: string | null;
  activityExecutionId?: string | null;
  executableNodeId?: string | null;
  checkpointId?: string | null;
  checkpointOrdinal?: number | null;
  physicalCommitId?: string | null;
  bookmarkId?: string | null;
  incidentId?: string | null;
  attemptId?: string | null;
  parentActivityExecutionId?: string | null;
  executionScopeId?: string | null;
  iterationId?: string | null;
  causation?: RuntimeHistoryCausation | null;
  metadata: Record<string, string>;
}

export interface RuntimeHistoryPage {
  coverage: RuntimeHistoryCoverage;
  items: RuntimeHistoryEvent[];
  upperSequence: number;
  scannedThroughSequence: number;
  scannedCount: number;
  nextContinuationToken?: string | null;
  catchUpToken?: string | null;
  scopeComplete: boolean;
  scopeLimitation?: string | null;
  readAt: string;
}

export interface RuntimeHistoryRequest {
  limit?: number;
  continuationToken?: string | null;
  activityExecutionId?: string | null;
  includeDescendants?: boolean;
  bookmarkId?: string | null;
  category?: string | null;
  name?: string | null;
  search?: string | null;
}

export interface RuntimeHistoryBookmark {
  bookmark: {
    identity: { bookmarkId: string; workflowExecutionId: string; activityExecutionId: string; resumeTargetId: string };
    stimulusType: string;
    stimulusHash: string;
    createdAt: string;
    expiresAt?: string | null;
  };
  executableNodeId?: string | null;
  status: "Active" | "Consumed" | "Removed" | "Superseded" | "Unknown" | string;
  createdHistoryEventId?: string | null;
  endedHistoryEventId?: string | null;
  lastHistoryEventId: string;
  lastSequence: number;
  firstRecordedSequence: number;
  endedAt?: string | null;
  reason?: string | null;
  selectedBookmarkId?: string | null;
  priorState: "Unknown" | "CreationMetadata" | "CreationEvent" | string;
  eligibility: "eligible" | "expired" | "not-active" | "ineligible" | "unavailable" | "unknown" | string;
  eligibilityReason?: string | null;
  eligibilityObservedAt: string;
}

export interface RuntimeHistoryBookmarkPage {
  coverage: RuntimeHistoryCoverage;
  items: RuntimeHistoryBookmark[];
  nextContinuationToken?: string | null;
  totalCount?: number | null;
  activeCount?: number | null;
  scopeComplete: boolean;
  scopeLimitation?: string | null;
  readAt: string;
}

export interface RuntimeHistoryBookmarkRequest {
  limit?: number;
  continuationToken?: string | null;
  activityExecutionId?: string | null;
  includeDescendants?: boolean;
  status?: string | null;
}

export interface RuntimeHistoryEventDetails { coverage: RuntimeHistoryCoverage; event: RuntimeHistoryEvent; readAt: string }
export interface RuntimeHistoryBookmarkDetails { coverage: RuntimeHistoryCoverage; bookmark: RuntimeHistoryBookmark; readAt: string }

export type RunHistorySelection = { kind: "event" | "bookmark"; id: string } | null;
