# Feature Specification: Variable lifecycle inspection

**Feature Branch**: `claude/3052-s6-define-b4bd05`

**Created**: 2026-11-16

**Status**: Approved — re-confirmed 2026-11-17 after the pre-merge review; it applies ADR 0092 as merged (`f52a661df`); no ADR change is needed.

**Initiative**: part of [#3052](https://github.com/elsa-workflows/elsa-foundation/issues/3052), spec row **S-6**,
decisions **D-8** and **D-9**.

**Issues**: [#3290](https://github.com/elsa-workflows/elsa-foundation/issues/3290) (each variable's write history and
the winning write), [#3291](https://github.com/elsa-workflows/elsa-foundation/issues/3291) (commit-ordered journal and
comparing two instances), [#3292](https://github.com/elsa-workflows/elsa-foundation/issues/3292) (a trace when a write
overwrites another open branch's write).

**Input**: When branches write a variable in an order the workflow does not define, the last commit wins (D-8), and a
write on a stale base raises a lost-update warning (ADR 0092). The engineer must be able to see what happened: who
wrote each variable, in which order, with which values, which write won, which writes overwrote a concurrent write,
and where two instances of one version took different paths. This spec builds the inspection read model and API over
the write history that spec 1221 (S-10) records.

## Approval

**Which ADR text this spec applies.** ADR 0092 as accepted and merged into the initiative branch (`f52a661df`), with
its decision 8 amendment (the logical invocation identity). Decision 13 names this spec as the consumer of the write
history: "variable write history in inspection, the commit-ordered journal and instance comparison, and the runtime
trace of overwrites". Where ADR 0092 does not apply, the initiative branch's versions of ADR 0045, ADR 0048 and
ADR 0090 apply.

| Source | What this spec takes from it | Text applied |
|---|---|---|
| **ADR 0092** (variable reads and writes share one versioned lifecycle) | Decision 6 (the lost-update warning is an instance diagnostic and a write-history entry), decision 8 (accept recorded completion), decision 9 (refreshed snapshots stay in the history), decision 12 (`ModifyVariable` versions), decision 13 (write-history metadata, optional values, operational data, omitted/redacted/expired ≠ `null`) | `f52a661df` and the decision 8 amendment |
| **Spec 1221** (S-10, variable lifecycle model) | The write-history record (FR-024) including the instance commit order, value capture and retention (FR-025), operational data (FR-026), intermediate writes kept (FR-027); per-variable versions and digests (FR-001) | as merged (`06dacfafe`) |
| **D-8**, **D-9** | Last commit wins with a warning, never a refusal; one set of rules for frames and rows | board |

**What spec 1221 provides and what this spec adds.** Spec 1221 records the write-time data this spec reads: the write
records with their frame, digest and instance commit sequence, the activity lifecycle stamps with their commit
sequence, each activity execution's commit-sequence range, the alteration write records, and the written values as
activity value evidence (*Data* in the data model). This spec adds the scheduling causes of an invocation (Q3), a
per-activity lost-update count and the store columns it queries by (Q2, Q7), and the read side.

**Out of scope.** Studio presentation (pages, diagnostics badges, diff views) belongs in elsa-studio and is specified
there against the API this spec defines. The physical storage of the write history is spec 1221's choice. Write-time
behaviour (the commit rule, the warning, `Fault`) is spec 1221's. A merge policy is not part of ADR 0092.

## Terms

- *Write history*, *version*, *base*, *lost update*, *invocation*: as in ADR 0092 and spec 1221.
- *Variable instance*: one variable in one frame (frame and key). Versions count per variable instance, so loop
  iterations and container activations each have their own history.
- *Applied write*: a write that changed the value (its new version differs from the version it met), whatever its
  outcome: blind, current base, returned value, lost-update warning or accepted override. A refused `Fault` write is
  listed in the history but is not applied. A write on a stale base that wrote the value already there (two branches
  both writing 1) is listed as a lost-update warning but is not applied: the version does not change.
- *Commit sequence*: a per-instance number that orders every commit point of one instance, across all variables and
  frames (Q1).
- *Journal*: the instance's commit-ordered list of what happened: which activity executions were scheduled, started,
  suspended, resumed, completed or faulted, on which branch or path, and which variable writes each commit applied.
- *Happens before*: commit C happens before invocation I when I was scheduled, directly or through the invocations
  scheduled in between, by C or by a later commit of the same invocation that made C (Q3).
- *Concurrent overwrite*: an applied write that replaced the value of a write that does not happen before the
  overwriting invocation. An applied lost update (a warning, or an accepted override) is one kind: the one where the
  overwriter had read the variable.
- *Value state*: what inspection can show for a historical value: `captured`, `omitted` (not captured), `redacted`
  (captured, caller may not see it), `expired` (retention removed it). None of them is `null`; a captured `null` is a
  value.

## Decisions

- **Q1 — What orders writes across variables?** The frame revision orders the writes of one frame only, not writes in
  different frames and not activity lifecycle steps.
  **Decision:** a per-instance *commit sequence*: the execution lease's fencing token (issued one higher on every
  acquisition) followed by the commit's number within that lease. It grows with commit order across owners, needs no
  stored counter and costs no extra read or write; the numbers are ordered, not dense. It is assigned to each logical
  checkpoint before a coalescing fold, so intermediate writes keep their order, and stamped on every write record and
  every activity lifecycle stamp, including the commit that scheduled an invocation. Spec 1221 records it as part of
  FR-024 ("the instance commit order"); this spec reads it and relies only on its order. Older records, and commits
  made outside an execution lease, carry none and read back as absent.
- **Q2 — Where do the journal and the history come from?**
  **Decision:** derived from what is already committed: write records on the writer's activity execution state,
  lifecycle stamps, and alteration records. Each activity execution carries the first and last commit sequence of its
  stamps and records, so a page reads only the rows whose range meets it. No new store and no extra rows. A projection
  is added later only if measurement shows the query is too slow for large instances (SC-004). ADR 0084's journal is
  about commit cost and is not used.
- **Q3 — What is "another open branch" (#3292)?** Static structure cannot see script `setVariable`, C# writes,
  sub-workflows or run-time Flowchart paths.
  **Decision:** a concurrent overwrite as defined in *Terms*, decided from scheduling provenance, which exists for
  every write source, so it covers scripts, C# writes, sub-workflows and run-time Flowchart paths without structural
  analysis. Writes of the same logical invocation never count. Provenance names one scheduling activity per
  invocation, which is not enough: a loop schedules each iteration with the loop as scheduler, a parent callback does
  not name the child whose completion caused it, and a join names only its last arrival. This spec adds the
  *scheduling causes* to provenance: the completed invocations whose completion caused the scheduling (the child for a
  callback or a next iteration, the arrivals that satisfied a join). A simpler rule, "the replaced write committed
  after the overwriter started", was rejected: an intrinsic `Set`, and every inline leaf once there is no gap (ADR 0092
  decision 10), starts and writes in one dispatch, so it would never flag them.
- **Q4 — Is the trace always on, or does it follow the inspection granularity?**
  **Decision:** always available. It is derived when read (Q2, Q3), so it costs nothing at run time beyond the
  scheduling causes. The write-history metadata is mandatory under ADR 0092 decision 13 and does not depend on the
  inspection granularity; only values follow the capture settings (Q5).
- **Q5 — Values: what does inspection show?**
  **Decision:** capture and retention are spec 1221's (FR-025). This spec shows each historical value through its
  value state, and a payload only through an authorized payload read. A written value is activity value evidence of
  the writing activity execution and uses the existing payload read. The value a lost-update warning overwrote, a
  refused `Fault` value and an alteration's values live on the write record, so this spec extends the payload read to
  them. A value that capture did not keep shows as `omitted`. Overwritten and refused values are operational data:
  shown under the same protection rules, never removed by inspection retention.
- **Q6 — Which instances can be compared?**
  **Decision:** two instances of the same executable version only; any other pair is refused with a reason.
  Activity executions are matched by their *run-time path*: the executable node ids from the root down to the
  activity, each with its occurrence number under its parent activity execution in scheduling order (the third
  iteration, the second resume). Row ids and iteration ids differ between instances and are not used. The comparison
  shows the first commit where the matched transitions differ, and per variable whether the final value and the
  sequence of written values differ, compared by digest, so it works when values were not captured and never
  discloses a value.
- **Q7 — How does the lost-update warning reach the user as an "instance diagnostic"?**
  **Decision:** as data, not as an incident. The instance details carry the count of lost-update warnings. Each
  activity execution keeps the count of its own lost-update warnings, written with the row that records them, and the
  details sum it over the instance through a store column, so reading the details stays cheap and no commit writes a
  row it would not write anyway. Concurrent overwrites are derived, so
  their count comes with the filtered history, not with the details. An incident is not used: an incident in a commit
  ends the coalescing session and would reopen the inline gap. Studio shows the count and links to the history
  (elsa-studio).
- **Q8 — Who may read it?**
  **Decision:** the existing permission for activity execution inspection reads the history, journal, trace and
  comparison metadata. Values follow the existing sensitive-value rules and the payload permission; nothing new is
  disclosed by the new endpoints.
- **Q9 — Are `ModifyVariable` changes in the history?** An alteration is not an activity execution.
  **Decision:** yes. Spec 1221 records a write record (source `Alteration`) on the alteration's outcome with its
  commit sequence; the read model merges it into the history. Without it, an operator's correction shows as a version
  gap.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — See who wrote a variable and which write won (Priority: P1)

Two instances of one workflow version end with different values for `total`. The engineer opens one instance and lists
the writes to `total`: each writer (node, invocation, attempt), the kind of write, the base and new versions, the
commit order, the value state, and which write is the current value.

**Why this priority**: D-8 makes last-commit-wins defined behaviour; without this view the engineer cannot see it.

**Independent Test**: run a workflow where two parallel branches write `total`; read the history of `total`; the
entries match the commits in order and the last applied entry equals the current value.

**Acceptance Scenarios**:

1. **Given** three writes to X in commits 4, 7 and 9, **When** the history of X is read, **Then** it lists them in that
   order with writer, source, base and new versions, and the write of commit 9 is marked as the current value.
2. **Given** a write whose value equals the current value (no version change), **When** the history is read, **Then**
   it is not listed as an applied write (spec 1221: an unchanged value writes nothing).
3. **Given** X moved `0 → 1 → 0` inside one buffered segment, **When** the history is read, **Then** both writes are
   listed with their own commit sequence.
4. **Given** values were not captured, **When** the history is read, **Then** every value shows `omitted`, not `null`;
   a captured `null` shows `captured` with value `null` to a caller allowed to see it.
5. **Given** a caller without the sensitive-value permission, **When** the history is read, **Then** captured values
   show `redacted` and metadata is still shown.
6. **Given** an operator changed X with `ModifyVariable`, **When** the history is read, **Then** the change is listed
   with source `Alteration` in its commit position (Q9).

---

### User Story 2 — See lost updates and concurrent overwrites (Priority: P1)

The engineer sees, for an instance, every lost-update warning and every concurrent overwrite: the variable, both
writers and their branches, the base and current versions, and the overwritten value (to someone allowed to see it), so
it can be restored.

**Why this priority**: ADR 0092 makes the warning an instance diagnostic; #3292 asks for a trace of writes static
analysis cannot see.

**Independent Test**: two parallel branches read `count` and write `count + 1`, and a `Set` on a third branch writes
`count` blindly; the instance shows one lost update and the concurrent overwrites, and nothing for a sequential run.

**Acceptance Scenarios**:

1. **Given** a lost-update warning, **When** the instance details are read, **Then** the lost-update count is 1, and
   the filtered history names the variable, both writers, both branches or paths, base and current versions, and the
   overwritten value's state.
2. **Given** a blind `Set` on a third parallel branch that replaced a value another branch committed, **When** the trace
   is read, **Then** it is listed as a concurrent overwrite, not as a lost update.
3. **Given** the same workflow with the branches in sequence, or joined (a join or a `Parallel` completing) before a
   later write, **When** the trace is read, **Then** it is empty.
4. **Given** a loop (`Do`, `While`, `For`, `ForEach`) whose body writes X in every iteration, **When** the trace is
   read, **Then** it is empty: each iteration is scheduled by the completion of the one before.
5. **Given** an invocation that writes X on suspension and again on completion, **When** the trace is read, **Then** it
   does not list the second write as an overwrite of itself.
6. **Given** a `Fault` lost update, **When** the history is read, **Then** the refused write is listed with outcome
   fault and its refused value's state; it is not applied and not an overwrite, and the current value is unchanged.
7. **Given** an operator accepted the recorded completion of that faulted step (ADR 0092 decision 8), **When** the
   history is read, **Then** the accepted write is listed as an applied write with source accepted completion and
   outcome accepted override, linked to the refused write it resolves, and both entries stay in the history.

---

### User Story 3 — Read an instance's commit-ordered journal (Priority: P2)

The engineer reads the journal of an instance: per commit, which activity executions were scheduled, started,
suspended, resumed, completed or faulted, on which branch or path, and which variable writes the commit applied.

**Why this priority**: the scheduling sequence and timestamps do not show commit order; the order of commits explains
which write won.

**Independent Test**: a workflow with a fork, a suspension and a resume; the journal lists the commits in order and the
resume appears in its own commit.

**Acceptance Scenarios**:

1. **Given** a completed instance, **When** the journal is read, **Then** every commit appears once, in commit sequence
   order, with its activity transitions and variable writes.
2. **Given** an instance with coalesced (boundary-level) inspection, **When** the journal is read, **Then** logical
   checkpoints folded into one durable commit keep their own order (Q1).
3. **Given** a large instance, **When** the journal is read, **Then** it pages by commit sequence and a page continues
   exactly where the previous one ended.
4. **Given** a background admission refresh superseded a snapshot, **When** the journal is read, **Then** both
   evaluations appear, linked with the reason.

---

### User Story 4 — Compare two instances of one version (Priority: P2)

The engineer compares two instances of the same version and sees where their commit order first differs and which
variables ended with different values or write sequences.

**Why this priority**: different results between instances of one version are the symptom D-8 can cause.

**Independent Test**: run one version twice with branches that finish in a different order; the comparison shows the
first diverging commit and the variable that differs.

**Acceptance Scenarios**:

1. **Given** two instances of one version with identical runs, **When** compared, **Then** no divergence is reported.
2. **Given** two instances whose branches committed in a different order, **When** compared, **Then** the first
   diverging commit is shown with the activity executions on each side, matched by run-time path (Q6).
3. **Given** different final values for X, **When** compared, **Then** X is listed as different by digest, without
   disclosing either value to a caller not allowed to see it.
4. **Given** two instances of different versions, **When** compared, **Then** the request is refused with a reason.

---

### User Story 5 — See a variable's current state (Priority: P3)

The engineer lists the instance's variables with their current version, last writer, last commit sequence and value
state.

**Independent Test**: after a run, the list matches the frames' current versions and writers.

**Acceptance Scenarios**:

1. **Given** an instance with root and container variables, **When** the list is read, **Then** each variable shows its
   scope, version, last writer and value state.
2. **Given** an instance started before versions existed, **When** the list is read, **Then** variables show version 0
   and "no history recorded", not "no writes".
3. **Given** a loop body with a local variable X written in three iterations, **When** the list and the history are
   read, **Then** each iteration's X is a separate variable instance (its own frame) with its own versions and current
   value.

### Edge Cases

- Instance deleted: its history, journal and trace go with it; comparison with it is refused as not found.
- Closed-scope (graph-row) variables keep their version, digest and writer with the row; they are listed like any
  other variable instance.
- A legacy instance (no write records): history is empty with "no history recorded". Records without a commit sequence
  are ordered by frame revision and time, after nothing and before every sequenced record, and are marked approximate.
- Join rules may change; this spec records only which completions caused a scheduling, which holds for any join rule.
- A retried invocation: its attempts share one logical invocation (ADR 0092 decision 8, amended); writes of different
  attempts of one logical invocation are not concurrent overwrites of each other.
- A re-executed invocation is a new logical invocation; it can overwrite its predecessor.
- An alteration write has no activity execution: it happens before an invocation only if that invocation was scheduled
  by a commit at or after it (a re-execution after `ModifyVariable`); otherwise a later write that replaces it is a
  concurrent overwrite, which keeps the operator's correction visible.
- Expired values: shown `expired`; operational data (overwritten value, refused write) stays while recovery needs it.
- A comparison where one instance is still running compares what is committed and says so.

## Requirements *(mandatory)*

### Functional Requirements

**Data read from spec 1221**

- **FR-001**: The read model MUST use the write-time data spec 1221 records: the instance commit sequence on write
  records and lifecycle stamps (including the scheduling commit), the frame and digest on write records, the
  alteration write records, each activity execution's first and last commit sequence, and the written values as
  activity value evidence. Records without them MUST read back as absent and be handled as legacy (*Edge Cases*).

**Write history (#3290)**

- **FR-002**: The API MUST list an instance's variable instances with scope, frame, key, current version, last writer,
  last commit sequence and value state.
- **FR-003**: The API MUST list the write history of an instance, filterable by variable instance, by source, by
  outcome (applied, lost-update warning, lost-update fault, accepted override) and by concurrent overwrite, ordered by
  commit sequence and paged.
- **FR-004**: Each entry MUST show the variable (scope, frame and key), the writer (node, logical invocation, attempt),
  the source (capture, intrinsic, session change, alteration, background result, accepted completion), base, current
  and new versions, the commit sequence, the frame revision, the outcome, and the value state. Writes that changed
  nothing MUST NOT be listed. The applied entry whose new version is the variable instance's current version MUST be
  marked as the current value.
- **FR-004a**: An accepted override MUST be shown as an applied write and MUST link to the refused write it resolves;
  the refused write stays in the history.
- **FR-005**: `ModifyVariable` changes MUST appear in the history with source `Alteration` (Q9).
- **FR-006**: Values MUST be shown only through value states and an authorized payload read; omitted, redacted and
  expired MUST be distinct from a captured `null` (ADR 0092 decision 13). The payload read MUST reach write-record
  values (written, overwritten, refused) as well as activity value evidence, under the existing payload permission.

**Overwrites and lost updates (#3292)**

- **FR-007**: The read model MUST mark every concurrent overwrite as defined in *Terms*, for every write source,
  derived when read from scheduling provenance. Provenance MUST record the scheduling causes of an invocation: the
  completed invocations whose completion caused it (a callback's or next iteration's completed child, the arrivals that
  satisfied a join).
- **FR-008**: A lost-update entry MUST name both writers and both branches or paths, base and current versions, and the
  state of the overwritten value (or refused value under `Fault`). A refused `Fault` write MUST NOT be marked as an
  overwrite.
- **FR-009**: The instance details MUST carry the lost-update warning count, summed from a per-activity count kept
  with each row (Q7); the filtered history MUST return the total of concurrent overwrites.

**Journal and comparison (#3291)**

- **FR-010**: The API MUST return an instance's journal in commit sequence order, paged, with each commit's activity
  transitions (scheduled, started, suspended, resumed, completed, faulted, cancelled,
  superseded), branch or path,
  input refreshes, and applied writes.
- **FR-011**: The API MUST compare two instances of the same executable version: activity executions matched by
  run-time path (Q6); the first diverging commit; per variable whether the final digest and the sequence of written
  digests differ. Any other pair MUST be refused with a reason.
- **FR-012**: Comparison MUST NOT disclose values; it compares digests.

**Access, paging and cost**

- **FR-013**: All new reads MUST use the existing activity-execution inspection permission; values MUST follow the
  existing sensitive-value rules (Q8).
- **FR-014**: Paging MUST use a cursor of commit sequence and position within the commit, so a page never ends inside
  a commit without the next page continuing exactly there.
- **FR-015**: The run-time cost this spec adds MUST be limited to the scheduling causes (FR-007) and the per-activity
  lost-update count on rows already being written (FR-009); everything else it presents is recorded by spec 1221 or
  computed when read.
- **FR-016**: The store MUST be able to filter an instance's activity executions by their commit-sequence range and
  sum their lost-update counts without reading the rows' documents (store columns and an index, every provider).

### Key Entities

- **Variable summary**: one variable instance with its current version, last writer, last commit sequence and value
  state.
- **Write history entry**: a view of one spec 1221 write record (or alteration record) with its commit sequence,
  outcome, overwrite marking and value state.
- **Journal commit**: one commit sequence number with its activity transitions, input refreshes and applied writes.
- **Instance comparison**: the matched activity executions, the first diverging commit and the per-variable
  differences of two instances of one version.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For every write in the test suite, the history reconstructs who wrote each variable instance, in which
  order, and which write is the current value (100%), including `0 → 1 → 0` within one buffered segment and loops.
- **SC-002**: Every applied lost update and every concurrent overwrite in the reproduction suite is shown (0 missed),
  and sequential runs, joins, `Parallel` and loops show none (0 false traces).
- **SC-003**: An engineer finds why two instances of one version ended differently from the comparison alone, without
  logs or a debugger, in the reproduction scenario.
- **SC-004**: The history and journal of an instance with 10,000 activity executions return their first page within
  the existing activity-list read time on the same store, measured in the plan; the instance details read time does
  not change measurably.
- **SC-005**: No value is shown to a caller who cannot see it through the existing inspection reads (0 disclosures in
  the access tests).
- **SC-006**: Run-time overhead of the scheduling causes and the per-activity count is within noise of spec 1221's
  benchmark.

## Assumptions

- Spec 1221 records the write-time data of FR-001; this spec adds the scheduling causes, the per-activity
  lost-update count, the store columns and the read side.
- Merging activity rows by commit sequence, using each row's commit-sequence range, is fast enough per page; a
  projection follows only on measurement (Q2).
- Studio presentation is specified in elsa-studio against this API.
- The write-history physical storage is spec 1221's (records on the writer's activity execution state); this spec
  depends on it only through the query in Q2.
- Values depend on spec 1221's value capture (FR-025); until it captures values, they show `omitted`.
