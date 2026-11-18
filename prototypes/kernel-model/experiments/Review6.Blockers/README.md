# Review 6 blockers: NT-1 and PI-1

Executable evidence for the two Blockers of the sixth Astra High review of the kernel proposal
(elsa-workflows/elsa-foundation#4229, [the review](https://github.com/elsa-workflows/elsa-foundation/issues/4229#issuecomment-6980291737)),
reviewed at `d49aad9cd`:

- **R6-B1 (NT-1):** an admitted Native parent can outgrow its envelope before it sends the first child terminate;
- **R6-B2 (PI-1):** a new outside publication can lose its delivery to the previous publication's receipt.

Each case runs one schedule under three rule sets:

- `reviewed`: the design at `d49aad9cd`;
- `repaired`: the sixth fix round's repair rules, as the coordinator's design brief states them;
- `negative`: the repaired rules with the case's one repaired rule removed:
  - NT-1: `TerminalGrowth(state)` is not counted in the envelope check;
  - PI-1: the incarnation is in neither the delivery id nor the match digest.

Mutations of the repaired set run where they bite:

- NT-1: `growth-undercount`, Native's bound at 6 bytes per open inbox instead of the measured 7;
- PI-1: `match-without-incarnation`, the incarnation in the delivery id only; `fresh-incarnation-on-duplicate`, a
  duplicate within the window gets a new incarnation and a match step;
- PI-1: `built-key`, which is no mutation. It is the existing prototype behaviour: the built delivery key through the
  built `Events.PublishAsync`.

The optional cases of the brief, MC-1 (R6-M1) and JK-1 (R6-M5), are not in this harness. On the owner's direction of
18 November the harness covers the two Blockers only, and those two findings become K3 cases on the pages.

The layout follows [Review5.Termination](../Review5.Termination/README.md) and
[Review5.Delivery](../Review5.Delivery/README.md), which are unchanged.

## Run

From `prototypes/kernel-model`:

```
dotnet restore experiments/Review6.Blockers --locked-mode
dotnet build experiments/Review6.Blockers -c Release --no-restore
BIN=experiments/Review6.Blockers/bin/Release/net10.0/Kernel.Review6.Blockers.dll
dotnet $BIN --list                                      # every case, rule set and expected outcome
dotnet $BIN --case nt1-witness --rules repaired         # one run: exit 0 when every assertion holds, 1 otherwise
dotnet $BIN --all                                       # every run, then a summary; exit 0 only if all match the expected matrix
experiments/Review6.Blockers/run.sh                     # every run one by one, then --all; exits with the matrix result (2 if setup fails)
```

`run.sh` reads its individual runs from `--list`, so they and `--all` run the same rows.
[results.txt](results.txt) holds the output of `--all` and the commit it ran at. The whole matrix takes about four
seconds.

## The run table

Expected outcome per case and rule set. Every run's actual outcome equals its expected one (see
[results.txt](results.txt)).

| Run | Backs | reviewed | repaired | negative | Mutations |
|---|---|---|---|---|---|
| `nt1-growth` | R6-B1, the per-item figure: Native's terminal growth of each inbox and receipt shape, measured with the real `TerminateMetadata` and end-path close | PASS | PASS | PASS | `growth-undercount` FAIL |
| `nt1-witness` | R6-B1, the reviewer's witness: a 16,436,864-byte state with 3,500 open empty inboxes, then a plain terminate | FAIL | PASS | FAIL | |
| `nt1-at-bound` | R6-B1, the largest state each rule set's check admits, then a plain terminate | FAIL | PASS | FAIL | `growth-undercount` FAIL |
| `nt1-thendelete-protected` | R6-B1, the same with a protected `ThenDelete`: one pending invocation input protects the row, so the ending commit is the live row's write | FAIL | PASS | FAIL | |
| `nt1-no-inboxes` | R6-B1 control: a Native state with no invocation inbox at the reviewed bound has no terminal growth | PASS | PASS | PASS | |
| `pi1-gap` | R6-B2, the message sweep runs in the gap before the second admission | PASS | PASS | PASS | `built-key` PASS |
| `pi1-event-first` | R6-B2, the reviewer's trace: the old event is swept, the second admission comes, then the message sweep | FAIL | PASS | FAIL | `built-key` FAIL, `match-without-incarnation` FAIL |
| `pi1-slow-consumer` | R6-B2, the slow consumer: the first delivery is still pending at the sweep | FAIL | PASS | FAIL | `built-key` FAIL |
| `pi1-duplicate` | R6-B2, a retry with another payload within the second publication's window | PASS | PASS | PASS | `fresh-incarnation-on-duplicate` FAIL |

