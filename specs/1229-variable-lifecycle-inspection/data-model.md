# Data model: Variable lifecycle inspection

## Data recorded by spec 1221 (read here)

Additive optional members; older records read back with them absent.

| Where | Member | Meaning |
|---|---|---|
| `VariableWriteRecord` | `CommitSequence: long?` | Commit sequence of the logical checkpoint that committed the write: lease fencing token, then ordinal within the lease (ordered, not dense). |
| `VariableWriteRecord` | `FrameId: string?` | The frame written; with the key it identifies the variable instance. |
| `VariableWriteRecord` | `Digest: string?` | Digest of the value the write produced, when it applied. |
| `ActivityExecutionState` | `Transitions: IReadOnlyList<ActivityLifecycleStamp>?` | One stamp per scheduled, started, suspended, resumed, completed, faulted, cancelled. |
| `ActivityExecutionState` | `FirstCommitSequence: long?`, `LastCommitSequence: long?` | Range of the row's stamps and records. |
| Alteration job outcome | `VariableWrites: IReadOnlyList<VariableWriteRecord>?` | The `ModifyVariable` write, source `Alteration` (Q9). |
| `ActivityExecutionState.ValueSnapshots` | snapshot with subject `VariableWrite` | The written value: name = variable key, phase = scope id, sequence = new version, metadata `runtime.variableWrite.scopeId` and `runtime.variableWrite.newVersion`. |
| Closed-scope row metadata | `runtime.variable.version`, `.digest`, `.writer` | A graph-row variable's version, digest and writer. |

`ActivityLifecycleStamp(Kind, CommitSequence, BranchId?, ExecutionPathId?, Evaluation?)`; `Evaluation` is the input
evaluation a refreshed background step ran on (set only when above 1). Input refreshes keep spec 1221's own records
(`ActivityInputRefresh`, `ActivityInputSnapshot.Evaluation`); the journal shows them in the commit that made them.
A write is recorded only when it changed the value or was a lost update (`VariableWriteRecord.IsRecorded`), so an
equal-value write on a stale base is a recorded lost-update warning with an unchanged version; a refused `Fault` write
is recorded.

## Data added by this spec

| Where | Member | Meaning |
|---|---|---|
| `ActivitySchedulingProvenance` | `CausedBy: IReadOnlyList<string>?` | The activity executions whose completion caused this scheduling: the completed child of a parent callback or next iteration, the arrivals that satisfied a join. Absent when the scheduling activity alone is the cause (Q3). |
| `ActivityExecutionState` | `LostUpdateWarningCount: int?` | The row's lost-update warnings, set when the row is written (Q7). |
| Runtime store (every provider) | columns and an index for `FirstCommitSequence`, `LastCommitSequence`, `LostUpdateWarningCount` | Filter rows by commit-sequence range and sum the count without reading documents (FR-016). |

## Views

**VariableSummaryView**: `ScopeId`, `FrameId`, `VariableKey`, `Version`, `LastWriter` (WriterView),
`LastCommitSequence`, `Value` (ValueStateView), `HistoryRecorded: bool`.

**VariableWriteEntryView**: `EntryId`, `ScopeId`, `FrameId?`, `VariableKey`, `Writer` (WriterView), `Source`,
`Outcome` (`applied`, `lostUpdateWarning`, `lostUpdateFault`, `acceptedOverride`), `BaseVersion?`, `CurrentVersion`,
`NewVersion`, `CommitSequence?`, `FrameRevision`, `OrderApproximate: bool`, `IsCurrentValue: bool`,
`ConcurrentOverwrite?` (OverwriteView), `Value` (ValueStateView), `OverwrittenValue?` / `RefusedValue?`
(ValueStateView; the written value resolves through the activity value evidence), `ResolvesEntryId?` (accepted override → the refused write), `OccurredAt`.

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

**Instance details (existing view)**: + `LostUpdateWarningCount` (sum over the instance's rows).

**Page**: `Items`, `Next?` (opaque cursor of commit sequence and position within the commit), and for the history
`ConcurrentOverwriteTotal?` when filtered on overwrites.

## Rules

- Variable instance: (`FrameId`, `VariableKey`); records without a frame fall back to (`ScopeId`, `VariableKey`) and are
  marked approximate.
- Order: commit sequence, then position in the commit (frame revision, then record order). Legacy records come first,
  ordered by frame revision and time, marked approximate.
- Applied: an entry whose `NewVersion` differs from its `CurrentVersion` (blind, current, returned value, lost-update
  warning, accepted override). `lostUpdateFault` is listed, never applied; an equal-value lost-update warning is listed
  and counted, not applied.
- Current value: the applied entry whose `NewVersion` equals the variable instance's current version.
- Accepted override link: the successor's `RetryOfActivityExecutionId` names the faulted row; its `lostUpdateFault`
  entry for the same variable instance is the one resolved.
- Logical invocation: a writer's row id resolves to `LogicalInvocationId` through its row; an alteration has none.
- Concurrent overwrite: research R-3. Run-time path: research R-5. Values: research R-4.
