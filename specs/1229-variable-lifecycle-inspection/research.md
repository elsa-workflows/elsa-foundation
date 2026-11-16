# Research: Variable lifecycle inspection

Each item records the decision the plan follows, why, and the alternatives weighed. Alternatives for the lifecycle
itself are in ADR 0092; these are this spec's own design choices.

## R-1 — Instance commit sequence (Q1)

- **Decision**: a per-instance `long` counter. The last value lives on the workflow execution state; the committer
  increments it inside the fenced commit and stamps the commit's records. In a coalescing drain each logical
  checkpoint takes its number when it is taken (the drain is single-threaded per instance), and the folded durable
  commit stores the highest. Background results commit through the same fenced path, so the order stays total.
- **Rationale**: the write record's `CommitOrder` is the frame revision. It orders one frame's writes only; root and
  container variables live in different frames, and lifecycle transitions are not in a frame at all. Timestamps are not
  a total order (equal or skewed clocks across nodes).
- **Alternatives**: frame revision plus timestamp (not total; rejected); the scheduler's `ExecutionSequence` (scheduling
  order, not commit order, per #3291; rejected); a global database sequence (cross-instance cost, provider-specific;
  rejected).
- **To verify in the first task**: that every commit path (inline drain, background result, alteration, recovery) goes
  through `RuntimeCheckpointCommitter` under the instance fence. A path that does not is listed and given the stamp.

## R-2 — Derived read model, no new store (Q2)

- **Decision**: read the instance's activity execution states page by page, flatten write records and lifecycle
  stamps, merge alteration records, sort by commit sequence. Paging uses the commit sequence as the continuation.
- **Rationale**: spec 1221 measured and chose records on the writer's state; a second copy would double write cost.
- **Alternatives**: a write-history table or projection (extra rows per write; kept as the fallback if SC-004 fails);
  ADR 0084's commit journal (about commit cost; not the same record).
- **Risk**: sorting across pages needs the rows ordered by first commit sequence; if the store cannot, the service reads
  the instance's rows in batches and merges. Measured for SC-004.

## R-3 — Concurrent overwrite rule (Q3, Q4)

- **Decision**: entry W2 (logical invocation I2) replaced the value of entry W1 (logical invocation I1). W1 is the
  previous applied entry for the same variable in commit order (or the version's writer, for an alteration). W2 is a
  concurrent overwrite when I1 ≠ I2 and W1's commit does not happen before I2. Happens-before is walked backwards from
  I2 over scheduling provenance: the scheduling activity, the parent (for a parent callback) and, new, the arrivals
  that satisfied a join. W1 happens before I2 when the walk reaches I1 through a scheduling commit whose sequence is at
  or after W1's commit sequence. The walk stops at commit sequences below W1's, so it is bounded.
- **Rationale**: covers scripts, C# writes, sub-workflows and run-time paths (#3292) without structural analysis.
  Read time only, except the join arrivals.
- **Alternatives**: "W1 committed after I2 started" (never flags an intrinsic `Set` or an inline leaf without a gap,
  which start and write in one dispatch; rejected); spec 266's static unordered-writer pairs (miss everything static
  analysis cannot see); branch ids alone (nested forks and loops reuse or nest branches; insufficient); recording the
  overwrite at write time (run-time cost for something derivable).
- **Gap found**: `ActivitySchedulingProvenance` holds one `SchedulingActivityExecutionId`. A join completed by the last
  of several arrivals names only that one, so without the change every write after a join would look concurrent with
  the other branches. The plan adds the satisfying arrivals to provenance, for join completions only.
- **Container writes**: a container that writes on a later step while its child runs, and the child that writes too,
  are concurrent unless one was scheduled by the other's writing commit. This is intended.

## R-4 — Values (Q5)

- **Decision**: map each historical value to `captured`, `omitted`, `redacted` or `expired`, reusing the capture and
  access states of `ActivityExecutionInspectionValueSnapshotView`; payloads only via the existing value-payload read
  with its permission.
- **Rationale**: one disclosure path; ADR 0092 decision 13 needs the four states distinct from `null`.
- **Alternatives**: inline values in the history view (a second disclosure path; rejected).

## R-5 — Comparison (Q6)

- **Decision**: same `WorkflowExecutableIdentity` only. Match activity executions by executable node id and invocation
  origin path; walk both journals in commit order and report the first commit whose matched transitions differ. Compare
  final values and write sequences by digest.
- **Alternatives**: cross-version comparison by authored activity id (node ids and structure may change; out of scope).

## R-6 — Lost-update diagnostic (Q7)

- **Decision**: counts on the instance details view; filters on the history. No incident.
- **Rationale**: spec 1221's implementation found an incident in a commit ends the coalescing session, reopening the
  inline gap.

## R-7 — Alteration write record (Q9)

- **Decision**: the `ModifyVariable` handler builds a `VariableWriteRecord` (source `Alteration`, writer = operator
  alteration id) and stores it on the alteration job outcome, stamped with the commit sequence of the alteration's
  checkpoint.
- **Alternatives**: infer from the version's writer only (shows the last alteration, not each; rejected).

## R-8 — Legacy records

- **Decision**: records without a commit sequence are ordered by frame revision then time and marked approximate;
  instances without records show "no history recorded".
