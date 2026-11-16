# Feature Specification: Variable lifecycle inspection

**Feature Branch**: `claude/3052-s6-define-b4bd05`

**Created**: 2026-11-16

**Status**: Draft — waits for the coordinator's answers to Q1–Q9; the ADR it applies (ADR 0092) is agreed, and no
ADR change is needed.

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
the write history that spec 1221 (S-10) records. It records nothing new about writes beyond what that read model needs.

## Approval

**Which ADR text this spec applies.** ADR 0092 as accepted and merged into the initiative branch (`f52a661df`), with
its decision 8 amendment of 2026-11-16 (the logical invocation identity). Decision 13 names this spec as the consumer of
the write history: "variable write history in inspection, the commit-ordered journal and instance comparison, and the
runtime trace of overwrites". Where ADR 0092 does not apply, the initiative branch's versions of ADR 0045, ADR 0048 and
ADR 0090 apply.

| Source | What this spec takes from it | Text applied | State |
|---|---|---|---|
| **ADR 0092** (variable reads and writes share one versioned lifecycle) | Decision 6 (the lost-update warning is an instance diagnostic and a write-history entry), decision 9 (refreshed snapshots stay in the history), decision 12 (`ModifyVariable` versions), decision 13 (write-history metadata, optional values, operational data, omitted/redacted/expired ≠ `null`) | `f52a661df` and the decision 8 amendment | accepted, merged |
| **Spec 1221** (S-10, variable lifecycle model) | The write-history record (FR-024), value capture and retention (FR-025), operational data (FR-026), intermediate writes kept (FR-027); per-variable versions and digests (FR-001) | as merged (`06dacfafe`) | approved; implementation in progress |
| **D-8**, **D-9** | Last commit wins with a warning, never a refusal; one set of rules for frames and rows | board | agreed |

**Depends on implementation, not on agreement.** The record shapes this spec reads come from spec 1221's
implementation. Where that implementation differs from what inspection needs, the difference is a question below
(Q1, Q9), not a silent change.

**Out of scope.** Studio presentation (pages, diagnostics badges, diff views) belongs in elsa-studio and is specified
there against the API this spec defines. The physical storage of the write history is spec 1221's Open question B and
is not decided here. Write-time behaviour (the commit rule, the warning, `Fault`) is spec 1221's. A merge policy is
not part of ADR 0092.

## Terms

- *Write history*, *version*, *base*, *lost update*, *invocation*: as in ADR 0092 and spec 1221.
- *Commit sequence*: a per-instance number that orders every commit point of one instance, across all variables and
  frames (Q1).
- *Journal*: the instance's commit-ordered list of what happened: which activity executions started, suspended,
  resumed, completed or faulted, on which branch or path, and which variable writes each commit applied.
- *Concurrent overwrite*: an applied write that replaced a value written by another invocation that is not ordered
  before it by the workflow's own scheduling (Q3). A lost update is one kind of concurrent overwrite: the one where the
  overwriter had read the variable.
- *Value state*: what inspection can show for a historical value: `captured`, `omitted` (not captured), `redacted`
  (captured, caller may not see it), `expired` (retention removed it). None of them is `null`; a captured `null` is a
  value.

## Decisions (questions with recommended answers)

Each question has a recommended answer. The spec, plan and tasks are written to the recommended answers; a different
answer is applied to the text before approval.

- **Q1 — What orders writes across variables?** The record from spec 1221's implementation stores the frame revision
  after the write as its commit order. That orders the writes of one frame, not writes in different frames (a root
  variable and a container variable), and not activity lifecycle steps.
  **Recommended:** a per-instance *commit sequence*, assigned at each logical checkpoint in the drain (one drain per
  instance, so the order is total), stamped on every write record and on every activity lifecycle transition. Folded
  (coalesced) commits keep each logical checkpoint's own number, so intermediate writes keep their order. The frame
  revision stays as it is. If spec 1221's implementation adds this number, this spec only reads it; otherwise this
  spec adds it as an optional member, read back as absent on older records.
- **Q2 — Where does the journal and the history come from?** Options: a new journal store; ADR 0084's commit journal;
  or derived from what is already committed.
  **Recommended:** derived. Write records ride the writer's activity execution state (spec 1221's measured choice);
  lifecycle transitions carry their commit sequence (Q1). The read model queries one instance's activity executions,
  paged, and merges them by commit sequence. No new store and no extra rows. A projection is added later only if
  measurement shows the query is too slow for large instances (SC-004). ADR 0084's journal is about commit cost and is
  not used.