34 runs. A case whose three rule sets all pass is a control. Where a mutation is listed, it shows that the repaired rule
is what makes the case pass.

## The kernel the cases run on

- **The store.** One real `InMemoryStore` with 4 partitions and a harness clock. Every partition has an owner that
  takes its fence through the real `TakeAsync`. Every write after setup is a `CommitBatch` through the real
  `CommitAsync` (`InMemoryStore.cs` 87–194), which checks the fence, counts every consumed input and refuses a send
  whose key exists. Setup seeds instance rows directly into the store. Outside messages enter through the real
  `AddAsync` (227–232).
- **Turns of the actor type are the real ones.** The terminate is the built `WorkflowInstance` terminate
  (`WorkflowInstance.cs` 179–203), reached through the real `Command` (31–93). A failed turn parks through the real
  `WorkflowInstance.Park` (120–132). Each outcome becomes a `TurnCommit` as `Worker.cs` 1450–1455 builds one.
- **The model is the real Native model.** `NativeModel.TerminateMetadata` (`NativeModel.cs` 171–179) runs inside the
  built terminate, and `NativeModel.ProtectsDeletion` (180–183) decides the `ThenDelete` protection.

## NT-1 (R6-B1): reserve the model's terminal growth

### The schedule

Q is P's parent, and G is P's one registered child. P is a running Native instance.

1. **P's growth turn.** P's turn writes a synthesized Native state. The harness composes this commit: the
   synthesized state stands for a model turn's result.
2. **The operator's terminate.** The terminate goes to P (`nt1-thendelete-protected` adds `ThenDelete`). The real
   terminate runs `TerminateMetadata`, which closes every open invocation inbox, and composes the terminate to G and
   the one notice to Q.
3. **G's terminate.** Any terminate stored for G is delivered through the real terminate.

### The synthesized state

The state follows the reviewer's witness (`structural-witness.log`):

- 3,500 frames, each with an open, empty invocation inbox. `RegisterInbox` admits each one
  (`InvocationInbox.cs` 162–172).
- Null-valued variables with 506-character labels.
- One padding string, sized to the exact byte.

`nt1-witness` uses the reviewer's size, 16,436,864 bytes. `nt1-at-bound` and `nt1-thendelete-protected` use the
largest state that the rule set's own check admits.

### The envelope check (proposed, held by the harness)

Before each commit of P the harness computes:

> state + `TerminalGrowth(state)` + registries at the marker's per-entry sizes + 16,384 ≤ 16,457,728

- **State** is the UTF-8 byte count of the row's `State`.
- **`TerminalGrowth(state)`** is counted under `repaired` only.
- **The registries** count 384 bytes per child and 12,416 per job. A terminate's commit counts its list, which is the
  previous row's registries.
- **The header** is 16,384 bytes. The row's other fields measure 366 bytes, inside it.

A commit above the envelope is refused as a definite error, and the turn fails and parks through the real `Park`, as an
over-envelope turn does. The `Fault` text counts against `ParkRecord`'s own reserve, so the Park commit is measured
without it.

### The assertions

- **Room for the terminate.** Every state P committed leaves room for its own terminate: the state, its real terminal
  growth, the one child and the header stay within 16,457,728. The growth is measured by running the real
  `TerminateMetadata` on a copy of each landed state.
