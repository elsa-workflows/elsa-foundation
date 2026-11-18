# Review 4 blockers: FR-1 and RT-1

Executable evidence for the two Blockers of the fourth Astra High review of the kernel proposal
(elsa-workflows/elsa-foundation#4229): R4-B1, a final released row losing to a late arrival, and R4-B2, a delete
stranding a live subtree. Each case runs one schedule under three rule sets:

- `reviewed`: the rules of the reviewed design (`ea6596a85`);
- `repaired`: the repair of the fourth fix round, as the transitions PR (#4780) states it;
- `negative`: the repaired rules with the one repaired rule removed.

Mutations of the repaired set run where they bite:

- FR-1: `memory-precedence`, `held-blocks`, `trial-plain-seq`, `skip-target`, `no-shelf`, `no-cohort-mark`,
  `one-row-read-counts-held`, `mark-ignores-controls`, `controls-wait-on-ticket` and `boundary-holds-cohort`;
- RT-1: `cascade-any`, `successor-admitted`, `successor-inherits` and `root-retains`.

## Run

From `prototypes/kernel-model`:

```
dotnet restore experiments/Review4.Blockers --locked-mode
dotnet build experiments/Review4.Blockers -c Release --no-restore
BIN=experiments/Review4.Blockers/bin/Release/net10.0/Kernel.Review4.Blockers.dll
dotnet $BIN --list                                   # every case, rule set and expected outcome
dotnet $BIN --case fr1-base --rules repaired         # one run: exit 0 when every assertion holds, 1 otherwise
dotnet $BIN --all                                    # every run, then a summary; exit 0 only if all match the expected matrix
experiments/Review4.Blockers/run.sh                  # every run one by one, then --all; exits with the matrix result (2 if setup fails)
```

[results.txt](results.txt) holds the output of `--all` and the commit it ran at.

## Which page case each run backs

| Run | Page case | Expected |
|---|---|---|
| `fr1-base` | E-7, as stated | reviewed FAIL, repaired PASS, negative FAIL |
| `fr1-takeover-before-complete` | E-7, takeover after R's mark and before the model handoff (a retake by the same process) | reviewed FAIL, repaired PASS, negative FAIL, memory-precedence FAIL |
| `fr1-takeover-after-complete` | E-7, the precedence after Complete survives a takeover (a retake by the same process) | reviewed FAIL, repaired PASS, negative FAIL, memory-precedence FAIL |
| `fr1-trial-takeover` | E-7 on the built recovery path: a crash takeover after Complete, with A and R as suspects | reviewed FAIL, repaired PASS, negative FAIL, trial-plain-seq FAIL, skip-target PASS, no-cohort-mark PASS |
| `fr1-poll-limit` | E-7 on the built recovery path, and the page's shelving rule: as `fr1-trial-takeover`, with a poll limit of 1 | reviewed FAIL, repaired PASS, negative FAIL, trial-plain-seq FAIL, skip-target PASS, no-cohort-mark FAIL, no-shelf FAIL |
| `fr1-trial-takeover-open` | The rule that a withheld suspect skips only itself, never its target: a crash takeover before Complete | reviewed FAIL, repaired PASS, negative PASS, trial-plain-seq PASS, skip-target FAIL, no-cohort-mark PASS |
| `fr1-held-poll-limit` | E-8 run 2: R1 held, R2 beyond a bounded poll | reviewed FAIL, repaired PASS, negative FAIL, one-row-read-counts-held FAIL |
| `fr1-control-after-complete` | E-7's control run: a control polled while the cohort mark is set | reviewed FAIL, repaired PASS, negative FAIL, mark-ignores-controls FAIL |
| `fr1-control-crash` | E-7's control run after a crash takeover: R is a suspect behind K's recovery boundary | reviewed FAIL, repaired PASS, negative FAIL, boundary-holds-cohort FAIL |
| `fr1-ticket-control`, `fr1-ticket-control-parent`, `fr1-ticket-control-suspend` | E-11: an operator terminate, a parent's tree terminate, and a suspend then a resume, with a hanging restored ticket | PASS under all three, controls-wait-on-ticket FAIL |
| `fr1-model-end` | E-7, model-end variant: R dropped under the finished-instance rule | PASS under all three |
| `fr1-model-end-recall` | Control for E-7's model-end variant: the ending turn recalls R | PASS under all three |
| `fr1-held` | E-8, after Complete; the evidence is the repaired run and `held-blocks` | PASS under all three, held-blocks FAIL |
| `rt1-a` | DT-7 order (a); RM-1, A's marker and P's terminate | reviewed FAIL, repaired PASS, negative FAIL |
| `rt1-a-g-deleted` | DT-7 variant: G deleted while G2 lives | reviewed FAIL, repaired PASS, negative FAIL |
| `rt1-b` | DT-7 order (b) | PASS under all three |
| `rt1-nonparent` | DT-8; RM-1, the non-parent's terminate | reviewed FAIL, repaired PASS, negative FAIL, cascade-any FAIL |
| `rt1-successor-unreachable` | DT-9(a) | reviewed FAIL, repaired PASS, negative FAIL |
| `rt1-successor-gap` | DT-9(b), the ObligationOutstanding start refusal; `successor-admitted` is its mutation | reviewed FAIL, repaired PASS, negative FAIL, successor-admitted FAIL |
| `rt1-successor` | DT-9(c), a keyed root; RM-1, a root's marker keeps nothing | PASS under all three, successor-inherits FAIL, root-retains FAIL |

`fr1-held` passes under `reviewed` and `negative` because A reaches the model before S is ever held, so those two
runs never exercise a held cohort row and are not E-8 evidence. Only the `repaired` run, which asserts that S was held
when A was delivered, and the `held-blocks` mutation are.

## FR-1 (R4-B1): release cohort precedence

The workflow is `Parallel(Delay F 50, Delay F2 100)` on the real `NativeModel`. Its real start turn arms both
timers. R is F's timer at seq 1100. While the instance is parked, R arrives and is kept as `Arrived`. Recover opens
generation 1, and the first page, C0, is the final page. It restores R with `ReleasedBy (1,1)` and sends the next
drain control C at seq 950. A is an ordinary `native.cancel` (`NativeKinds.Cancel`) from sender `0/other` at seq 900.
It is withheld during the drain. The owner's polls mark every polled row seen, and that includes the withheld A
(Worker.cs 1397). C finds no kept row past the cursor, and its gate row R is taken up, so C completes the drain. The
model then gets one input per turn, in the order the rule set gives:

| Rule set | Offer order | Suspect trials | Outcome |
|---|---|---|---|
| reviewed | plain tuple order | built NextTrial: seq order; a withheld suspect skips its whole target | A's real cancel turn ends the model and recalls R; F completes 0 times |
| repaired | open drain: controls, then the cohort, then the rest. After Complete: the non-held cohort rows first, and nothing else of the instance while one remains; a control waits behind them | no non-cohort suspect ahead of a cohort row or grouped with one; a withheld suspect skips only itself | R's real timer turn completes F once, with F2 still waiting; then A cancels |
| negative | controls first while the drain is open, then tuple order | seq order; a withheld suspect skips only itself | as reviewed, except in `fr1-trial-takeover-open` |
| memory-precedence | repaired, with the cohort kept in the owner's memory only | as repaired | in both retake runs the new owner offers A first |
| trial-plain-seq | repaired | cohort-first trials off: plain seq order | after Complete, A is tried first |
| skip-target | repaired | a withheld suspect skips its whole target, as built | before Complete, the withheld A keeps R from its trial and the drain stalls |
| no-shelf | repaired | a suspect the cohort rule passes over is not shelved | with a poll limit of 1, every poll returns A again, R is never polled, and the run stalls |
| no-cohort-mark | repaired, with no durable cohort mark: the owner knows only the cohort rows it has polled | as repaired, over polled rows | with a poll limit of 1 the owner holds A alone and tries it |
| one-row-read-counts-held | repaired, with a cohort read of one row that counts held keys | as repaired | the read returns the held R1, so the mark waits for nothing and A is offered before R2 |
| mark-ignores-controls | repaired, except that a control polled while the mark is set runs ahead of the cohort read | as repaired | the parent's terminate overtakes R, and R is dropped |
| controls-wait-on-ticket | repaired, with job tickets counted as cohort rows | as repaired | the controls wait on the hanging ticket J until the step bound |
| boundary-holds-cohort | repaired | cohort suspects stay behind a control's recovery boundary, as built | after a crash, K waits for the cohort and R waits behind K: neither moves |

Under `repaired`, the cohort is the set of rows whose `ReleasedBy` is the creation's latest generation and that are
not Done; a job ticket never is one (`Kind NOT IN (:ticketKinds)`). Complete sets a durable cohort mark (creation,
generation) on the instance row whenever such a row may remain. While the mark is set and the owner holds no non-held
cohort row, it runs the mark's read: a one-row existence check for a cohort row, skipping the keys the model holds and
ticket kinds. The check loads nothing; the row reaches memory through the ordinary poll. A check that finds none clears
the mark. While a non-held cohort row remains, no other row of the instance is offered, control kinds included, and no
non-cohort suspect is tried. While the drain is open or the mark is set, cohort suspects are tried first, in tuple
order, ahead of a control's recovery cut. The control takes its built place once no non-held cohort row remains. A
suspect the cohort rule passes over is shelved: the flush marks it seen at this epoch,
neither on trial nor cleared to run, so later polls move past it. Held cohort rows are skipped by the mark's rule, as
they are by the poll. When the model ends, held rows are handed back with the ending input, or meet the
finished-instance rule.

The poll is the built one. The store returns due, not-Done rows with `Seen < epoch` in seq order, up to the poll limit
(InMemoryStore.cs 219–221). The owner ignores a row already in memory. An untried, unshelved suspect keeps its old
`Seen`, so it comes back in every poll and fills a place there. That is why the built flush shelves a suspect of a
withheld instance (Worker.cs 691–699, F12). The repaired rule extends that shelving to the suspect the cohort rule
passes over (proposed).

The variants:

- `takeover-before-complete` and `takeover-after-complete`: the same process retakes the partition, before Complete
  and after it. Memory is lost, but its own earlier marks make no suspects (Worker.cs 577–578).
- `trial-takeover`: another process takes over after Complete. A and R carry the lost owner's Seen, so both are
  suspects. The trial path mirrors NextTrial (Worker.cs 734–760, with TrialGroup 1 from line 35) line by line for one
  instance: seq order, the recovery boundary at the earliest pending control (796–820), a target skipped behind a
  suspect it cannot be given, and one suspect per group. The built "cannot be given" test is the instance-level
  `Withheld(slot)` with its exemptions (Worker.cs 756). Feeding the drain's per-row withholding into that test is
  proposed.
- `poll-limit`: as `trial-takeover`, but the new owner's PollLimit (Worker.cs 14, used at 516) is 1. Its first poll
  returns A alone.
  - Under `repaired`, the mark's check finds R, so the cohort rule passes A over and shelves it. The next poll moves
    past A to R. R is tried and completes F, the check clears the mark, and then A is tried.
  - Under `no-shelf`, A keeps its old `Seen` and every poll returns it again. R is never polled, and the run stalls.
- `trial-takeover-open`: another process takes over before Complete. A, a withheld suspect, sorts first.
  - Under the built rule (`reviewed`, `skip-target`), A skips the target, so R is never tried, the gate never sees R,
    and the drain stalls until the step bound.
  - Under `repaired` and `trial-plain-seq`, A skips only itself, and R, a cohort suspect, is tried first, ahead of C's
    cut. Under `negative`, R waits for the later retry control C2000 and is then tried. In each, C completes after R.
  - This case backs the skip-target rule, which also applies in the cohort-first pass. The precedence plays no part,
    so `negative` passes.
- `held-poll-limit` (E-8 run 2): the workflow is `Parallel(H, Event X)`, with H a `HeldGate` over `Event go`. The final page
  restores R1, a composite signal to H at seq 1000, and R2, a `native.resume x` at seq 1100. The same process retakes
  the partition after Complete, with the cohort mark set and PollLimit 2. Four arrivals for another instance sit at
  seq 1001–1004, so the first poll returns A and R1, and R2 only comes two polls later. R1 reaches the model, which
  holds it through Native's real hold path.
  - Under `repaired`, the mark's check skips the held R1 and finds R2, so A waits. The poll reaches R2, R2's real turn
    completes X with R1 handed back, and then A cancels.
  - Under `one-row-read-counts-held`, the check returns the held R1, A is offered, and R2 is dropped.
- `control-after-complete` (E-7's control run): after Complete, with the mark set, the parent's tree terminate K (`kernel.terminate`,
  seq 1000) arrives. The same process retakes with PollLimit 1, so the first poll returns K and not R.
  - Under `repaired`, K waits: the mark's check finds R. The next poll returns R, R's real timer turn completes F, the
    next check clears the mark, and then K's built terminate ends the instance.
  - Under `mark-ignores-controls`, K runs first and R is dropped.
- `control-crash`: as `control-after-complete`, but another process takes over, so R carries the lost owner's mark and
  is a suspect at seq 1100, behind K's recovery boundary at 1000.
  - Under `repaired`, R is tried first, ahead of K's cut. Its real timer turn completes F, the check clears the mark,
    and K's built terminate runs.
  - Under `boundary-holds-cohort`, the mark withholds K until R is delivered, and the boundary keeps R behind K, so
    neither moves until the step bound.
- `ticket-control`, `ticket-control-parent`, `ticket-control-suspend` (E-11): a retry selects job ticket J, the final
  page restores it, J's driver enters the job and it hangs, and Complete sets the mark. Then a control arrives: an
  operator terminate, a parent's tree terminate, or a suspend followed by a resume. Under `repaired`, the mark's check
  skips ticket kinds, finds no cohort row and clears the mark. Each control applies at once, and a terminate cancels
  J. Under `controls-wait-on-ticket`, J counts as a cohort row, which is never offered to a turn, so the controls wait
  until the step bound. The suspend, resume and terminate are the actor type's built controls, mirrored.
- `model-end`: an earlier kept `native.cancel` Q is released with R, and there is no late arrival. R here is a
  `native.resume go`, which the model never recalls. Q's real cancel turn ends the model, and R is dropped under the
  built finished-instance rule.
- `model-end-recall`: the control. R is F's timer, and Q's cancel recalls it before the drop rule is reached.
- `held`: the model holds the cohort row, through a real Native hold path. `HeldGate` is a harness-authored composite
  activity, like Incarnation.Run's `HeldReceiver`. It defers composite signals until its child `Event go` completes.
  Native's `FinishSignals` then calls `ITurn.Hold`. S is a composite signal to H, released as the cohort. A is
  `native.resume go`, the input S waits for. Under `repaired`, the poll skips the held S, so A is delivered with S
  handed back, and S is accepted afterwards. The `held-blocks` mutation counts a held cohort row in the precedence,
  and the instance stalls.

FR-1 runs assert, as each case needs:

- F's completion count;
- the exact sequence of model transitions;
- that F2 was still waiting when A arrived;
- that the instance ended and the drain completed;
- R's fate: consumed by a model turn, dropped, or recalled;
- that no undone or kept rows remain.

## RT-1 (R4-B2): retirement keeps a bounded cancellation obligation

The tree is P → A → G → G2, with live G and G2 and job J on A. A, G and G2 sit at generated child ids
(`g{n}/child/…`), and P is at creation 2. The rows are real `InstanceRow`s with real registry and job-key JSON. A is in
an unprotected parked-delete drain. An operator terminates P, and P's terminate sends the parent-bound terminate T to
A.

Complete sends the one Faulted notice through the real `WorkflowInstance.Finished` and retires A through the real
`CreationProtocol.Retire`. Under `repaired`, a marker whose row recorded a parent keeps that parent, the registry and
the job keys. A root's marker keeps nothing. A parent-bound terminate that the real `TargetError` refuses with
`RetiredCreation` sends terminate to the retained children and cancels the retained jobs. In the same commit it
writes the marker at its next revision (RM-1), with the obligation cleared and every other field unchanged.

The real `Retire` and `Origin` put no constraint on that shape. The built `CreationProtocol.Validate` does: called on
that write, it refuses it with "invalid lifecycle retirement", because it accepts a retired row only from a delete
of a live row (Creation.cs, `Validate`). Each cascade report prints that refusal. The release must widen `Validate` for
the obligation write.

The cases:

- `rt1-a`: T is delivered after A's Complete.
  - `reviewed`: the marker keeps nothing, so G, G2 and J survive.
  - `repaired`: the cascade terminates G, G's built terminate ends G2, and J is cancelled.
  - `negative`: the marker drops the registry, so J is cancelled while G and G2 survive.
- `rt1-a-g-deleted`: G ended on its own while G2 lives, and G was deleted. A's cascade reaches G's marker, which
  cascades on to G2.
- `rt1-b`: T is delivered before Complete and refused as "resolution open", with the cascade of #4756. This passes
  under every rule set. It is the control showing that the reviewed rule covers this order.
- `rt1-nonparent`: before T arrives, four terminates without authority reach the marker. Each is refused with
  `RetiredCreation` and leaves the obligation unchanged:
  - an application send from X;
  - a terminate from P's earlier creation 1;
  - an outside add naming P as sender;
  - an operator's terminate.

  T then discharges the obligation. `cascade-any` terminates G at X's send.
- `rt1-successor-unreachable` (DT-9(a)): a start for creation 2 addressed to A's retired generated id arrives twice,
  as an outside add and as X's application send. The real `Message.SuppliedCreator` and the real
  `Delivery.DiscardedAt` classify both as supplied creators to discard. `DiscardedAt` runs over a real `Slot` holding
  A's marker, reached by reflection. The Worker's consumption of such a delivery, with no turn and no row change
  (Worker.cs 1030–1037), is mirrored. The marker and the obligation are unchanged, no creation 2 appears, and T then
  discharges the obligation. A control asserts that the same start at the creator scope, which only `StartChild`
  (with a fresh id) and key admission mint, is no supplied creator.
- `rt1-successor-gap` (DT-9(b)): a start for creation 2 reaches the start check directly. The real `StartError`
  accepts it. The repaired rule then refuses it with `ObligationOutstanding`, because A's marker still holds an
  obligation. This is the coordinator's defence-in-depth rule. In valid operation it never fires, as
  `rt1-successor-unreachable` and `rt1-successor` show. The marker stays, and T discharges the obligation.
  `successor-admitted` drops the rule: the successor replaces the marker, the real `TargetError` refuses T with
  `WrongCreation`, and G, G2 and J survive.
- `rt1-successor` (DT-9(c)): a keyed root R under key K (`5/t1:invoice`), with a live child H, is deleted. A keyed
  start carries no parent (KeyAdmission.cs 31–32), so R's marker keeps nothing. The real `StartError` accepts B,
  creation 2, at the same id. B inherits no registry, starts H2, and is terminated by an operator. Its terminate
  reaches H2 only. H runs to its own end, and the real `TargetError` refuses H's notice to R's creation at B with
  `WrongCreation`. Two mutations fail:
  - `successor-inherits` gives B R's registry, so B sends H a terminate. H's parent binding refuses it, because H's
    `ParentCreation` is R's.
  - `root-retains` lets R's marker keep its registry, so the obligation refuses B's start as `ObligationOutstanding`.

In every run of P's tree, the real `TargetError` refuses two late arrivals with `RetiredCreation`: J's late result at
A's id, and G's notice at A's marker (G2's notice at G's marker in `rt1-a-g-deleted`). RT-1 runs assert, as each case
needs:

- which descendants end terminated;
- whether J is cancelled;
- which obligations remain on markers;
- that no live row still lists children or jobs;
- that P receives exactly one notice from A;
- that no creation 2 appears at A's id;
- the refusal reasons;
- the obligation the marker recorded at Complete.

## What this proves, and what it does not

**Executed here:**

- the proposed drain and inbox transitions: page, the gate, Complete and the cohort mark, the cohort precedence and
  poll order, held rows skipped, and the suspect trial order;
- the proposed retirement obligation, its cascade and the marker's next revision;
- #4756's open-drain refusal;
- the ObligationOutstanding start refusal.

They run against real prototype code:

- `NativeModel.TurnAsync` with its real timer, cancel, event-resume and composite-signal hold handlers;
- `CreationProtocol.TargetError`, `StartError`, `Retire`, `Origin`, `Fingerprint` and `Validate`;
- `Message.SuppliedCreator` and `Delivery.DiscardedAt`, over a real `Slot`;
- `ChildRegistry.Read` and `Write`;
- `WorkflowInstance.Finished`;
- `DurableKeyJson` job keys and `InboxKey.Ticket`.

Each report lists the real calls it made. The internal functions are called by reflection, so no prototype project
changed.

**Mirrored from the built code**, because each needs a real `Worker` (`Terminate` is private and takes a
`TurnInput` that only the Worker builds, and `NextTrial` reads the Worker's private state):

- the Worker's recovery path, for one instance:
  - the store poll and its limit (InMemoryStore.cs 219–221; Worker.cs 14, 516–537);
  - the flush's marks (1397) and the `Shelf` marking of a withheld instance's suspects (691–699);
  - the suspect test (577–578);
  - NextTrial (734–760);
  - the recovery boundary (796–820);
- the built terminate of a live row (`WorkflowInstance.cs` 179–203), in RT-1 and for the FR-1 terminates;
- the built suspend and resume (`WorkflowInstance.cs`, `Control`);
- the child-notice path (`WorkflowInstance.cs` 36–61);
- the Worker's consumption of a discarded supplied creator (`Worker.cs` 1030–1037).

The parent binding at a live child is the proposed rule of #4756. Two steps of the mirrored recovery path are proposed:

- feeding the drain's per-row withholding into NextTrial's "cannot be given" test, which as built is the
  instance-level `Withheld(slot)` with its exemptions (Worker.cs 756);
- shelving the suspect the cohort rule passes over;
- trying cohort suspects ahead of a control's recovery cut.

**Left for implementation qualification:**

- the `Worker`: trial commits and their attempt counts, shelving, hold hand-back, commit batching and the fence;
- store providers: seq allocation on SQL and MongoDB, counted updates, unknown commit outcomes;
- real ownership: leases, takes and epochs;
- cross-partition transport;
- Park itself and the operator selection;
- a poll in flight across a page commit (R4-M4, E-9 and E-10);
- E-8 with the drain still open;
- a later Park turning undone cohort rows Stale;
- DT-7's delete that took over a discard;
- RM-1's size bound and its refusal of any other write to a marker;
- RM-1's list validation: a foreign child or ticket in a marker's retained lists refused, and `Validate`'s
  foreign-ticket mutation;
- RM-1's runs with no children and with one job.

The built `Validate` refusal of the marker's next-revision write is printed in each cascade report, as a note on
existing behaviour, and not asserted.
