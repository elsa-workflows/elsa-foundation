# Review 5 delivery: EV-1, FR-2, JR-1 and PO-1

Executable schedules for four Majors of the fifth Astra High review of the kernel proposal
(elsa-workflows/elsa-foundation#4229, review comment 6973554094), reviewed at `0a1bb5e0c`:

- **EV-1 (R5-M2):** event eligibility across a recovery Open, on the real in-memory publisher path;
- **FR-2 (R5-M3):** the drain's outstanding-work credit, plus the two R4-B1 traces that passed the fifth review,
  re-staged under the new gate;
- **JR-1 (R5-M4):** a job result that completes while its instance is parked, across takeover and recover;
- **PO-1 (R5-M6):** an exhausted job ticket against a cohort suspect in the poison comparison.

Each case runs one schedule under three rule sets:

- `reviewed`: the rules of the reviewed design (`0a1bb5e0c`);
- `repaired`: the fifth round's repair, as the coordinator's design brief states it, with the coordinator's later
  statements for EV-1 (18 November, the match-step rule, which withdraws the earlier ε rule) and FR-2 (the final
  credit rule);
- `negative`: the repaired rules with the one repaired rule removed.

Mutations of the repaired set run where they bite. JR-1 has a second reading of the reviewed design, `reviewed-kind`.

A run ends PASS, FAIL or STOPPED. STOPPED would mean the stated repaired rules give no single expected outcome for that
schedule. No run stops at this commit: the coordinator decided the three that stopped in the first version of this PR
(see "Decisions applied").

The machinery is copied from [Review4.Blockers](../Review4.Blockers/README.md), which is unchanged so it stays
reproducible at its recorded commit:

- the report and the Native host;
- the row and drain fixtures;
- `HeldGate`.

The Background test activity follows [Astra4.Sizes](../Astra4.Sizes/README.md).

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
| `ev1-a` | R5-M2 trace A: E accepted while the wait is absent, three positions of its match step against the restore, and a second processing of the ticket | reviewed FAIL, repaired PASS, match-at-acceptance FAIL, split-no-row-hold FAIL |
| `ev1-a-after` | Control, invariant 13: E accepted after the wait committed, its match step at once or 30 s later | PASS under both |
| `ev1-b` | R5-M2 trace B: keeping wait restored at 99, E kept through 100, catch-up at 99 and at 101 after a sweep | reviewed FAIL, repaired PASS, negative FAIL, match-at-now FAIL |
| `ev1-b-paged` | SB-2's swept later page: two events, a page of one, sweeps before and between pages | reviewed FAIL, repaired PASS, negative FAIL, p0-at-page-commit FAIL |
| `ev1-b-stepback` | The clock steps back below a swept event's KeepUntil, a live E2 is published, then a keeping wait registers | reviewed FAIL, repaired PASS, no-horizon FAIL |
| `ev1-b-suspended` | A suspended publisher's publish ticket and a suspended instance's meet ticket complete their steps, so their holds on E end | reviewed PASS, repaired PASS, meet-waits-while-suspended FAIL |
| `ev1-replay` | SB-2's replayed publish ticket: no duplicate, E not swept while the ticket is outstanding | reviewed FAIL, repaired PASS, negative FAIL |
| `ev1-replay-atomic` | The match step's atomicity: a step that died inserted nothing, so the replay after the sweep delivers once | reviewed FAIL, repaired PASS, negative PASS, split-no-row-hold FAIL |
| `ev1-replay-consumed` | As `ev1-replay`, with both waits' deliveries consumed before the sweep | reviewed FAIL, repaired PASS, negative FAIL |
| `fr2-base` | R5-M3: three full pages, the drain's controls winning priority before any model handoff | reviewed FAIL, repaired PASS, negative FAIL, seen-gate PASS |
| `fr2-hold` | With a Hold: five restored rows held by the model, one of them page 1's gate row | reviewed FAIL, repaired PASS, negative FAIL, seen-gate PASS, held-gate-waits FAIL |
| `fr2-suspend` | A suspension after page 1 and a resume six passes later | reviewed FAIL, repaired PASS, negative FAIL, seen-gate PASS, pages-while-suspended FAIL |
| `fr2-takeover` | A crash takeover after page 2's rows were marked and before any reached the model; 170 trials | reviewed FAIL, repaired PASS, negative FAIL, seen-gate PASS |
| `fr2-hold-takeover` | A crash takeover after page 2 was consumed that loses five holds (u = 5) | reviewed FAIL, repaired PASS, negative FAIL, seen-gate PASS, p-page-whatever-u FAIL |
| `fr2-bytes` | Ten 1 MiB rows, then one 16 MiB row | reviewed FAIL, repaired PASS, negative FAIL, seen-gate PASS, credit-metadata-only FAIL |
| `fr2-zero-cut` | A 4 MiB held row lost at a takeover cuts the next page to zero | reviewed FAIL, repaired PASS, negative FAIL, seen-gate PASS, zero-cut-completes FAIL |
| `fr2-r4b1-base` | R4-B1's base trace (Review4 `fr1-base`) under the handoff gate | reviewed PASS, repaired PASS, negative PASS, seen-gate-no-precedence FAIL |
| `fr2-r4b1-held` | R4-B1's held trace (Review4 `fr1-held-crash`): R held at Complete, then a crash takeover | reviewed PASS, repaired PASS, negative FAIL, seen-gate-no-precedence FAIL |
| `jr1-crash-after` | R5-M4: J completes while S is parked; crash after the conversion commit; takeover; recover | reviewed FAIL, reviewed-kind FAIL, reviewed-keeps-registry FAIL, repaired PASS, negative FAIL |
| `jr1-crash-after-keep` | As above, crash after the parked instance kept the result | reviewed FAIL, reviewed-kind FAIL, repaired PASS, negative FAIL |
| `jr1-obsolete` | As `jr1-crash-after-keep`, with a kept result row of J's obsolete generation g0 | reviewed FAIL, reviewed-kind FAIL, repaired PASS, negative FAIL, generation-blind FAIL |
| `jr1-crash-before` | Crash before the conversion commit, while S is parked: recover restarts J under the next generation | reviewed FAIL, reviewed-kind FAIL, repaired PASS, negative FAIL |
| `jr1-running` | Control: J completes while S runs, the built in-memory path | PASS under all four |
| `po1-cohort` | R5-M6: exhausted J at 900, cohort suspect R at 1100, the mark set after Complete, a cold takeover with no control | reviewed FAIL, repaired PASS, negative FAIL |
| `po1-open` | As above with the drain still open and its last control pending | reviewed FAIL, repaired PASS, negative FAIL |
| `po1-ordinary` | The ordinary earlier-poison case with no drain and no mark; the same rows also run on a real `Worker` | PASS under all three |

In FR-2 and PO-1 the negative rule set coincides with the reviewed one. In each, the repair changes one rule, and the
negative restores it. The mutations separate the repaired set's other parts.

## EV-1 (R5-M2): event eligibility by the match step

**The rule run as `repaired`.** This is the coordinator's statement of 18 November with its two later decisions; the ε
rule it withdraws is not run.

1. **The match step.** The match step is the processing of a publish ticket. Its one commit consumes the ticket and
   inserts the deliveries. A non-keeping wait receives exactly the events whose match step reads its committed
   subscription row. It never receives an event through a catch-up.
2. **The keeping entitlement.**
   - A keeping wait's entitlement point is P0 = max(RegisteredAt, H + 1 ms). H is the sweep's durable, monotone horizon:
     the greatest KeepUntil deleted. So the entitlement KeepUntil ≥ P0 excludes every swept event. It also excludes an
     unswept event whose KeepUntil equals H, which has already expired.
   - P0 is read at the restore and recorded on the meet ticket.
   - The catch-up is owed the events of the wait's keys with KeepUntil ≥ P0. The wait also gets everything matched
     after its registration.
3. **The sweep.**
   - It deletes E only once E's KeepUntil has passed, and advances H to that KeepUntil.
   - It never deletes E while an outstanding publish ticket names E.
   - It never deletes E while an outstanding meet ticket covers E's key with P0 ≤ E's KeepUntil.
   - It keeps an event's done delivery rows while the event row exists (an indexed `Events.Match` read), so a replayed
     match step meets its duplicates.
   - Meet and publish tickets pass suspension.

**The path.**

- **The publisher's side** is `Events.PublishAsync` (`Store.cs` 287–295): `PublishAsync` (`InMemoryStore.cs`
  263–267), `PublishedMatchesAsync` (281–285), then the deliveries.
  - Reviewed: the built split. `AddEventAsync` (235–246) adds each delivery, and a later commit consumes the ticket
    (`Worker.cs` 1324–1325, 1338–1339).
  - Repaired: one fenced, count-checked `CommitAsync` consumes the ticket and inserts the deliveries, built by the real
    `EventRoute.Delivery`. The in-memory commit refuses an existing key, so the step leaves out rows already present,
    as a provider's insert-if-absent would.