- **The first child send.** The first counted child terminate is stored by a landed commit, and G ends Cancelled.
- **One notice.** Q hears exactly one notice from P.
- **Forensic state.** Except in `nt1-witness`: P ends Cancelled (and kept, for `ThenDelete`) with every invocation inbox
  closed.

### What the runs show

- **`reviewed`.** In `nt1-witness` the growth turn lands (16,453,632 ≤ 16,457,728). `TerminateMetadata` then grows the
  state by 24,500 bytes, to 16,461,364. The ending commit comes to 16,478,132 and is refused. P parks Faulted, keeps
  its registry and sends Q one Faulted notice. No terminate to G is ever stored, and G stays Running.
- **`repaired`.** The same growth turn is refused, because the check counts the 24,500 bytes. It parks with P's
  previous state. The operator's terminate then reaches the parked row. The built terminate of an ended row sends G its
  terminate, and G ends Cancelled.
  - **The harness's choice.** The proposed terminate drain at a parked run is unchanged by R6-B1. The harness uses the
    built terminate of an ended row (`WorkflowInstance.cs` 63–64) and does not model that drain.
- **At the bound.** In `nt1-at-bound` under `repaired`, the state admitted with 24,500 bytes reserved terminates to
  exactly 16,457,728. The ending commit lands with P Cancelled, every inbox closed, and the terminate to G stored and
  delivered.
- **The ThenDelete variant.** `nt1-thendelete-protected` is the same with a protected `ThenDelete`. One Queued input
  makes `ProtectsDeletion` true, so the ending commit is the live row's write. Its real growth is 24,475 against a
  bound of 24,523.
- **The negative and the mutation.** The negative and `growth-undercount` fail the same way as `reviewed`. Under
  `growth-undercount` the at-bound state is admitted 3,500 bytes too large.

### The measured per-item figure

Measured by `nt1-growth` with the real `NativeModel.TerminateMetadata`, which runs `InvocationLifetime.Terminate` at
`InvocationLifetime.cs` 42–45 and `Close` at 29–40, then `LexicalScopes.Terminate` at `LexicalScopes.cs` 28–34. The
end paths' close (`InvocationInbox.cs` 74–76) is reached through `InvocationLifetime.Terminate` by reflection.

