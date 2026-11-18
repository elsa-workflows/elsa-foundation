# Review 4 blockers: FR-1 and RT-1

Executable evidence for the two Blockers of the fourth Astra High review of the kernel proposal
(elsa-workflows/elsa-foundation#4229): R4-B1, a final released row losing to a late arrival, and R4-B2, a delete
stranding a live subtree. Each case runs one schedule under three rule sets:

- `reviewed`: the rules of the reviewed design (`ea6596a85`);
- `repaired`: the repair of the fourth fix round, as the transitions PR (#4780) states it;
- `negative`: the repaired rules with the one repaired rule removed.

Mutations of the repaired set run where they bite: `memory-precedence` and `held-blocks` (FR-1), and `cascade-any`,
`successor-admitted` and `successor-inherits` (RT-1).

## Run

From `prototypes/kernel-model`:

```
dotnet restore experiments/Review4.Blockers --locked-mode
dotnet build experiments/Review4.Blockers -c Release --no-restore
BIN=experiments/Review4.Blockers/bin/Release/net10.0/Kernel.Review4.Blockers.dll
dotnet $BIN --list                                   # every case, rule set and expected outcome
dotnet $BIN --case fr1-base --rules repaired         # one run: exit 0 when every assertion holds, 1 otherwise
dotnet $BIN --all                                    # every run, then a summary; exit 0 only if all match the expected matrix
experiments/Review4.Blockers/run.sh                  # every run one by one, with its exit code
```

[results.txt](results.txt) holds the output of `--all` and the commit it ran at.

## Which page case each run backs

| Run | Page case | Expected |
|---|---|---|
| `fr1-base` | E-7, as stated | reviewed FAIL, repaired PASS, negative FAIL |
| `fr1-takeover-before-complete` | E-7, takeover after R's mark and before the model handoff | reviewed FAIL, repaired PASS, negative FAIL, memory-precedence FAIL |
| `fr1-takeover-after-complete` | E-7, the precedence after Complete survives a takeover | reviewed FAIL, repaired PASS, negative FAIL, memory-precedence FAIL |
| `fr1-model-end` | E-7, model-end variant | PASS under all three |
| `fr1-held` | E-8, after Complete | PASS under all three, held-blocks FAIL |
| `rt1-a` | DT-7 order (a); RM-1, A's marker and P's terminate | reviewed FAIL, repaired PASS, negative FAIL |
| `rt1-a-g-deleted` | DT-7 variant: G deleted while G2 lives | reviewed FAIL, repaired PASS, negative FAIL |
| `rt1-b` | DT-7 order (b) | PASS under all three |
| `rt1-nonparent` | DT-8; RM-1, the non-parent's terminate | reviewed FAIL, repaired PASS, negative FAIL, cascade-any FAIL |
| `rt1-successor` | DT-9(b); RM-1, a root's marker keeps nothing | PASS under all three, successor-inherits FAIL |
| `rt1-successor-unreachable` | DT-9(a) | reviewed FAIL, repaired PASS, negative FAIL |
| `rt1-successor-gap` | The ObligationOutstanding start refusal (coordinator's defence-in-depth rule); DT-9's first mutation | reviewed FAIL, repaired PASS, negative FAIL, successor-admitted FAIL |

## FR-1 (R4-B1): release cohort precedence

The workflow is `Parallel(Delay F 50, Delay F2 100)` on the real `NativeModel`. Its real start turn arms both
timers. R is F's timer at seq 1100. While the instance is parked, R arrives and is kept as `Arrived`. Recover opens
generation 1, and the first page, C0, is the final page. It restores R with `ReleasedBy (1,1)` and sends the next
drain control C at seq 950. A is an ordinary `native.cancel` (`NativeKinds.Cancel`) from sender `0/other` at seq 900.
It is withheld during the drain. A poll at the current epoch marks C and R seen. C finds no kept row past the cursor,
and its gate row R is taken up, so C completes the drain. The model then gets one input per turn, in the order the
rule set gives:

| Rule set | Order | Outcome |
|---|---|---|
| reviewed | plain tuple order throughout | A's real cancel turn ends the model and recalls R; F completes 0 times |
| repaired | while a recover drain is open or a cohort row remains: control kinds, then the cohort (`ReleasedBy` = the creation's latest generation, not Done), then the rest; otherwise the built order, a control taken alone after the rows ahead of it (Worker.cs 868–874) | R's real timer turn completes F once, with F2 still waiting; then A cancels |
| negative | control kinds first while the drain is open, then tuple order | as reviewed |
| memory-precedence | repaired, with the cohort kept in the owner's memory only | in both takeover runs the new owner offers A first, as reviewed |

The variants:

- `takeover-before-complete`: the owner is lost after R is marked seen. The new owner, at the next epoch, keeps only
  the durable rows, re-polls, and C's gate counts R only at the new epoch.
- `takeover-after-complete`: the owner is lost after Complete commits and before R reaches a model turn. Precedence
  comes from R's durable `ReleasedBy` tag and the row's `ResolutionGeneration`, so it survives.
- `model-end`: an earlier kept `native.cancel` Q is released with R, and there is no late arrival. Q's real cancel
  turn ends the model and recalls R, so R never reaches a model turn. The drain completes, and no `KeptBy` remains.
- `held`: the model holds the cohort row, through a real Native hold path. `HeldGate` is a harness-authored composite
  activity, like Incarnation.Run's `HeldReceiver`. It defers composite signals until its child `Event go` completes.
  Native's `FinishSignals` then calls `ITurn.Hold`. S is a composite signal to H, released as the cohort. A is
  `native.resume go`, the input S waits for. Under `repaired`, the poll skips the held S, so A is delivered with S
  handed back, and S is accepted afterwards. The `held-blocks` mutation counts a held cohort row in the precedence,
  and the instance stalls. No stub model is used.

FR-1 runs assert, as each case needs:

- F's completion count;
- the exact sequence of model transitions;
- that F2 was still waiting when A arrived;
- that the instance ended and the drain completed;
- R's fate (consumed by a model turn, or recalled);
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
writes the marker at its next revision with the obligation cleared.

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
- `rt1-successor`: a keyed root R under key K (`5/t1:invoice`), with a live child H, is deleted. A keyed start carries
  no parent (KeyAdmission.cs 31–32), so R's marker keeps nothing. The real `StartError` accepts B, creation 2, at the
  same id. B inherits no registry, starts H2, and is terminated by an operator. Its terminate reaches H2 only. H runs
  to its own end, and the real `TargetError` refuses H's notice to R's creation at B with `WrongCreation`.
  `successor-inherits` gives B R's registry, so B sends H a terminate. H's parent binding refuses it, because H's
  `ParentCreation` is R's.
- `rt1-successor-unreachable`: a start for creation 2 addressed to A's retired generated id arrives twice, as an
  outside add and as X's application send. The real `Message.SuppliedCreator` and the real `Delivery.DiscardedAt`
  classify both as supplied creators to discard. `DiscardedAt` runs over a real `Slot` holding A's marker, reached by
  reflection. The Worker's consumption of such a delivery, with no turn and no row change (Worker.cs 1030–1037), is
  mirrored. The marker and the obligation are unchanged, no creation 2 appears, and T then discharges the obligation.
  A control shows that the same start at the creator scope, which only `StartChild` (with a fresh id) and key
  admission mint, is classified as no supplied creator.
- `rt1-successor-gap`: a start for creation 2 reaches the start check directly. The real `StartError` accepts it.
  The repaired rule then refuses it with `ObligationOutstanding`, because A's marker still holds an obligation. This
  is the coordinator's defence-in-depth rule. In valid operation it never fires, as `rt1-successor-unreachable` and
  `rt1-successor` show. The marker stays, and T discharges the obligation. `successor-admitted` drops the rule: the
  successor replaces the marker, the real `TargetError` refuses T with `WrongCreation`, and G, G2 and J survive.

In every run of P's tree, J's late result reaches A's id and the real `TargetError` refuses it with
`RetiredCreation`. RT-1 runs assert, as each case needs:

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

- the proposed drain and inbox transitions: page, the gate, Complete, the cohort precedence and poll order, held rows
  skipped, and takeover by reloading durable rows only;
- the proposed retirement obligation, its cascade and the marker's next revision;
- #4756's open-drain refusal;
- the ObligationOutstanding start refusal.

They run against real prototype code:

- `NativeModel.TurnAsync` with its real timer, cancel, event-resume and composite-signal hold handlers;
- `CreationProtocol.TargetError`, `StartError`, `Retire`, `Origin` and `Fingerprint`;
- `Message.SuppliedCreator` and `Delivery.DiscardedAt`, over a real `Slot`;
- `ChildRegistry.Read` and `Write`;
- `WorkflowInstance.Finished`;
- `DurableKeyJson` job keys and `InboxKey.Ticket`.

Each report lists the real calls it made. The internal functions are called by reflection, so no prototype project
changed.

**Mirrored from the built code:**

- the built terminate of a live row (`WorkflowInstance.cs` 179–203);
- the child-notice path (`WorkflowInstance.cs` 36–61);
- the Worker's consumption of a discarded supplied creator (`Worker.cs` 1030–1037).

Each of these needs a real `Worker`: `Terminate` is private and takes a `TurnInput` that only the Worker builds. The
parent binding at a live child is the proposed rule of #4756.

**Left for implementation qualification:**

- the `Worker`: its poll, marks, trials and hold hand-back, commit batching and the fence;
- store providers: seq allocation on SQL and MongoDB, counted updates, unknown commit outcomes;
- real ownership: leases, takes and epochs;
- cross-partition transport;
- Park itself and the operator selection;
- a poll in flight across a page commit (R4-M4, E-9 and E-10);
- E-8 with the drain still open;
- a later Park turning undone cohort rows Stale;
- DT-7's delete that took over a discard;
- RM-1's size bound and its refusal of any other write to a marker.
