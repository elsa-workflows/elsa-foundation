# Data model: Variable lifecycle inspection

Stored members are additive and optional; older records read back with them absent. Views are read-only.

## Stored (additions)

| Where | Member | Meaning |
|---|---|---|
| `VariableWriteRecord` (spec 1221) | `CommitSequence: long?` | Instance commit sequence of the logical checkpoint that committed the write (Q1). |
| `ActivityExecutionState` | `Transitions: IReadOnlyList<ActivityLifecycleStamp>?` | One stamp per started, suspended, resumed, completed, faulted, superseded: kind, commit sequence, branch/path. |
| Workflow execution state | `LastCommitSequence: long` | The instance's counter, advanced under the fence. |
| `ActivitySchedulingProvenance` | `JoinArrivalActivityExecutionIds: IReadOnlyList<string>?` | For a join completion: the arrivals that satisfied it (Q3). |
| Alteration job outcome | `VariableWrites: IReadOnlyList<VariableWriteRecord>?` | The `ModifyVariable` write, source `Alteration` (Q9). |

`ActivityLifecycleStamp(Kind, CommitSequence, BranchId?, ExecutionPathId?, EvaluationId?)` — `EvaluationId` links a
refreshed background snapshot to the one it superseded (ADR 0092 decision 9).

## Views

**VariableSummaryView**: `ScopeId`, `FrameId`, `VariableKey`, `Version`, `LastWriter` (WriterView), `LastCommitSequence`,
`Value` (ValueStateView), `HistoryRecorded: bool`.

**VariableWriteEntryView**: `ScopeId`, `VariableKey`, `Writer` (WriterView), `Source`, `Outcome` (`applied`,
`lostUpdateWarning`, `lostUpdateFault`, `acceptedOverride`), `BaseVersion?`, `CurrentVersion`, `NewVersion`, `CommitSequence?`,
`OrderApproximate: bool`, `IsCurrentValue: bool`, `ConcurrentOverwrite?` (OverwriteView), `Value` (ValueStateView),
`OverwrittenValue?` / `RefusedValue?` (ValueStateView), `ResolvesEntry?` (for an accepted override: the refused
write it resolves), `OccurredAt`.

**WriterView**: `Source`, `ExecutableNodeId?`, `ActivityExecutionId?`, `LogicalInvocationId?`, `AttemptId?`,
`BranchId?`, `ExecutionPathId?`, `AlterationId?`.

**OverwriteView**: `OverwrittenWriter` (WriterView), `OverwrittenCommitSequence`, `IsLostUpdate`.

**ValueStateView**: `State` (`captured`, `omitted`, `redacted`, `expired`), `Digest?`, `EvidenceId?` (for the
authorized payload read), `IsOperational` (overwritten or refused value kept for recovery).

**JournalCommitView**: `CommitSequence`, `OccurredAt`, `CheckpointName?`, `Transitions` (list of activity execution id,
node id, kind, branch/path), `Writes` (list of VariableWriteEntryView).

**InstanceComparisonView**: `Left`/`Right` instance ids, `Executable`, `Comparable: bool`, `RefusalReason?`,
`FirstDivergence?` (left and right JournalCommitView at the first differing commit), `Variables` (per variable:
`FinalDigestDiffers`, `WriteSequenceDiffers`, left and right write counts), `Partial: bool` (one side still running).

**Instance details (existing view)**: + `LostUpdateWarningCount`, `ConcurrentOverwriteCount`.

## Rules

- Order: commit sequence; ties within one commit by frame revision, then record order.
- Outcome mapping from spec 1221's commit outcomes: blind, current and returned-value writes are `applied`; an
  accepted override is its own outcome and counts as applied.
- Current value: the last applied entry of each variable in that order.
- Concurrent overwrite: research R-3.
- Values: research R-4; never inline.
