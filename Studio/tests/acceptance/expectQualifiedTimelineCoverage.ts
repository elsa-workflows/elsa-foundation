import { expect } from "@playwright/test";

/** The owned full host must advertise the actually qualified timer/source chain. */
export function expectQualifiedTimelineCoverage(coverage: unknown) {
  expect(coverage).toMatchObject({
    state: "captured", policyId: "runtime-history-v4", policyVersion: 4,
    capturedFromStart: true, hasGaps: false, limitation: null,
    captureAvailable: true, captureLimitation: null,
    sourceHistoryPending: false, sourceHistoryLimitation: null,
    supportedKinds: expect.arrayContaining([
      "ActivityScheduled", "ActivityStarted", "ActivityCompleted", "ActivityContinuationReturned", "ActivityRetryScheduled",
      "TimerScheduled", "TimerDispatched", "TimerRetired", "ChildWorkflowDispatchRequested",
      "StimulusPublicationCommitted", "ChildWorkflowSettled"
    ])
  });
}
