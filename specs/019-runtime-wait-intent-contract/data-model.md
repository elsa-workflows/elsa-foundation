# Data Model: Runtime Wait Registration And Post-Commit Intent Contract

## RuntimeWaitRegistration

Durable runtime wait correlation record. It is not a global unmatched bookmark inbox.

Fields:

- `WaitRegistrationId`
- `WorkflowExecutionId`
- `ActivityExecutionId`
- `BookmarkId`
- `CorrelationId`
- `StimulusType`
- `MatchCriteria`
- `Status`
- `RegisteredAt`
- `ExpiresAt`
- `DependsOnPostCommitIntentId`
- `FailurePolicy`
- `EarlySignalPolicy`
- `Metadata`

## RuntimeWaitRegistrationStatus

States:

- `Reserved`
- `Active`
- `Satisfied`
- `Cancelled`
- `Expired`
- `Faulted`

Reserved and active waits are matchable by correlation. Terminal states are not.

## Early Signal Policy

When a signal arrives before the dependent post-commit intent is delivered, runtime can match the reserved wait by correlation and then follow policy:

- `SatisfyReservedWait`
- `BufferUntilIntentDelivered`
- `RejectUnexpectedSignal`

No global unmatched inbox is introduced by this slice.
