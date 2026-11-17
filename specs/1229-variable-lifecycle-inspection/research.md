# Research: Variable lifecycle inspection

Each item records the decision, why, and the alternatives weighed. Alternatives for the lifecycle itself are in ADR
0092; these are this spec's own design choices.

## R-1 — Instance commit sequence (Q1)

- **Decision**: spec 1221 records it (FR-024, "the instance commit order"). The number is the execution lease's fencing
  token in the high 32 bits and the commit's ordinal within that lease in the low 32 bits. The fencing token is issued
  one higher on every acquisition, so numbers grow across owners; within a lease the ordinal grows per commit. Numbers
  are ordered, not dense. `RuntimeCheckpointCommitter` numbers each logical checkpoint and stamps the records and
  lifecycle stamps the handlers built before `CommitAsync`. In coalescing mode the coalescing store sits below the
  committer, so each logical checkpoint is numbered before the fold. A commit replayed with the same commit id keeps
  its number. Background results and alterations commit through the same committer.
- **Rationale**: the frame revision orders one frame only; timestamps are not a total order; the scheduler's
  `ExecutionSequence` is scheduling order, not commit order.
- **Cost**: none at commit beyond stamping: no stored counter, no extra read or write.
- **Alternatives**: a counter stored on the workflow execution state (the workflow execution change is optional in a
  commit today, so every numbered commit would write that row); frame revision plus timestamp (not total); a global
  database sequence (cross-instance cost, provider-specific).
- **Limit**: a commit made outside an execution lease is not numbered. Task T002 lists such commit paths (recovery and
  deletion among them) and how the read model orders what they commit (as legacy, approximate).

## R-2 — Derived read model, no new store (Q2)

- **Decision**: page an instance's activity execution rows by their commit-sequence range (`FirstCommitSequence`,
  `LastCommitSequence`): a page from cursor C reads the rows whose range reaches past C, flattens their stamps and
  records, merges alteration records, sorts by (commit sequence, position) and cuts at the page size. The cursor is
  (commit sequence, position), so a page may end inside a commit.
- **Rationale**: spec 1221 measured and chose records on the writer's state; a second copy would double write cost. A
  row's records span many commits (a container appends on each callback), so ordering rows by their first sequence is
  not enough; the range is.
- **Alternatives**: a write-history table or projection (kept as the fallback if SC-004 fails); ADR 0084's commit
  journal (about commit cost; not the same record).
- **Legacy**: rows without a range are read once, ordered by frame revision and time, and come before the sequenced
  records.

## R-3 — Concurrent overwrite rule (Q3, Q4)

- **Decision**: entry W2 (writer row R2, logical invocation I2) replaced entry W1 (the previous applied entry of the
  same variable instance). W2 is a concurrent overwrite when W2 is applied, I1 ≠ I2, and W1's commit does not happen
  before I2.
- **Happens before**: walk backwards from R2's scheduled stamp over provenance edges. Each edge goes from a row to the
  row that scheduled it (`SchedulingActivityExecutionId`) and to each row in `CausedBy`, labelled with the commit
  sequence of the scheduling. W1 (commit c1, row R1) happens before R2 when the walk reaches a row of I1 over an edge
  labelled ≥ c1, or reaches an alteration-scheduled row at a commit ≥ c1 for an alteration W1. The walk prunes edges
  labelled below c1, so it is bounded by the commits between W1 and W2.
- **Why `CausedBy`**: `Do`, `While`, `For` and `ForEach` schedule each iteration with the loop as scheduler; a parent
  callback does not name the child whose completion triggered it; a join or `Parallel` completion names one arrival.
  Without the causes every loop write, every write after a `Parallel` and every write after a join would be flagged.
  `Sequence` already passes the completed child.
- **Why the scheduled stamp**: a container schedules a child at commit 3, writes at 5, the child starts at 6 and
  overwrites. The child was scheduled before the container's write, so it is concurrent; using the start commit would
  call it ordered.
- **Lost updates**: an applied lost update (warning, accepted override) satisfies the rule. A refused `Fault` write
  replaced nothing and is never an overwrite.
- **Alternatives**: "W1 committed after I2 started" (never flags an intrinsic `Set` or a gap-free inline leaf);
  spec 266's static unordered-writer pairs (miss what static analysis cannot see); branch ids alone (nested forks and
  loops); recording the overwrite at write time (run-time cost for something derivable).

## R-4 — Values (Q5)

- **Decision**: map each value to a value state and read payloads through an authorized payload read extended to
  write-record values (written, overwritten, refused), addressed by owner (activity execution or alteration), entry and
  which value, under the existing payload permission.
- **Mapping from the existing activity value-evidence states**: `notCaptured`, `metadataOnly` → `omitted`;
  `payloadCaptured`, `diagnosticSnapshotCaptured` with access → `captured`; access `redacted` → `redacted`; a value
  removed by spec 1221's retention → `expired` (new; FR-025).
- **Alternatives**: inline values in the history view (a second disclosure path); the existing route only (needs an
  activity execution id and resolves only activity value evidence, so it cannot reach write-record values or
  alterations).

## R-5 — Comparison (Q6)

- **Decision**: same `WorkflowExecutableIdentity` only. A row's run-time path is the list of (executable node id,
  occurrence) from the root down, where occurrence numbers the rows of that node under the same parent row in order of
  their scheduled commit. Match rows by run-time path; walk both journals in commit order and report the first commit
  whose matched transitions differ. Per variable instance (frame's run-time path and key), compare the final digest
  and the list of written digests.
- **Rationale**: row ids and iteration ids embed row ids and differ per instance; the static invocation origin has no
  iteration segment.
- **Alternatives**: cross-version comparison by authored activity id (node ids and structure may change).

## R-6 — Lost-update diagnostic (Q7)

- **Decision**: a lost-update warning count kept on the instance by spec 1221 in the committing write; concurrent
  overwrites counted by the filtered history. No incident.
- **Rationale**: computing derived counts on every details read would run the whole read model; an incident in a
  commit ends the coalescing session and would reopen the inline gap.

## R-7 — Alteration write record (Q9)

- **Decision**: spec 1221's `ModifyVariable` handler records a `VariableWriteRecord` (source `Alteration`, writer the
  alteration) on the alteration job outcome with commit sequence, frame and digest; the read model merges it.

## R-8 — Applied and current value

- **Decision**: applied means the write changed the version; the current value is the applied entry whose new version
  equals the variable instance's current version. Writes that changed nothing are not recorded by spec 1221 (an
  unchanged value writes nothing) and are skipped if met in older records.