- **The subscriber's side** is the meet step (`Worker.cs` 1326–1329), paged as proposed. `P_page` is scaled from 170
  to 1, so two events make two pages.
  - Reviewed: its read is the real `KeptMatchesAsync` (287–298), at the step's clock.
  - Repaired: it reads the same store rows with KeepUntil ≥ the ticket's P0. The next page's ticket carries P0.
- **The sweep** is the real `SweepAsync` (348–359) under `reviewed`. Under `repaired` it is that sweep's two deletions
  (353–356) with rule 3; a done row's event is read from the delivery's `EventMatch` (`Store.cs` 212–214, 230–239).

Every commit is the real `CommitAsync`, and every clock value is the one its `Committed.Now` reports.

**The traces.**

- `ev1-a`: E is accepted at 10 while S's wait is absent, and S's non-keeping wait is restored at 20.
  - Three orders run: the match step before the restore; the publisher paused before its match step until after the
    restore; the match step at once, then a 30 s pause before the restore.
  - In the paused order, S then consumes its delivery, a sweep at 30 deletes E and the done row, and a stale dispatch
    processes the same ticket again. Repaired: the ticket is gone, so the count-checked `CommitAsync` refuses the second
    step and nothing lands. Reviewed (the split step) adds the delivery before its commit fails, so S gets E twice.
  - Repaired delivers 0, 1 and 0; reviewed 0, 2 and 0.
  - The reviewed oracle (observability.md 1187–1189: a wait that did not ask for kept events misses them) expects
    0, 0, 0, so it fails in the second order.
  - The repaired oracle is the match-step rule. It expects 0, 1 (once), 0. `match-at-acceptance`, a match step that reads
    the subscriptions committed at the event's acceptance, gives 0, 0, 0; `split-no-row-hold` gives 0, 2, 0.