- **Q3 — What is "another open branch" (#3292)?** Static structure cannot see script `setVariable`, C# writes,
  sub-workflows or run-time Flowchart paths.
  **Recommended:** a write W2 by invocation I2 is a *concurrent overwrite* when the value it replaced was written by
  another logical invocation I1 and the commit of that write does not *happen before* I2: I2 was not scheduled,
  directly or through the activities, join arrivals and parent callbacks in between, by that commit or a later commit
  of I1. The relation comes from scheduling provenance, which exists for every write source, so it covers scripts, C#
  writes, sub-workflows and run-time Flowchart paths without structural analysis. Writes of the same logical
  invocation never count. A lost update (stale base) is always a concurrent overwrite and is marked as such.
  Provenance today names one scheduling activity per invocation, so a join's other arrivals are missing; this spec adds
  them (the arrivals that satisfied the join), or every write after a join would be flagged. A simpler rule, "I1's
  write committed after I2 started", was rejected: an intrinsic `Set`, and every inline leaf once there is no gap
  (ADR 0092 decision 10), starts and writes in one dispatch, so that rule would never flag them.
- **Q4 — Is the trace always on, or does it follow the inspection granularity?**
  **Recommended:** always available. It is derived when read (Q2, Q3), so it costs nothing at run time and nothing when
  no overwrite happens. The write-history metadata is mandatory under ADR 0092 decision 13 and does not depend on the
  inspection granularity; only values follow the capture settings (Q5).
- **Q5 — Values: who captures them, and what does inspection show?** Spec 1221 FR-025 (value capture with retention)
  is not built yet.
  **Recommended:** capture and retention stay with spec 1221. This spec shows each historical value through its value
  state and reads payloads only through the existing separately authorized value-payload read. Until FR-025 lands,
  every non-operational historical value shows as `omitted`. The value a lost-update warning overwrote and a refused
  `Fault` write are operational data: shown under the same protection rules, never removed by inspection retention.
  The current value of a variable is shown with the same rules.
- **Q6 — Which instances can be compared?**
  **Recommended:** two instances of the same executable version only; any other pair is refused with a reason.
  Activity executions are matched by executable node and invocation origin path (loop iteration, sub-workflow), not by
  id. The comparison shows the first point where the commit order differs, and per variable whether the final value
  and the write sequence differ. Values are compared by digest, so comparison works when values were not captured and
  never discloses a value the caller may not see.
- **Q7 — How does the lost-update warning reach the user as an "instance diagnostic"?**
  **Recommended:** as data, not as an incident. The instance details carry a count of lost-update warnings and of
  concurrent overwrites, and the history can be filtered to them. An incident is not used: spec 1221's implementation
  found that an incident in a commit ends the coalescing session and would reopen the inline gap. Studio shows the
  count and links to the history (elsa-studio).
- **Q8 — Who may read it?**
  **Recommended:** the existing permission for activity execution inspection reads the history, journal, trace and
  comparison metadata. Values follow the existing sensitive-value rules and payload read; nothing new is disclosed by
  the new endpoints.
- **Q9 — Are `ModifyVariable` changes in the history?** Spec 1221's implementation stamps the operator as the writer of
  the new version but leaves no write record, and an alteration is not an activity execution.
  **Recommended:** yes. The alteration's outcome records a write record (source `Alteration`) with its commit sequence;
  the read model merges it into the variable's history. Without it, an operator's correction shows as a version gap.

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

**Independent Test**: two parallel branches read `count` and write `count + 1`, and a script on a third branch writes
`count` blindly; the instance shows one lost update and the concurrent overwrites, and nothing for a sequential run.

**Acceptance Scenarios**:

1. **Given** a lost-update warning, **When** the instance details are read, **Then** the lost-update count is 1, and
   the filtered history names the variable, both writers, both branches or paths, base and current versions, and the
   overwritten value's state.
2. **Given** a blind `Set` on a third parallel branch that replaced a value another branch committed, **When** the trace
   is read, **Then** it is listed as a concurrent overwrite, not as a lost update.
3. **Given** the same workflow with the branches in sequence, or joined before a later write, **When** the trace is
   read, **Then** it is empty.
4. **Given** an invocation that writes X on suspension and again on completion, **When** the trace is read, **Then** it
   does not list the second write as an overwrite of itself.
5. **Given** a `Fault` lost update, **When** the history is read, **Then** the refused write is listed with outcome
   fault and its refused value's state, and the current value is unchanged.

---

### User Story 3 — Read an instance's commit-ordered journal (Priority: P2)

The engineer reads the journal of an instance: per commit, which activity executions started, suspended, resumed,
completed or faulted, on which branch or path, and which variable writes the commit applied.

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
   diverging commit is shown with the activity executions on each side, matched by node and origin path.
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

### Edge Cases

- Instance deleted: its history, journal and trace go with it; comparison with it is refused as not found.
- Closed-scope (graph-row) variables are not versioned yet in spec 1221's implementation: their writes are listed as
  blind writes with no version; the gap is shown, not hidden.
- A legacy instance (no write records): history is empty with "no history recorded".
- Join behaviour is being reworked in a separate initiative; this spec records only which arrivals satisfied a join,
  which holds whatever the join rule becomes.
- A retried invocation: its attempts share one logical invocation (ADR 0092 decision 8, amended); writes of different
  attempts of one logical invocation are not concurrent overwrites of each other.
- A re-executed invocation is a new logical invocation; it can overwrite its predecessor.
- Expired values: shown `expired`; operational data (overwritten value, refused write) stays while recovery needs it.
- A comparison where one instance is still running compares what is committed and says so.

## Requirements *(mandatory)*

### Functional Requirements

**Order**

- **FR-001**: Every write record and every activity lifecycle transition MUST carry the instance commit sequence of the
  logical checkpoint that committed it (Q1). Records without it (older records) MUST read back as absent and be ordered
  by frame revision and time, marked as approximate.

**Write history (#3290)**

- **FR-002**: The API MUST list an instance's variables with scope, key, current version, last writer, last commit
  sequence and value state.
- **FR-003**: The API MUST list the write history of an instance, filterable by variable, by source and by outcome
  (applied, lost-update warning, lost-update fault, concurrent overwrite), ordered by commit sequence and paged.
- **FR-004**: Each entry MUST show the variable (scope and key), the writer (node, logical invocation, attempt), the
  source (capture, intrinsic, session change, alteration, background result), base, current and new versions, the
  commit sequence, the outcome, and the value state; the entry that produced the current value MUST be marked.
- **FR-005**: `ModifyVariable` changes MUST appear in the history with source `Alteration` (Q9).
- **FR-006**: Values MUST be shown only through value states and the existing authorized payload read; omitted,
  redacted and expired MUST be distinct from a captured `null` (ADR 0092 decision 13).

**Overwrites and lost updates (#3292)**

- **FR-007**: The read model MUST mark every concurrent overwrite as defined in Q3, for every write source, derived when
  read from scheduling provenance. Provenance MUST record the arrivals that satisfied a join, so that writes after a
  join are ordered after the joined branches.
- **FR-008**: A lost-update entry MUST name both writers and both branches or paths, base and current versions, and the
  state of the overwritten value (or refused value under `Fault`).
- **FR-009**: The instance details MUST carry the count of lost-update warnings and of concurrent overwrites (Q7).

**Journal and comparison (#3291)**

- **FR-010**: The API MUST return an instance's journal in commit sequence order, paged, with each commit's activity
  transitions (started, suspended, resumed, completed, faulted), branch or path, and applied writes.
- **FR-011**: The API MUST compare two instances of the same executable version: activity executions matched by node
  and invocation origin path; the first diverging commit; per variable whether the final digest and the write sequence
  differ. Any other pair MUST be refused with a reason.
- **FR-012**: Comparison MUST NOT disclose values; it compares digests.

**Access and cost**

- **FR-013**: All new reads MUST use the existing activity-execution inspection permission; values MUST follow the
  existing sensitive-value rules (Q8).
- **FR-014**: The run-time cost of this spec MUST be limited to stamping the commit sequence (FR-001), the alteration
  record (FR-005) and the join arrivals (FR-007); everything else is computed when read.

### Key Entities

- **Variable summary**: one variable of an instance with its current version, last writer, last commit sequence and
  value state.
- **Write history entry**: a view of one spec 1221 write record (or alteration record) with its commit sequence,
  outcome, overwrite marking and value state.
- **Journal commit**: one commit sequence number with its activity transitions and applied writes.
- **Instance comparison**: the matched activity executions, the first diverging commit and the per-variable
  differences of two instances of one version.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For every write in the test suite, the history reconstructs who wrote each variable, in which order, and
  which write is the current value (100%), including `0 → 1 → 0` within one buffered segment.
- **SC-002**: Every lost update and every concurrent overwrite in the reproduction suite is shown (0 missed), and a
  sequential run shows none (0 false traces).
- **SC-003**: An engineer finds why two instances of one version ended differently from the comparison alone, without
  logs or a debugger, in the reproduction scenario.
- **SC-004**: The history and journal of an instance with 10,000 activity executions return their first page within
  the existing activity-list read time on the same store, measured in the plan.
- **SC-005**: No value is shown to a caller who cannot see it through the existing inspection reads (0 disclosures in
  the access tests).
- **SC-006**: Run-time overhead is limited to the stamps of FR-014, measured against spec 1221's benchmark.

## Assumptions

- Spec 1221's write record (variable, writer, source, versions, outcome, overwritten writer and value, refused value)
  is the input; this spec reads it and adds only the commit sequence (if spec 1221 does not) and the alteration record.
- Instances are short enough per page that merging activity rows by commit sequence is acceptable; a projection
  follows only on measurement (Q2).
- Studio presentation is specified in elsa-studio against this API.
- Write history physical storage is spec 1221's Open question B; this spec works with the measured choice (records on
  the writer's activity execution state) and does not depend on it beyond the query in Q2.
