# Data model: Variable lifecycle inspection

## Data recorded by spec 1221 (read here)

Additive optional members; older records read back with them absent.

| Where | Member | Meaning |
|---|---|---|
| Workflow execution state | `LastCommitSequence: long` | The instance's counter, advanced under the fence when a logical checkpoint commits (Q1). |
| Workflow execution state | `LostUpdateWarningCount: long?` | Applied lost-update warnings, counted in the commit that raises them (Q7). |
| `VariableWriteRecord` | `CommitSequence: long?` | Commit sequence of the logical checkpoint that committed the write. |
| `VariableWriteRecord` | `FrameId: string?` | The frame written; with the key it identifies the variable instance. |
| `VariableWriteRecord` | `Digest: string?` | Digest of the value the write produced (the resulting version's digest). |
| `ActivityExecutionState` | `Transitions: IReadOnlyList<ActivityLifecycleStamp>?` | One stamp per scheduled, started, suspended, resumed, completed, faulted. |
| `ActivityExecutionState` | `FirstCommitSequence: long?`, `LastCommitSequence: long?` | Range of the row's stamps and records, stored so the store can filter by it. |
| Alteration job outcome | `VariableWrites: IReadOnlyList<VariableWriteRecord>?` | The `ModifyVariable` write, source `Alteration` (Q9). |

`ActivityLifecycleStamp(Kind, CommitSequence, BranchId?, ExecutionPathId?)`. Input refreshes keep spec 1221's own
records (`ActivityInputRefresh`, `ActivityInputSnapshot.Evaluation`); the journal shows them in the commit that made
them. Writes that changed nothing are not recorded; a refused `Fault` write is.

## Data added by this spec

| Where | Member | Meaning |
|---|---|---|
| `ActivitySchedulingProvenance` | `CausedBy: IReadOnlyList<string>?` | The activity executions whose completion caused this scheduling: the completed child of a parent callback or next iteration, the arrivals that satisfied a join. Absent when the scheduling activity alone is the cause (Q3). |

## Views

**VariableSummaryView**: `ScopeId`, `FrameId`, `VariableKey`, `Version`, `LastWriter` (WriterView),
`LastCommitSequence`, `Value` (ValueStateView), `HistoryRecorded: bool`.

**VariableWriteEntryView**: `EntryId`, `ScopeId`, `FrameId?`, `VariableKey`, `Writer` (WriterView), `Source`,
`Outcome` (`applied`, `lostUpdateWarning`, `lostUpdateFault`, `acceptedOverride`), `BaseVersion?`, `CurrentVersion`,
`NewVersion`, `CommitSequence?`, `FrameRevision`, `OrderApproximate: bool`, `IsCurrentValue: bool`,
`ConcurrentOverwrite?` (OverwriteView), `Value` (ValueStateView), `OverwrittenValue?` / `RefusedValue?`
(ValueStateView), `ResolvesEntryId?` (accepted override → the refused write), `OccurredAt`.

**WriterView**: `Source`, `ExecutableNodeId?`, `ActivityExecutionId?`, `LogicalInvocationId?`, `AttemptId?`,
`BranchId?`, `ExecutionPathId?`, `AlterationId?`.

**OverwriteView**: `OverwrittenEntryId`, `OverwrittenWriter` (WriterView), `OverwrittenCommitSequence`,
`IsLostUpdate`.

**ValueStateView**: `State` (`captured`, `omitted`, `redacted`, `expired`), `Digest?`, `PayloadRef?` (owner kind
`activityExecution` or `alteration`, owner id, entry id and which value: `written`, `overwritten`, `refused`; for the
authorized payload read), `IsOperational` (overwritten or refused value kept for recovery).

**JournalCommitView**: `CommitSequence`, `OccurredAt`, `Transitions` (activity execution id, node id, run-time path,
kind, branch/path), `InputRefreshes`, `Writes` (VariableWriteEntryView).

**InstanceComparisonView**: `Left`/`Right` instance ids, `Executable`, `Comparable: bool`, `RefusalReason?`,
`FirstDivergence?` (left and right JournalCommitView at the first differing commit), `Variables` (per run-time path of
the variable's frame and key: `FinalDigestDiffers`, `WrittenDigestsDiffer`, left and right write counts),
`Partial: bool` (one side still running).

**Instance details (existing view)**: + `LostUpdateWarningCount`.

**Page**: `Items`, `Next?` (opaque cursor of commit sequence and position within the commit), and for the history
`ConcurrentOverwriteTotal?` when filtered on overwrites.

## Rules

- Variable instance: (`FrameId`, `VariableKey`); records without a frame fall back to (`ScopeId`, `VariableKey`) and are
  marked approximate.
- Order: commit sequence, then position in the commit (frame revision, then record order). Legacy records come first,
  ordered by frame revision and time, marked approximate.
- Applied: an entry whose `NewVersion` differs from its `CurrentVersion` (blind, current, returned value, lost-update
  warning, accepted override). `lostUpdateFault` is listed, never applied.
- Current value: the applied entry whose `NewVersion` equals the variable instance's current version.
- Accepted override link: the successor's `RetryOfActivityExecutionId` names the faulted row; its `lostUpdateFault`
  entry for the same variable instance is the one resolved.
- Logical invocation: a writer's row id resolves to `LogicalInvocationId` through its row; an alteration has none.
- Concurrent overwrite: research R-3. Run-time path: research R-5. Values: research R-4.
