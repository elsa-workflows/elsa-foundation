# Review 4 blockers: FR-1 and RT-1

Executable evidence for the two Blockers of the fourth Astra High review of the kernel proposal
(elsa-workflows/elsa-foundation#4229): R4-B1, a final released row losing to a late arrival, and R4-B2, a delete
stranding a live subtree. Each case runs one schedule under three rule sets:

- `reviewed`: the rules of the reviewed design (`ea6596a85`);
- `repaired`: the repair of the fourth fix round;
- `negative`: the repaired rules with the one repaired rule removed.

`reviewed` and `negative` fail their invariant assertions, and `repaired` passes. Two extra mutations check the
repair's limits: `held-blocks` (FR-1) and `cascade-any` (RT-1).

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

## FR-1 (R4-B1): release cohort precedence

The workflow is `Parallel(Delay F 50, Delay F2 100)` on the real `NativeModel`. Its real start turn arms both
timers. R is F's timer at seq 1100. While the instance is parked, R arrives and is kept as `Arrived`. Recover opens
generation 1, and the first page, C0, is the final page. It restores R with `ReleasedBy (1,1)` and sends the next
drain control C at seq 950. A is an ordinary `native.cancel` (`NativeKinds.Cancel`) from sender `0/other` at seq 900.
It is withheld during the drain. A poll at the current epoch marks C and R seen. C finds no kept row past the cursor,
and its gate row R is taken up, so C completes the drain. The model then gets one input per turn, in the order the
rule set gives:

| Rule set | Order after Complete | Outcome |
|---|---|---|
| reviewed | plain tuple order: A, then R | A's real cancel turn ends the model and recalls R; F completes 0 times |
| repaired | control kinds, then the cohort (`ReleasedBy` = the creation's latest generation, not Done), then the rest | R's real timer turn completes F once, with F2 still waiting; then A cancels |
| negative | control kinds, then tuple order | as reviewed |

The variants:

- `takeover-before-complete`: the owner is lost after R is marked seen. The new owner, at the next epoch, keeps only
  the durable rows, re-polls, and C's gate counts R only at the new epoch.
- `takeover-after-complete`: the owner is lost after Complete commits and before R reaches a model turn. Precedence
  comes from R's durable `ReleasedBy` tag and the row's `ResolutionGeneration`, so it survives.
- `held`: the model holds the cohort row, through a real Native hold path. `HeldGate` is a harness-authored composite
  activity, like Incarnation.Run's `HeldReceiver`. It defers composite signals until its child `Event go` completes.
  Native's `FinishSignals` then calls `ITurn.Hold`. S is a composite signal to H, released as the cohort. A is
  `native.resume go`, the input S waits for. Under `repaired`, the poll skips the held S, so A is delivered and S is
  accepted afterwards. The `held-blocks` mutation lets a held cohort row block other rows, and the instance stalls.
  This is a real model path, so no stub model is used.

Every FR-1 run asserts:

- F's completion count;
- the exact sequence of model transitions after Complete;
- that F2 was still waiting when A arrived;
- that the instance ended;
- R's fate (consumed by a model turn, or recalled);
- that no undone rows remain.

## RT-1 (R4-B2): retirement keeps a bounded cancellation obligation

The tree is P → A → G → G2, with live G and G2 and job J on A (real `InstanceRow`s, real registry and job-key
JSON). A is in an unprotected parked-delete drain. An operator terminates P, and P's terminate sends the parent-bound
terminate T to A.

- `rt1-a`: T is committed while A exists and delivered after A's Complete. Complete sends the one Faulted notice
  through the real `WorkflowInstance.Finished` and retires A through the real `CreationProtocol.Retire`. The real
  `CreationProtocol.TargetError` refuses T with `RetiredCreation`.
  - `reviewed`: the marker keeps nothing, so G, G2 and J survive.
  - `repaired`: the marker keeps the recorded parent, the registry and the job keys. T passes the parent binding, so
    the refusing commit sends terminate to G, cancels J and clears the obligation. G's and G2's built terminates
    finish the tree.
  - `negative`: the marker drops the registry, so J is cancelled while G and G2 survive.
- `rt1-b`: T is delivered before Complete and refused as "resolution open", with the cascade of #4756. This passes
  under every rule set. It is the control showing that the reviewed rule covers this order.
- `rt1-nonparent`: X, not A's parent, sends a terminate to A's retired creation. It gets the plain refusal, with no
  cascade, and the obligation stays. `cascade-any` (cascade for any terminate at a marker) terminates G and fails.
- `rt1-successor`: the real `CreationProtocol.StartError` accepts creation 2 over the marker. The successor's own
  terminate never reaches creation 1's obligation.

Every RT-1 run asserts:

- which descendants end terminated;
- whether J is cancelled;
- which obligations remain on the marker;
- that P receives exactly one notice from A;
- the refusal reason.

### Open point: `rt1-successor-gap`

This case is the same as `rt1-a`, except that a successor creation of A's id starts before T arrives. The real
`TargetError` then refuses T with `WrongCreation`. Under the repair as written, the cascade runs only on a
`RetiredCreation` refusal at the retired marker, so G, G2 and J survive under every rule set. The design does not yet say where the
retained obligation lives once a successor creation replaces the marker row. The harness keeps it keyed by
`(id, creation)`, and nothing reaches it. This case reports the gap and is expected to fail. It is reachable only
when a parent's child can sit at an id that is reused. The prototype's generated physical addresses are never
recycled (Creation.cs, `IsPhysicalAddress`).

## What this proves, and what it does not

**Executed here:**

- the proposed drain and inbox transitions: page, the gate, Complete, the cohort precedence and poll order, held rows
  skipped, and takeover by reloading durable rows only;
- the proposed retirement obligation and its cascade, and #4756's open-drain refusal.

They run against real prototype code:

- `NativeModel.TurnAsync` with its real timer, cancel, event-resume and composite-signal hold handlers;
- `CreationProtocol.TargetError`, `StartError`, `Retire`, `Origin` and `Fingerprint`;
- `ChildRegistry.Read` and `Write`;
- `WorkflowInstance.Finished`;
- `DurableKeyJson` job keys and `InboxKey.Ticket`.

Each report lists the real calls it made. The internal functions are called by reflection, so no prototype project
changed.

**Mirrored from the built code:**

- the built terminate of a live row (`WorkflowInstance.cs` 179–203);
- the child-notice path (`WorkflowInstance.cs` 36–61).

`Terminate` is private, and its `TurnInput` needs a real `Worker`. The parent binding at a live child is the proposed
rule of #4756.

**Left for implementation qualification:**

- the `Worker`: its poll, marks, trials and hold hand-back, commit batching and the fence;
- store providers: seq allocation on SQL and MongoDB, counted updates, unknown commit outcomes;
- real ownership: leases, takes and epochs;
- cross-partition transport;
- Park itself and the operator selection;
- a poll in flight across a page commit (R4-M4);
- a later Park turning undone cohort rows Stale;
- a model end dropping a remaining cohort row.