- `ev1-a-after`: the wait commits at 10, E is accepted at 20, and the match step runs at once or 30 s later. Both
  deliver 1.
- `ev1-b`: E is accepted at 1, kept through 100, and published wholly during Park. A keeping wait is restored at 99.
  - Reviewed: the catch-up at 99 delivers 1. At 101, after the real sweep, it delivers 0.
  - Repaired: 1 in both. The meet ticket (P0 99 ≤ 100) covers E against the sweep, and the catch-up reads
    KeepUntil ≥ P0.
  - A sweep at 102, with no ticket outstanding, deletes E in every run.
- `ev1-b-paged`: two events kept through 100. The control runs both pages at 99. The other schedule runs a sweep at
  101, page 1, another sweep, then page 2. Repaired delivers 2 in both.
- `ev1-b-stepback`: E1 (KeepUntil 100) is swept at 101 with no ticket covering it, so H = 100. The clock steps back to
  98, and E2 is published with KeepUntil 99, before the wait registers. A keeping wait then registers at 99:
  P0 = max(99, 100 + 1) = 101.
  - Repaired: the wait is not entitled to the swept E1 (100 < 101), and its catch-up delivers nothing. E2 is live and
    excluded too (99 < 101), the cost of a clock step-back (see "Notes").
  - `no-horizon` (P0 = 99) delivers E2 and is entitled to the swept E1. Reviewed (`KeptMatchesAsync` at 99) also
    delivers E2.
