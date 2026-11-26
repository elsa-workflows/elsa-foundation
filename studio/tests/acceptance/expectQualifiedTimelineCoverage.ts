import { expect } from "@playwright/test";

/** The owned full host must advertise the actually qualified native history owners. */
export function expectQualifiedTimelineCoverage(coverage: unknown) {
  expect(coverage).toMatchObject({
    state: "captured", policyId: "runtime-history-v5", policyVersion: 5,
    capturedFromStart: true, hasGaps: false, limitation: null,
    captureAvailable: true, captureLimitation: null,
    sourceHistoryPending: false, sourceHistoryLimitation: null,
    supportedKinds: expect.arrayContaining([
      "ActivityScheduled", "ActivityStarted", "ActivityCompleted", "ActivityContinuationReturned", "ActivityRetryScheduled",
      "TimerScheduled", "TimerDispatched", "TimerRetired", "ChildWorkflowDispatchRequested",
      "StimulusPublicationCommitted", "ChildWorkflowSettled", "ChildStartAttemptObserved", "ChildStartDispositionObserved",
      "ChildControlAccepted", "ChildControlApplied", "CompensationRegistered", "CompensationStarted",
      "CompensationHandlerScheduled", "CompensationHandlerCompleted", "CompensationHandlerFaulted",
      "CompensationConfirmed", "CompensationResolved", "CompensationCompleted", "CompensationAbandoned"
    ])
  });
}