| Shape | Growth (UTF-8) | Bound |
|---|---:|---:|
| One open inbox, empty (with or without retention) | +7 | 7 |
| One closed inbox | 0 | 0 |
| An open inbox with a Queued receipt and its pending input | −18 (gross +30 with the input kept) | 30 |
| An open inbox with a Selected receipt and its pending input | −21 (gross +27) | 30 |
| An open inbox with an Evaluating receipt and its pending input | −22 (gross +26) | 30 |
| An open inbox with a Completed receipt | +7 | 7 |
| A frame with a lexical scope and its resource | −89 | 0 |
| End path, closed as `completed` / `faulted` | +7 / +5 | 7 |
| 3,500 open empty inboxes and frames (the witness's structure) | +24,500 | 24,500 |

- **The per-item figure: 7 bytes per open invocation inbox.** `"Closed":null` becomes `"Closed":"cancelled"`, 4 bytes
  to 11. The `completed` reason costs the same, and `faulted` costs 5.
- **The bound.** Native's `TerminalGrowth(state)` (proposed) counts 7 bytes per open inbox and 23 bytes per non-final
  receipt. The 23 is the receipt's gross growth before the close clears its pending input: Status +6, Outcome +7,
  Clock +10. Frames count 0, because the lexical-scope close only removes fields.
- **The gross figures.** They were measured with `AdmissionFlaw.KeepPayload`, the model's flaw that keeps pending
  inputs. Under the built behaviour every receipt shape has a negative net growth.
- **SQL Server.** Its UTF-16 doubles each figure (derived, unmeasured).

## PI-1 (R6-B2): bind deliveries to a publication incarnation

### The schedule

W is I's wait on key K. It is registered once, through the real `SubscribeAsync`, and never unsubscribed. The window
is 1,000 ms.

1. **The first admission.** At t = 1000, (K, E) is published with payload `{"n":1}`.
2. **The first delivery.** W's turn consumes the delivery through the real commit, so the row is kept Done.
   `pi1-slow-consumer` leaves it pending.
3. **The sweep.** At t = 2001 the window has passed:
   - `pi1-gap`, `pi1-duplicate` and `pi1-slow-consumer`: the built `SweepAsync` (`InMemoryStore.cs` 348–359) removes
     the expired event row and Done rows in one call;
   - `pi1-event-first`: the event-sweep batch runs alone, and the message-sweep batch runs after the second
     admission.
4. **The second admission.** At t = 2050, (K, E) is published again with payload `{"n":2}`. `pi1-duplicate` then
   retries it within the new window with payload `{"n":3}`.
5. **The catch-up.** A later catch-up page of W runs through the real `KeptMatchesAsync` (287–298), as a keeping
   wait's replay reads it.
6. **The last delivery.** W's turn consumes whatever is pending.

### Real calls and harness-held rules

- **The publish.** It uses the real event insert, `PublishAsync` (263–267), a no-op when (K, E) exists. The match is the
  real `PublishedMatchesAsync` (281–285), which builds routes from the stored row. The envelope is the built one
  (`EventRoute.Delivery`, `Store.cs` 221–222 over `Events.Deliver`, 247–251). The delivery insert is the real
  `AddEventAsync` (235–246), which refuses an existing full key. Under `built-key` the whole publish is the built
  `Events.PublishAsync` (287–296).
- **The full key (proposed, held by the harness).** The delivery id and `Match` replace the built ones.
  - `reviewed`: R4-M1's digests (observability, "The event match is a fixed digest"):
    - the id is `ev:` and the SHA-256 of (event id, instance, creation, wait);
    - `Match` is the SHA-256 of (key, event id).
  - `repaired`: version 2 of the same encodings with the 16-byte incarnation after the event id:
    - the id is `ev:` and the SHA-256 of (event id, incarnation, instance, creation, wait);
    - `Match` is the SHA-256 of (key, event id, incarnation).
  - The prototype has no tenant, so the key stands for Q(t, key). The envelope's constructor is internal, so the harness
    builds it by reflection.
- **The incarnation (proposed, held by the harness).** It is minted when the real insert added the row, which the
  harness tells by reference identity of the stored row. A duplicate gets the existing row's incarnation, writes
  nothing and runs no match step. The receipt names the incarnation. The store-generated id is stood in for by a
  16-byte serial, so the output is deterministic.
- **The match step runs inline.** It runs as `Store.cs` 290–294 runs it. The proposed publish ticket moves it to the
  owner and does not change the key.
- **The sweep batches (proposed batching).**
  - The event-sweep batch uses the built predicate `KeepUntil < now` (`InMemoryStore.cs` 354).
  - The message-sweep batch uses the built predicate (353) and round 5's rule: a Done delivery goes only when no event
    row has its `Match` (observability, "Done deliveries are kept with their events").

### The assertions

- **One delivery per publication.** W receives `[{"n":1}, {"n":2}]`, each accepted publication once with its own
  payload.
- **The second receipt.** The second admission's receipt is New.
- **The old Done row (`pi1-event-first`).** After the message-sweep batch, the first publication's Done delivery is
  gone, since its own event row is gone.
- **The duplicate (`pi1-duplicate`).** The retry is answered as a duplicate naming the second publication's identity
  and adds nothing.

### What the runs show

- **`reviewed`, `pi1-event-first`.** This is the reviewer's trace. The second admission's delivery derives the old
  full key, so `AddEventAsync` refuses it. The new event row has the old `Match`, so it then protects the old Done row
  from the message sweep. The catch-up meets the same key. W receives `{"n":1}` only.
- **`reviewed`, `pi1-gap`.** The message sweep ran in the gap, and W receives both payloads.
- **`pi1-slow-consumer`.** The pending first delivery takes the key under `reviewed`, `negative` and `built-key`.
- **The built code reproduces it.** `pi1-slow-consumer` under `built-key` runs end to end on the built code (the built
  `Events.PublishAsync`, `SweepAsync` and `AddEventAsync`) and loses the second publication.
- **`repaired`.** The delivery id and `Match` differ per incarnation, so the second delivery is added in every order.
  The old Done row's `Match` has no event row, so the message-sweep batch removes it. The catch-up meets the second
  delivery's own key and adds nothing.
- **The mutations.** `match-without-incarnation` still delivers, since the store's full key includes the id, but the
  old Done row outlives its event: the new event's equal `Match` keeps it. `fresh-incarnation-on-duplicate` delivers
  `{"n":2}` twice and names a third incarnation in the duplicate's receipt.

### Sizes

The incarnation is fixed-width, 16 bytes, stored in the event row and counted inside `C`. The delivery id stays
`ev:` and 64 hex characters (67 bytes), and `Match` stays 64 hex characters, since both are digests. This changes
R4-M1's delivery-id formula: the encoding's version goes to 2 and the incarnation joins its parts.

## Executed and proposed

| Part | Label |
|---|---|
| NT-1: Native terminate, `TerminateMetadata`, `InvocationLifetime` close, `ProtectsDeletion`, `Park`, the commits | executed in this repair, real prototype code |
| NT-1: the envelope check, `TerminalGrowth(state)` and Native's bound | proposed, held by the harness |
| NT-1: the growth turn's state | synthesized (no authored workflow drives it), written by a harness-composed commit |
| NT-1: `ThenDelete`'s protected-row decision | proposed semantics, the real `ProtectsDeletion` |
| PI-1: event insert, match read, catch-up read, delivery insert, consumer commit, built sweep | executed in this repair, real prototype code |
| PI-1: the digests, the incarnation and its receipt, the sweep batches with round 5's predicate | proposed, held by the harness |
| PI-1: `built-key` | existing prototype behaviour (`Store.cs` 287–296, `InMemoryStore.cs` 235–246, 348–359) |

Existing prototype behaviour that the cases rest on:

- `WorkflowInstance.cs` 183–190: the terminate writes the model's `TerminateMetadata` state.
- `NativeModel.cs` 171–178: `TerminateMetadata` closes every inbox and retires lexical scopes.
- `InvocationLifetime.cs` 29–45: `Close` and `Terminate`.
- `InvocationInbox.cs` 162–171: `RegisterInbox` admits each configuration.
- `Store.cs` 287–315: the built publish and catch-up.
- `InMemoryStore.cs` 263–299 and 348–356: the event insert, matches and sweep.

## What this proves, and what it does not

- **NT-1.** The reviewed check admits a Native state whose ending commit cannot fit, so the first child terminate is
  never sent. Counting a measured `TerminalGrowth` at every commit keeps every landed state terminable, at the exact
  boundary, for a plain terminate and a protected `ThenDelete`.
  - **Not covered.** It does not measure a provider's stored encoding: the 366 bytes of other fields and the UTF-16
    doubling are qualified at K4. It does not drive the state through authored workflow turns (K6), and it does not
    exercise the paged cascade beyond one child, which `Review5.Termination` covers.
- **PI-1.** A key derived from logical names alone lets a previous publication's surviving delivery row suppress a new
  admission. The built prototype shows this with a slow consumer, and the round-5 batched sweep shows it with a Done
  row. Binding the delivery id and `Match` to a store-generated incarnation delivers each admission once in both sweep
  orders, keeps duplicates within the window single, and lets the old row retire with its own event.
  - **Not covered.** It does not exercise provider-equal spellings, which need a provider collation, or a crash in the
    gap between the two sweep batches. The crash leaves the `pi1-event-first` state, but no crash is executed. It does
    not exercise the owner-side publish ticket. Those are K3/K4 cases on the pages.
- **The store boundary.** No run measures a database: the in-memory store has the semantics the SQL is meant to have.