- `ev1-b-suspended`: the publisher is suspended with its publish ticket for E (kept through 100) outstanding, and S is
  suspended when its keeping wait is restored at 99.
  - The match step and the meet step consult the suspension. Under the rule they pass it, as built (`Worker.cs` 640–645
    and 1315–1339 take meet and publish tickets with no withholding check). Both steps complete, and the sweep at 102
    deletes E while both instances are still suspended.
  - `meet-waits-while-suspended` makes both tickets wait. E is still held at 102, and both tickets complete only at the
    resume (20,000).
- `ev1-replay`: W1 (non-keeping, registered at 5) and W2 (keeping, restored at 10,020) are owed E (accepted at 10, kept
  through 20,000).
  - Owner 1 dies in E's match step. The repaired commit does not land. The reviewed split has inserted W1's delivery
    and left the ticket undone.
  - Owner 2 takes over after the lease, restores W2 and runs its catch-up. It pauses past E's keep window with the
    ticket outstanding, sweeps at 20,001 and replays the ticket.
  - Reviewed: the real sweep deletes E. Repaired: the outstanding publish ticket keeps it.
  - Both waits get one delivery each.
- `ev1-replay-atomic`: W1 alone. Owner 1 dies in the match step and S1 consumes what it was given; owner 2 takes over,
  sweeps and replays the ticket.
  - Reviewed (the split step and the built sweep): the sweep deletes S1's consumed done row, and the replay delivers E a
    second time.
  - Repaired: nothing landed, so the replay delivers once.
  - `negative` (the split step with the repaired sweep) also delivers once: the delivery-row hold keeps S1's done row
    while E, held by its outstanding publish ticket, exists, so the replay meets the duplicate. Either rule alone
    prevents the duplicate here; `split-no-row-hold` removes both and fails.
- `ev1-replay-consumed`: as `ev1-replay`, with S1 and S2 consuming their deliveries before the sweep. W2 is owed E twice
  over, by its catch-up and by the replayed match step. Repaired: the delivery-row hold keeps S2's done row, so the
  replay finds the duplicate and each wait has one delivery. `negative` (the sweep without the hold) deletes the row,
  and S2 gets E twice.

**Mutations.**

- `match-at-now`: the catch-up reads through the real `KeptMatchesAsync`, which at 101 finds nothing.
- `p0-at-page-commit`: P0 is recomputed at page 2's commit (101), so the sweep between pages deletes E2.
- `no-horizon`: P0 = RegisteredAt, which entitles the stepped-back wait to the swept event.
- `split-no-row-hold`: the split match step and the built deletion of done rows; the replay duplicates.
- `meet-waits-while-suspended`: the publish and meet tickets wait for the resume, so E is still held at 102.
- `match-at-acceptance`: the match step reads the subscriptions committed at the event's acceptance.

## FR-2 (R5-M3): the drain's outstanding-work credit

