# Review 5 delivery: EV-1, FR-2, JR-1 and PO-1

Executable schedules for four Majors of the fifth Astra High review of the kernel proposal
(elsa-workflows/elsa-foundation#4229, review comment 6973554094), reviewed at `0a1bb5e0c`:

- **EV-1 (R5-M2):** event eligibility across a recovery Open, on the real in-memory publisher path;
- **FR-2 (R5-M3):** the drain's outstanding-work credit across several full pages;
- **JR-1 (R5-M4):** a job result that completes while its instance is parked, across takeover and recover;
- **PO-1 (R5-M6):** an exhausted job ticket against a cohort suspect in the poison comparison.

Each case runs one schedule under three rule sets:

- `reviewed`: the rules of the reviewed design (`0a1bb5e0c`);
- `repaired`: the fifth round's repair rules, as the coordinator's design brief and its 18 November EV-1 decision state them;
- `negative`: the repaired rules with the one repaired rule removed.

Mutations of the repaired set run where they bite: `match-at-now`, `fresh-event-id` and `owed-from-at-commit` (EV-1),
`pages-while-suspended` and `held-gate-waits` (FR-2). JR-1 has a second reading of the reviewed design,
`reviewed-kind`. A run ends PASS, FAIL or STOPPED. STOPPED means the stated repaired rules give no single expected
outcome for that schedule: the run prints its trace and the reason, and the recommendation is below.

The machinery (the report, the Native host, the reflection helpers, the row and drain fixtures, `HeldGate`) is copied
from [Review4.Blockers](../Review4.Blockers/README.md), which is unchanged so it stays reproducible at its recorded
commit. The Background test activity follows [Astra4.Sizes](../Astra4.Sizes/README.md).

## Run

From `prototypes/kernel-model`:

```
dotnet restore experiments/Review5.Delivery --locked-mode
dotnet build experiments/Review5.Delivery -c Release --no-restore
BIN=experiments/Review5.Delivery/bin/Release/net10.0/Kernel.Review5.Delivery.dll
dotnet $BIN --list                                   # every case, rule set and expected outcome
dotnet $BIN --case ev1-b --rules repaired            # one run: exit 0 PASS, 1 FAIL, 3 STOPPED
dotnet $BIN --all                                    # every run, then a summary; exit 0 only if all match the expected matrix
experiments/Review5.Delivery/run.sh                  # every run one by one, then --all; exits with the matrix result (2 if setup fails)
```

[results.txt](results.txt) holds the output of `--all` and the commit it ran at.

## The run table

| Run | Finding and trace | Expected |
|---|---|---|
| `ev1-a` | R5-M2 trace A, restore more than ε after the event's acceptance, both publisher delays | reviewed FAIL, repaired PASS, negative FAIL |
| `ev1-a-band` | R5-M2 trace A inside the ε band (the reviewer's witness): either outcome permitted, never two deliveries | PASS under all three |
| `ev1-a-after` | Control: invariant 13, an event accepted after the wait committed, both publisher delays | PASS under all three |
| `ev1-b` | R5-M2 trace B: keeping wait restored at 99, event kept through 100, catch-up at 99 and at 101 after a sweep | reviewed FAIL, repaired PASS, negative FAIL, match-at-now FAIL |
| `ev1-b-paged` | SB-2's swept later page: two events, a page of one, sweeps before and between pages | reviewed FAIL, repaired PASS, negative FAIL, match-at-now FAIL, owed-from-at-commit FAIL |
| `ev1-b-band` | A keeping wait owed an event only by rule (a), with a sweep before its catch-up | reviewed FAIL, repaired STOPPED, negative FAIL |
| `ev1-replay` | SB-2's replayed publish ticket: no duplicate, and the event not swept while the ticket is outstanding | reviewed FAIL, repaired PASS, negative FAIL, fresh-event-id FAIL |
| `ev1-replay-consumed` | As `ev1-replay`, with both waits' deliveries consumed and their done rows swept before the replay | reviewed FAIL, repaired STOPPED, negative FAIL |
| `fr2-base` | R5-M3: three full pages, the drain's controls winning priority before any model handoff | reviewed FAIL, repaired PASS, negative FAIL |
| `fr2-hold` | R5-M3 with a Hold: five restored rows held by the model, one of them page 1's gate row | reviewed FAIL, repaired PASS, negative FAIL, held-gate-waits FAIL |
| `fr2-suspend` | R5-M3 with a suspension after page 1 and a resume six passes later | reviewed FAIL, repaired PASS, negative FAIL, pages-while-suspended FAIL |
| `fr2-takeover` | R5-M3 with a crash takeover between pages 2 and 3 | reviewed FAIL, repaired PASS, negative FAIL |
| `fr2-hold-takeover` | R5-M3 with a Hold and a crash takeover that loses the holds; the held rows return as suspects and are tried | reviewed FAIL, repaired PASS, negative FAIL |
| `jr1-crash-after` | R5-M4: J completes while S is parked; crash after the conversion commit; takeover; recover | reviewed FAIL, reviewed-kind FAIL, repaired PASS, negative FAIL |
| `jr1-crash-after-keep` | R5-M4: as above, crash after the parked instance kept the result | reviewed FAIL, reviewed-kind FAIL, repaired PASS, negative FAIL |
| `jr1-crash-before` | R5-M4: crash before the conversion commit, so J runs a second time (the at-least-once allowance) | PASS under all four |
| `jr1-running` | Control: J completes while S runs, the built in-memory path | PASS under all four |
| `po1-cohort` | R5-M6: exhausted J at 900, cohort suspect R at 1100, the mark set after Complete, a cold takeover with no control | reviewed FAIL, repaired PASS, negative FAIL |
| `po1-open` | R5-M6: as above with the drain still open and its last control pending | reviewed FAIL, repaired PASS, negative FAIL |
| `po1-ordinary` | R5-M6: the ordinary earlier-poison case with no drain and no mark; the same rows also run on a real `Worker` | PASS under all three |

In FR-2 and PO-1 the negative rule set coincides with the reviewed one: in each, the repair changes one rule, and the
negative restores it. The mutations are the controls that separate the repaired set's other parts.

## EV-1 (R5-M2): event eligibility by stored stamps

**The rules run.** The coordinator decided on 18 November (coordinator design choice): ε is the configured lease, 10 s by
default (`Defaults.Lease`, `src/Kernel.Runtime/PartitionRuntime.cs` 7). Both stamps are database time at commit through
the qualified commit clock, which only the in-memory store has today (`Store.cs` 137–141, `CommitClock.cs` 44–45,
`InMemoryStore.cs` 94 and 143). The harness takes every stamp from the real `CommitAsync`'s `Committed.Now`.

- Any wait is owed E when (a) `E.AcceptedAt ≥ S.RegisteredAt − ε`. A keeping wait is also owed E when (b)
  `E.KeepUntil ≥ S.RegisteredAt`. The publisher's match and the meet step both apply this.
- Each meet and publish ticket carries `OwedFrom`, stamped at commit. The sweep deletes an event only when its
  `KeepUntil` has passed and lies below the least `OwedFrom` of the outstanding meet and publish tickets.

**The path.** The publisher's side is `Events.PublishAsync` (`Store.cs` 287–295), split at the reviewer's pause point:
`PublishAsync` (`InMemoryStore.cs` 263–267), then `PublishedMatchesAsync` (281–285), then `AddEventAsync` (235–246). The
stamp check sits between the lookup and the insert. The subscriber's side is the meet step (`Worker.cs` 1326–1329), paged
as proposed: under `reviewed` its read is the real `KeptMatchesAsync` (287–298) at the step's clock; under `repaired` it
reads the same store rows by rule (a) or (b). Turn commits, the restore's Open and ticket consumption are real fenced
`CommitAsync` calls. The sweep is the real `SweepAsync` (348–359) under `reviewed`; under `repaired` the harness applies
its two deletions (353–356) with the owed-from mark. `P_page` is scaled from 170 to 1, so two events make two pages.

**The traces.**

- `ev1-a`: E is accepted at 10 and persisted while S's wait is absent. The publisher pauses before its lookup; S's
  non-keeping wait is restored at 10,011, 10,001 ms after acceptance; the publisher resumes. Both orders run. Reviewed:
  0 deliveries when the lookup runs first, 1 when the restore runs first. Repaired: 0 in both orders.
- `ev1-a-band`: the same with the restore 10 ms after acceptance, inside the band, as in the reviewer's witness. Both
  outcomes are permitted there. Every rule set gives 0 and 1 in the two orders, and never 2.
- `ev1-a-after`: the wait commits at 10, E is accepted at 20, and the lookup runs at once or 10,001 ms later: 1 delivery
  in both orders.
- `ev1-b`: E is accepted at 1, kept through 100, published wholly during Park; a keeping wait is restored at 99 with its
  meet ticket. Reviewed: the catch-up at 99 delivers 1, and at 101, after the real sweep, 0. Repaired: 1 in both,
  because the meet ticket's `OwedFrom` 99 holds E (100 is not below 99), and rule (b) owes it. A sweep at 102, with no
  ticket outstanding, deletes E in every run, so the mark is no lasting hold.
- `ev1-b-paged`: two events kept through 100. Control: both pages at 99. Then: a sweep at 101, page 1, another sweep,
  page 2. Repaired delivers 2 in both. The second page's ticket carries its predecessor's `OwedFrom`; see the
  interpretation below.
- `ev1-replay`: W1 (non-keeping, registered at 5) and W2 (keeping, restored at 10,020) are owed E (accepted at 10, kept
  through 20,000). Owner 1 matches W1 and dies before consuming the publish ticket. Owner 2 takes over after the lease,
  restores W2, runs its catch-up, pauses past E's keep window with the ticket outstanding, sweeps at 20,001 and replays
  the ticket. Reviewed: the real sweep deletes E. The replay re-inserts it through `PublishAsync`'s `TryAdd` and adds no
  delivery, because both delivery rows are still there. Repaired: the publish ticket's `OwedFrom` 10 keeps E, and the
  replay adds no delivery.
- `ev1-b-band` and `ev1-replay-consumed` stop under `repaired`; see the findings.

**Mutations.** `match-at-now` keeps the mark but reads through the real `KeptMatchesAsync`: at 101 it finds nothing.
`owed-from-at-commit` stamps page 2's ticket at its own commit (101), so the sweep between pages deletes E2.
`fresh-event-id` gives the replay a new event id, and both waits get a second delivery.

## FR-2 (R5-M3): the drain's outstanding-work credit

**The schedule.** S runs `Parallel(H, Event X 'never')` on the real `NativeModel`, with H a `HeldGate` over `Event go`.
Park kept three full pages of rows (510 rows at seq 1000–1509), no-op `native.resume` rows of 1,250 bytes. In the hold
variants, five of page 1's rows are composite signals to H, the last of them page 1's last row, so page 1's gate row
is one the model holds. `A = native.resume go` arrives during the drain and is withheld until Complete. A neighbour
instance N of the same partition gets one input every pass.

One owner pass: the built poll (`InMemoryStore.cs` 219–221, limit 500) and the flush's marks (`Worker.cs` 1397), at
most one suspect trial (`Worker.cs` 733–763, TrialGroup 1, cohort suspects first), one turn of S, one turn of N. During
the drain S's control kinds come first, then its released rows (R4-B1). A model turn takes the built batch: every
offered row up to the first control, behind no untried suspect (`Worker.cs` 845–877). A waiting page sends its
`kernel.drain` with a 3 ms delay. Both rule sets wait while the instance is suspended.

- `reviewed` and `negative` gate a page on the Seen test: the previous page's last row is done, held, or seen at this
  epoch.
- `repaired` gates it on handoff: that row is consumed by a committed model turn or held. The owner must also have at
  most one page (`P_page` rows, or `M × P_page` = 4,177,920 bytes) of restored rows not handed off. A takeover resets the
  handoffs: the new owner knows only durable state. A trial counts once its turn commits.

**What the runs show.**

- Reviewed: pages 2 and 3 run before the model receives a row, and 510 rows are pending at the first handoff.
  - `fr2-suspend`: page 2 runs before the suspend applies, and page 3 runs right after the resume.
  - `fr2-takeover`: the new owner's re-poll marks page 2's gate row at its epoch, page 3 runs, and 509 rows are pending.
- Repaired: pending non-held restored rows peak at 170, and their bytes at one page's (212,840 bytes).
  - `fr2-hold-takeover`: the bound is 175. The five rows held before the loss return as suspects, are tried one at a
    time, and are held again before page 3.

Every run asserts:

- the peak pending non-held restored rows and bytes;
- the held-row gauge (5 in the hold variants);
- that the drain completed;
- that all 510 restored rows reached model turns;
- in the hold variants, that `HeldGate` accepted a signal once A arrived;
- that N had a turn in every pass it had input (its turn share);
- in `fr2-suspend`, that S had no model turn while suspended;
- that S still runs.

**Mutations.**

- `pages-while-suspended` lets a page run while S is suspended, counting the withheld rows as handed off. Pages 2 and
  3 run during the suspension, and 510 rows are pending at resume.
- `held-gate-waits` counts a held gate row as not handed off. Page 1's held gate row waits for A, which only Complete
  releases, and the drain stalls.

## JR-1 (R5-M4): a fenced, authenticated job result

**The schedule.** S runs `Sequence(Set svc alpha, Parallel(c, Set svc beta))` on the real `NativeModel`, with `c` a
Background node bound to `svc` (as in Astra4.Sizes). Its real start turn arms the job (`NativeModel.cs` 347–355). The
ticket `c9_0/subject_1!2_w6` commits with the row through the real `CommitAsync`. The job body is the real
`NativeModel.ExecuteJobAsync` (186–197) running `EffectEcho`, which counts physical invocations and returns
`alpha|<effect key>`.

While J runs, an input parks S. The Park follows the built `WorkflowInstance.Park` (120–132) and keeps the job
registry; Park keeping and `KeptBy` are proposed. J's body then returns, with S still parked. After that come a crash
takeover (the real `TakeAsync` from another node after the lease), a recover and the owner's poll. A parked instance
consumes late input with no model turn (`WorkflowInstance.cs` 78–90), and Park keeps it `Arrived`. The built check
for a supplied completion is the real `Delivery.DiscardedAt` (`Turns.cs` 109–112) over a real `Slot`.

- `reviewed`: the completion stays in memory (`CompletedJob`, `Worker.cs` 1297–1306). The parked instance consumes it,
  and the kept row is the ticket itself. At recover the restored `kernel.job` row is a ticket again, and the built
  `Hold` and `StartJob` enter J a second time after its result had committed: 2 physical invocations where 1 is allowed.
- `reviewed-kind`: the kept row also persists the result kind (`job-finished` under the ticket's key). After the
  takeover the real `DiscardedAt` classifies the restored row as a supplied completion, and the result is lost: S
  never finishes.
- `repaired`: a standalone fenced commit consumes the ticket (counted) and inserts the result row
  `…!result!g1` under the reserved scope 2^62, its id derived from the ticket's full key and the generation. The new
  owner accepts it by that identity and the registry's current generation; the built `DiscardedAt` alone refuses it,
  and the report prints that. Park keeps it `Arrived`, and recover restores it. One model turn resumes `c` with
  `alpha|c9_0/subject_1/c/-/0`, the arm-time input and the original effect key. J runs once.
- `negative`: the same conversion with an ordinary-scope result row. The new owner's real `DiscardedAt` refuses it and
  the result is lost.
- `jr1-crash-before`: the owner dies after J's body returned and before any commit recorded the result. The ticket is
  still undone. The new owner does not enter it for a parked row (`Worker.cs` 1169–1177), the parked instance keeps it,
  and recover restores it. J runs a second time, the one re-run the at-least-once allowance permits, and its one result
  reaches the model under every rule set.
- `jr1-running`: J completes while S runs, and the built path (`Worker.cs` 1297–1306) delivers it once.

Every run asserts:

- one model result;
- its value;
- the physical invocation count against the allowance (2 for a crash before the conversion commit, else 1);
- that S finished;
- that no undone row of S is left.

## PO-1 (R5-M6): precedence in the poison comparison

**The history, on the real store.** S runs `Parallel(Delay F 50, Delay F2 100000)`: its real start turn arms F's timer
R (due 1050). J, a job ticket that hangs, is at seq 900 and R at seq 1100. Owners 1, 2 and 3 each put J on trial (the
real `CommitAsync` trial mark) and die. Each next take is the real `TakeAsync`, which adds an attempt to the rows on
trial (`InMemoryStore.cs` 70–82). So owner 3 is the last failed owner, and J reaches `MaxAttempts` (3). Owner 4 marks J
and R seen.

- `po1-cohort`: R was released by the drain's final page and J by an early page, and Complete set the cohort mark.
- `po1-open`: the drain is still open, with its last control C at seq 950.
- In both, a new process takes over before either input is dispatched: a cold takeover with no memory and, in
  `po1-cohort`, no control.

**The mirrored owner.** It reads through the real `PollAsync`. Its rules mirror the built Worker for one instance:

- J is a poison input (`Worker.cs` 705–707) and R a suspect (577–578);
- `NextTrial` (733–763) skips a suspect that does not precede the due poison (`PrecedesPoison`, 714–716);
- ordinary delivery parks S with the exhausted J (`RunTurn` 1064–1065, `WorkflowInstance.Park` 120–132);
- FR-1's cohort rules apply on top: cohort suspects first, the mark withholding non-cohort rows, controls first while the
  drain is open.

The outcomes:

- `reviewed`: `PrecedesPoison` rejects R's trial because J sorts first.
  - `po1-cohort`: the mark withholds J while R is pending, and nothing moves until the step bound (stall).
  - `po1-open`: C waits for its untried gate row R, J (a released row) is offered and parks S, and R then reaches the
    parked instance as late input (wrong order; F never completes).
- `repaired`: in the cohort context a non-cohort poison input never precedes a cohort suspect. R is tried first, and its
  real timer turn completes F. The mark clears (`po1-cohort`) or C completes (`po1-open`), and then J parks S, its fault
  naming J.
- `po1-ordinary`: there is no drain and no mark. The built comparison holds under every rule set: J parks S first, and R
  then reaches the parked instance. The same history is handed to a real `Worker` (`WorkflowInstance` over the real
  `NativeModel`), pumped until idle. It parks S with the fault "message …!4_hang was on trial when 3 owners were lost".
  The run asserts that the mirrored owner's outcome (status, fault, F's completion, undone rows) equals the real
  Worker's.

## What this proves, and what it does not

**Executed in this repair** (harness runs only):

- the EV-1 stamp check, owed rules (a) and (b), and owed-from sweep mark;
- the FR-2 handoff credit, with a Hold, a suspension, a crash takeover and trials;
- the JR-1 fenced conversion and its authentication;
- the PO-1 precedence in the poison comparison.

Each runs under its reviewed, repaired and negative sets and the mutations above.

**Real prototype code these runs go through** (each report lists its calls):

- `InMemoryStore` `CommitAsync` (fenced, count-checked), `TakeAsync`, `PollAsync`, `PublishAsync`,
  `PublishedMatchesAsync`, `KeptMatchesAsync`, `AddEventAsync`, `SweepAsync`, `AddAsync`;
- `EventRoute.Delivery`;
- `NativeModel.TurnAsync` with its real timer, resume, composite-signal hold and job-result handlers;
- `NativeModel.ExecuteJobAsync`;
- `Delivery.DiscardedAt` over a real `Slot`;
- `InboxKey.Ticket` and `CreationProtocol.Origin`;
- in `po1-ordinary`, a real `Worker` with `WorkflowInstance`.

**Existing prototype behaviour** these runs exhibit:

- the publisher's lookup reads the subscriptions present at the lookup (`InMemoryStore.cs` 281–285);
- the kept-event read filters by the read's clock (287–298);
- the sweep deletes an event once its `KeepUntil` has passed (354);
- a consumed delivery with a `KeepUntil` stays a done row until then (`Worker.cs` 1452–1453), and the sweep deletes it
  (`InMemoryStore.cs` 353);
- `AddEventAsync` deduplicates only against present rows (240–241);
- the in-memory `CompletedJob` conversion keeps the ticket's identity (`Worker.cs` 1247–1252, 1297–1306);
- a durable `job-finished` row is a supplied completion (`Turns.cs` 109–112);
- the poison comparison is plain `seq` order (`Worker.cs` 714–716);
- an exhausted ticket parks its instance (1064–1065).

**Mirrored from the built code** (each needs a real Worker's private state):

- the Worker's poll handling, marks, suspect test, trials and batching (FR-2, PO-1);
- the faulted instance's late-input path and `WorkflowInstance.Park` (JR-1, PO-1);
- the built suspend and resume (FR-2).

**Proposed and modelled in the harness:** `AcceptedAt`, `RegisteredAt`, `Subscriptions.Keep`, `Messages.OwedFrom`, the
paged meet ticket, the drain's pages, gate and handoff credit, `ReleasedBy`, the cohort mark, Park's `KeptBy`, the
recover page, `JobGeneration`, and the reserved result scope.

**Left for qualification at Kn:**

- the Worker implementing these rules, with its commit batching and fence (K3);
- providers that stamp the commit clock, the owed-from index and the sweep's statement, a provider's own replay and
  duplicate semantics (K4);
- real model handoff and holds under load, the drain's byte credit against real payloads (K6, K12).

## Findings for the coordinator

1. **`ev1-b-band` (STOPPED): rule (a) and the sweep mark disagree.** E is published with no keep window (accepted at 95,
   `KeepUntil` 95), and a keeping wait is restored at 99, inside ε. Rule (a) owes E to the wait. The sweep at 101 deletes
   E, since 95 has passed and lies below the mark 99, and the catch-up then finds nothing. The outcome is 1 at 99 and 0
   at 101: the R5-M2 defect, inside the band. Recommendation: the sweep also holds an event while
   `AcceptedAt ≥ mark − ε`, deleting only when `KeepUntil < mark` and `AcceptedAt < mark − ε`. Alternatively, rule (a)
   applies to non-keeping waits only.
2. **`ev1-replay-consumed` (STOPPED): the mark holds events and leaves their delivery rows.** Both waits consumed their
   deliveries, which stay done rows until E's `KeepUntil` (`Worker.cs` 1452–1453). The sweep at 20,001 deletes those rows
   (`InMemoryStore.cs` 353), while the publish ticket's `OwedFrom` keeps E. The replay's lookup still owes E to both
   waits, `AddEventAsync` finds no duplicate (240–241), and both waits get E twice. The reviewed design does the same.
   Recommendation: the mark also holds the done delivery rows of an event it holds. Alternatively, the replay records
   which waits the first attempt reached.
3. **A later meet page's `OwedFrom` (harness interpretation).** The rule stamps `OwedFrom` at commit. The commit that
   advances the cursor writes the next page's ticket; stamped at its own commit (101), it lets the sweep between pages
   delete an owed event (`owed-from-at-commit` fails). The repaired runs carry the predecessor's `OwedFrom`, the owed-from
   time of the registration. Please confirm or restate.
4. **FR-2's byte credit is metadata-sized.** `M × P_page` = 4,177,920 bytes bounds a page's metadata. A restored row in
   the owner's memory carries its payload, up to the row cap, so the byte credit as stated does not bound memory. In
   every executed schedule the gate's handoff clause already implies the one-page clause, because restored rows are
   handed off in `seq` order. The runs assert the observable bound of one page's encoded bytes.
5. **JR-1, a third reading of the reviewed design, not run.** The built `RemainingJobs` drops J's key from the registry
   when the parked instance consumes the in-memory completion (`WorkflowInstance.cs` 78–90, 135–144). The proposed Open
   then clears a kept job row whose key the registry no longer holds (observability.md, R4-M3). Under that reading the
   result is lost at recover. The runs keep the key, as the repair needs for its generation check.
6. **JR-1 needs a Worker change.** The built `Delivery.SuppliedCompletion` (`Turns.cs` 109) refuses every durable
   `job-finished` row, the repaired reserved-scope row included. The proposed authentication (reserved scope, the derived
   id, the registry's current generation) must replace that test for reserved-scope rows. K3 change row.
7. **The coordinator's ε decision is applied as stated:** ε = the configured lease, 10 s. Trace A's never-delivered
   outcome is asserted beyond ε. Inside the band only the permitted outcomes are asserted.