**The rule run as `repaired`** (the coordinator's final rule).

- **Admission.** A drain page is admitted once the previous page's gate row is handed off: delivered into a committed
  model turn, or held. A trial counts once its turn commits.
- **The credit.**
  - An admitted page restores at most `P_page` − u rows, where u is the restored rows not yet handed off.
  - It stops before a row that would take the stored bytes (metadata + payload length) of the u rows and the page above
    `B_credit` = 4 MiB.
  - With u = 0, the first row always goes.
  - A page cut to zero waits and never completes the drain.
- **Takeover.** A takeover resets the credit to durable state.
- **Suspension.** A page waits while the instance is suspended (M3-5, unchanged; the coordinator confirmed this and is
  removing "withheld by a suspension counts as handed off" from the transitions page).

**What bounds what** (accepted by the coordinator): the credit bounds the backlog, and the handoff gate governs
Complete's entitlement. A gate row that is not handed off is itself one of the u rows, so `P_page` − u already bounds a
page. The `seen-gate` mutation, the credit with the Seen gate, passes every FR-2 variant: the gate is not the bound.
The cohort precedence bites in `fr2-r4b1-held`.

`reviewed` and `negative` gate a page on the Seen test (the gate row done, held, or seen at this epoch) and restore
`P_page` rows.

**The schedule.**

- S runs `Parallel(H, Event X 'never')` on the real `NativeModel`, with H a `HeldGate` over `Event go`.
- Park kept the restored rows: no-op `native.resume` rows, and composite signals to H that the model holds.
- `A = native.resume go` arrives during the drain wherever H must finish, and is withheld until Complete.
- A neighbour instance N of the same partition gets one input every pass.

One owner pass follows `RunAsync`'s order (`Worker.cs` 322–354):

1. The built poll (`InMemoryStore.cs` 219–221) and the flush's marks (`Worker.cs` 1397).
2. One turn of S and one of N. During the drain, S's control kinds come first, then its released rows (R4-B1). A model
   turn takes the built batch (845–877).
3. The flush's trial selection (1396–1398): one suspect, cohort suspects first, delivered alone.

A waiting page sends its `kernel.drain` with a 3 ms delay.

**What the runs show.**

- **Reviewed** restores whole pages before any handoff in every variant:
  - `fr2-base`: 510 rows are pending at the first model handoff;
  - `fr2-bytes`: one page of 11 rows, 26 MiB;
  - `fr2-zero-cut`: page 1 holds the 4 MiB row and 169 others.
- **Repaired** keeps pending non-held restored rows at most 170. It keeps their bytes at most `B_credit`, or one
  larger row alone:
  - `fr2-bytes`: pages of 3, 3, 3 and 1, then the 16 MiB row alone, with at most three 1 MiB rows pending at once.
  - `fr2-hold-takeover`: the five holds lost at the takeover give u = 5. Page 3 restores 165 rows and page 4 the last 5.
  - `fr2-zero-cut`:
    - page 1 is the 4 MiB composite signal S0 alone, which the model holds;
    - page 2 is consumed;
    - the takeover loses the hold, so u is S0's 4,193,618 bytes, and the next page is cut to zero and waits;
    - S0 is tried and held again, and the page then restores 170 rows.
  - `fr2-takeover`: the new owner tries page 2's 170 suspects one at a time before page 3.

Every run asserts:

- the peak pending non-held restored rows and bytes;
- the held-row gauge;
- that the drain completed;
- that every kept row was restored and reached a model turn, and none is still kept;
- in the hold and zero-cut variants, that `HeldGate` accepted a signal once A arrived;
- in `fr2-suspend`, that S had no model turn while suspended;
- in `fr2-bytes`, the 1 MiB and 16 MiB bounds;
- that S still runs.

The neighbour's turns are reported as a count. The fixture gives each ready instance one turn per pass, as `RunAsync`'s
loop does, so the share holds by construction and is not asserted.

**Mutations.**

- `seen-gate`: the credit with the Seen gate. It passes every FR-2 variant, which shows the gate is not the bound.
- `credit-metadata-only`: counts metadata only, so all eleven rows of `fr2-bytes` are restored at once.
- `p-page-whatever-u`: restores 170 rows with u = 5, so 175 are pending.
- `zero-cut-completes`: completes the drain at the cut page, with 170 rows still kept.
- `pages-while-suspended`: lets a page run while S is suspended, with the withheld rows counted as handed off (the
  clause the transitions page drops); 510 rows are pending at resume.
- `held-gate-waits`: counts a held gate row as not handed off, so the drain stalls behind it.

**The R4-B1 traces, re-staged.** These are Review4.Blockers' `fr1-base` and `fr1-held-crash`, copied and run under the
handoff gate.

- `fr2-r4b1-base`: R (F's real Native timer, seq 1100) is restored by the final page, which sends C at 950. A, a
  `native.cancel` at 900 from another sender, is withheld.
  - Under the handoff gate C waits. R reaches a committed Native turn and F completes once; C then completes, and A
    cancels.
  - The reviewed rules (Seen gate, cohort precedence) pass as in Review4.
  - `negative` (the handoff gate with no cohort precedence) passes too, because R is handed off before Complete.
  - `seen-gate-no-precedence`, the fourth round's failing rules, lets A cancel first.
- `fr2-r4b1-held`: R is a composite signal the model holds, so it is held at Complete, and a crash takeover follows.
  - With the cohort precedence, R is tried first and held again, and then A cancels with R handed back: model inputs
    R, R, A.
  - Without it, A is tried first and R is dropped under the finished-instance rule.

## JR-1 (R5-M4): a fenced, authenticated job result

**The schedule.**

- S runs `Sequence(Set svc alpha, Parallel(c, Set svc beta))` on the real `NativeModel`, with `c` a Background node
  bound to `svc` (as in Astra4.Sizes). Its real start turn arms the job (`NativeModel.cs` 347–355).
- The ticket `c9_0/subject_1!2_w6` commits with the row through the real `CommitAsync`.
- The job body is the real `NativeModel.ExecuteJobAsync` (186–197) running `EffectEcho`. It counts physical invocations
  and returns `alpha|<effect key>`.
- While J runs, an input parks S, mirroring `WorkflowInstance.Park` (120–132): the job registry is kept, and Park's
  keeping is proposed. J's body then returns.
- A crash takeover follows (the real `TakeAsync` from another node after the lease), then a recover and the owner's
  poll.

**The paths the rule sets share.**

- The parked instance consumes late input with no model turn (`WorkflowInstance.cs` 78–90).
- When that consumption takes J's durable ticket, or this worker's completion under the ticket's key, the built
  `RemainingJobs` (135–144) drops J's key from the registry. The repaired rule (the coordinator's decision of 18
  November) keeps the entry of an undone current-generation ticket while the instance is parked.
- Park keeps the row `Arrived`.
- The recover Open clears a kept job row whose key the registry no longer holds, or whose generation is not the
  registry's current one (observability.md 1336–1343).
- The built check for a supplied completion is the real `Delivery.DiscardedAt` (`Turns.cs` 109–112), over a real
  `Slot`.

**The rule sets.**

- `reviewed`: the completion stays in memory (`CompletedJob`, `Worker.cs` 1297–1306). The parked instance consumes it,
  `RemainingJobs` drops J's key, the Open clears the kept ticket, and the result is lost.
- `reviewed-kind`: the kept row also persists the result kind under the ticket's key. It is lost the same way.
- `repaired`: a standalone fenced commit consumes the ticket (counted) and inserts the result row `…!result!g1` under
  the reserved scope 2^62. Its id derives from the ticket's full key and the generation.
  - The row's key differs from the ticket's, so `RemainingJobs` keeps J in the registry.
  - The new owner accepts it by its identity and the registry's current generation. The built `DiscardedAt` alone
    refuses it, and the report prints that.
  - Park keeps it, and recover restores it.
  - One model turn resumes `c` with `alpha|c9_0/subject_1/c/-/0`, the arm-time input and the original effect key. J
    runs once.
- `reviewed-keeps-registry` (`jr1-crash-after`): a third reading of the reviewed design. The registry keeps J's key, so
  the kept ticket is restored and the built `Hold` and `StartJob` enter J a second time after its result had committed.
- `negative`: the same conversion with an ordinary-scope result row. The real `DiscardedAt` refuses it after the
  takeover. In `jr1-crash-before` the negative removes the registry rule instead, and the result is lost.
- `generation-blind` (`jr1-obsolete`): the Open restores the g0 row, and the model receives `stale`.

**The variants.**

- `jr1-crash-before`: the owner dies after J's body returned and before any commit recorded the result.
  - The new owner does not enter a parked row's ticket (`Worker.cs` 1169–1177), so the parked instance consumes it as
    late input.
  - Repaired: the registry keeps J. Recover clears the kept ticket and restarts J from its JobSpec under g2
    (`…!2_w6@g2`, R4-M3). J runs a second time, the one re-run the at-least-once allowance permits, and its result
    `alpha|…` reaches the model once.
  - Reviewed: the built `RemainingJobs` drops J's key, the Open clears the ticket, and J is never re-run: the result
    is lost.
- `jr1-running`: J completes while S runs, and the built path delivers it once under every rule set.

Every run asserts:

- one model result;
- its value;
- the physical invocation count against the allowance (2 for a crash before the conversion commit, else 1);
- that S finished;
- that no undone row of S is left.

## PO-1 (R5-M6): precedence in the poison comparison

**The history, on the real store.**

- S runs `Parallel(Delay F 50, Delay F2 100000)`. Its real start turn arms F's timer R (due 1050).
- J, a job ticket that hangs, is at seq 900, and R at seq 1100.
- Owners 1, 2 and 3 each put J on trial (the real `CommitAsync` trial mark) and die.
- Each next take is the real `TakeAsync`, which adds an attempt to the rows on trial (`InMemoryStore.cs` 70–82). So
  owner 3 is the last failed owner, and J reaches `MaxAttempts` (3).
- Owner 4 marks J and R seen.
- `po1-cohort`: R was released by the drain's final page and J by an early page, and Complete set the cohort mark.
- `po1-open`: the drain is still open, with its last control C at seq 950.
- In both, a new process takes over before either input is dispatched. It is a cold takeover with no memory and, in
  `po1-cohort`, no control.

**The mirrored owner** reads through the real `PollAsync`. Its rules mirror the built Worker for one instance:

- J is a poison input (`Worker.cs` 705–707) and R a suspect (577–578).
- `NextTrial` (733–763) skips a suspect that does not precede the due poison (`PrecedesPoison`, 714–716).
- Ordinary delivery parks S with the exhausted J (`RunTurn` 1064–1065, `WorkflowInstance.Park` 120–132).
- A re-park interrupts an open drain.
- FR-1's cohort rules apply on top.

**The outcomes.**

- `reviewed`: `PrecedesPoison` rejects R's trial, because J sorts first.
  - `po1-cohort`: the mark withholds J while R is pending, and nothing moves (stall).
  - `po1-open`: C waits for its untried gate row R, and J, a released row, parks S. The re-park interrupts the drain,
    and R reaches the parked instance as late input (wrong order; F never completes).
- `repaired`: in the cohort context, a non-cohort poison input never precedes a cohort suspect. R is tried first and its
  real timer turn completes F; then J parks S, its fault naming J.
- `po1-ordinary`: the built comparison holds under every rule set. J parks S first, and R then reaches the parked
  instance.
  - The same history is also handed to a real `Worker` (`WorkflowInstance` over the real `NativeModel`), pumped until
    idle. It parks S with "message …!4_hang was on trial when 3 owners were lost".
  - The run asserts that the mirrored owner's outcome equals the real Worker's.

## What this proves, and what it does not

**Executed in this repair** (harness runs only), each under its reviewed, repaired and negative sets and the mutations
above:

- EV-1: the atomic match step, P0 and its horizon, and per-ticket sweep coverage;
- FR-2: the handoff gate and the u-bounded, byte-bounded pages, the zero cut included;
- JR-1: the fenced conversion, its authentication and its generation check;
- PO-1: the precedence in the poison comparison.

**Real prototype code these runs go through** (each report lists its calls):

- `InMemoryStore`: `CommitAsync` (fenced and count-checked), `TakeAsync`, `PollAsync`, `PublishAsync`,
  `PublishedMatchesAsync`, `KeptMatchesAsync`, `AddEventAsync`, `SweepAsync` and `AddAsync`;
- `EventRoute.Delivery`;
- `NativeModel.TurnAsync`, with its real timer, resume, cancel, composite-signal hold and job-result handlers;
- `NativeModel.ExecuteJobAsync`;
- `Delivery.DiscardedAt` over a real `Slot`;
- `InboxKey.Ticket` and `CreationProtocol.Origin`;
- in `po1-ordinary`, a real `Worker` with `WorkflowInstance`.

**Existing prototype behaviour** these runs exhibit:

- the publisher's lookup reads the subscriptions present at the lookup (`InMemoryStore.cs` 281–285);
- the kept-event read filters by the read's clock (287–298);
- the sweep deletes an event once its `KeepUntil` has passed (354);
- a consumed delivery with a `KeepUntil` stays a done row until then (`Worker.cs` 1452–1453), and the sweep then deletes
  it (`InMemoryStore.cs` 353);
- `AddEventAsync` deduplicates only against present rows (240–241);
- meet and publish tickets pass suspension (`Worker.cs` 640–645, 1315–1339);
- the in-memory `CompletedJob` conversion keeps the ticket's identity (`Worker.cs` 1247–1252, 1297–1306);
- `RemainingJobs` drops a job's key when a parked instance consumes its ticket (`WorkflowInstance.cs` 78–90, 135–144);
- a durable `job-finished` row is a supplied completion (`Turns.cs` 109–112);
- the poison comparison is plain `seq` order (`Worker.cs` 714–716);
- an exhausted ticket parks its instance (1064–1065).

**Mirrored from the built code**, because each needs a real Worker's private state:

- the Worker's poll handling, marks, suspect test, trials and batching (FR-2, PO-1);
- the faulted instance's late-input path, `RemainingJobs` and `WorkflowInstance.Park` (JR-1, PO-1);
- the built suspend and resume (FR-2).

**Proposed and modelled in the harness:**

- EV-1: the atomic match step, P0, H, the keep flag and the sweep coverage;
- FR-2: the drain's pages, gate and credit;
- shared by the drain cases: `ReleasedBy` and the cohort mark;
- Park's `KeptBy` and the recover page;
- JR-1: `JobGeneration` and the reserved result scope.

**Left for qualification at Kn:**

- the Worker implementing these rules, with its commit batching and fence (K3);
- providers' commit clocks, the insert-if-absent match step, the sweep's coverage statements and H, and duplicate
  semantics (K4);
- real model handoff and holds under load, and the byte credit against real payloads (K6, K12).

## Decisions applied (coordinator, 18 November)

1. **`ev1-b-stepback`:** P0 = max(RegisteredAt, H + 1 ms). The run passes under `repaired`.
2. **`ev1-replay-consumed`:** the sweep keeps an event's done delivery rows while the event row exists, and the event row
   while its publish ticket is outstanding (option (a)). The run passes under `repaired`.
3. **`jr1-crash-before`:** while the instance is parked, consuming late input keeps the registry entry of an undone
   current-generation ticket, and recover restarts the job from its JobSpec under the next generation. The run passes
   under `repaired`. The third reviewed reading runs as `reviewed-keeps-registry`.
4. **FR-2:** the credit bounds the backlog, and the handoff gate governs Complete's entitlement. `seen-gate` stays as
   the mutation that shows the gate is not the bound.
5. **Suspension:** pages wait while the instance is suspended (M3-5).
6. **JR-1's Worker change:** the built `Delivery.SuppliedCompletion` (`Turns.cs` 109) refuses every durable
   `job-finished` row, the reserved-scope result row included. Accepting that row is a K3 change row on the transitions
   page.

## Notes

- **The split match step.** With decision 2, the split match step adds no duplicate in `ev1-replay-atomic` as long as
  the event row exists: `negative` passes. The atomic step is what the non-keeping rule reads at commit. Only the
  combined mutation `split-no-row-hold` fails.
- **An unswept event at H.** An event whose KeepUntil equals H but that the sweep has not deleted is excluded from a
  later wait's catch-up by P0 = H + 1 ms. It has already expired, so this is the stated, accepted behaviour.
- **A clock step-back also excludes live events.** After the clock steps back below H, a live event published with
  KeepUntil ≤ H (`ev1-b-stepback`'s E2, KeepUntil 99, published at 98) is excluded from a keeping wait that registers
  before it expires. The run asserts this outcome of the stated rule. The cost falls only on a database clock that steps
  back; a coordinator note may want to state it on the events page.
