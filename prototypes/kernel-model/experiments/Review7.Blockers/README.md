# Review 7 blockers: NT-2 and MX-1

Executable evidence for the Blocker and the Major of the seventh Astra High review of the kernel proposal
(elsa-workflows/elsa-foundation#4229, [the review](https://github.com/elsa-workflows/elsa-foundation/issues/4229#issuecomment-6987785355)),
reviewed at `cfce7a2c1`:

- **R7-B1 (NT-2):** the terminal projection omits the retained creator handles, so an admitted parent can still fail
  the ending commit that sends its first child terminate;
- **R7-M1 (MX-1):** a committed turn publication and a later outside publication can claim one event row.

Each case runs one schedule under three rule sets:

- `reviewed`: the design at `cfce7a2c1`;
- `repaired`: the seventh fix round's repair rules, as the coordinator's design brief states them;
- `negative`: the repaired rules with the case's one repaired rule removed:
  - NT-2: `CustomHandles` is not counted in the projection, which is the sixth round's formula, so `negative` equals
    `reviewed` here;
  - MX-1: the prefix without the refusal.

MX-1 also runs the mutation `refusal-without-prefix`, the other half of R7-M1's rule removed.

The layout follows [Review6.Blockers](../Review6.Blockers/README.md), which is unchanged.

## Run

From `prototypes/kernel-model`:

```
dotnet restore experiments/Review7.Blockers --locked-mode
dotnet build experiments/Review7.Blockers -c Release --no-restore
BIN=experiments/Review7.Blockers/bin/Release/net10.0/Kernel.Review7.Blockers.dll
dotnet $BIN --list                                        # every case, rule set and expected outcome (the failed assertion ids)
dotnet $BIN --case nt2-handles --rules repaired           # one listed run: exit 0 when every assertion holds, 1 otherwise, 2 for an unlisted pair
dotnet $BIN --all                                         # every run, then a summary; exit 0 only if all match the expected matrix
experiments/Review7.Blockers/run.sh                       # every run one by one, then --all; exits with the matrix result (2 if setup fails)
```

`run.sh` reads its individual runs from `--list`, so they and `--all` run the same rows. Each matrix row states the
ids of the assertions it expects to fail, and `--all` compares each run's actual set of failed ids with that set, so an
unrelated failure is a mismatch. A `--case`/`--rules` pair that the matrix does not list is refused with a usage error.
[results.txt](results.txt) holds the output of `--all` and the commit it ran at. The whole matrix takes a few seconds.

## The run table

Expected outcome per case and rule set. Every run's actual outcome equals its expected one (see
[results.txt](results.txt)).

| Run | Backs | reviewed | repaired | negative | `refusal-without-prefix` |
|---|---|---|---|---|---|
| `nt2-handles` | R7-B1, the reviewer's trace: 1,500 detached starts in three commits of 500, then the 16,416,460-byte boundary state with 3,500 open inboxes, then a plain terminate | FAIL | PASS | FAIL | |
| `nt2-at-bound` | R7-B1, the same handles, then the largest state each rule set's check admits, then a plain terminate | FAIL | PASS | FAIL | |
| `nt2-thendelete-protected` | R7-B1, the same with a protected `ThenDelete`: one pending invocation input protects the row, so the ending commit is the live row's write | FAIL | PASS | FAIL | |
| `nt2-no-handles` | R7-B1 control: no detached start, the largest state each rule set admits | PASS | PASS | PASS | |
| `mx1-ticket-first` | R7-M1, the reviewer's trace: the turn's ticket commits, then an outside publish under the returned id before the ticket's first step | FAIL | PASS | FAIL | FAIL |
| `mx1-outside-first` | R7-M1, the outside publish comes first, under the id the turn will return | FAIL | PASS | FAIL | FAIL |
| `mx1-owner-change` | R7-M1, `ticket-first` with an owner change between the ticket's commit and its first step | FAIL | PASS | FAIL | FAIL |
| `mx1-distinct` | R7-M1 control: the outside publish uses its own id | PASS | PASS | PASS | PASS |

28 runs. A case whose rule sets all pass is a control.

## The kernel the cases run on

- **The store.** One real `InMemoryStore` with 4 partitions and a harness clock. Every partition has an owner, a real
  `Worker` that takes its fence through its own `TakeAsync` (`Worker.cs` 411–430), so its turns get that epoch's
  child issuer. The worker's monotonic clock (`WorkerOptions.Monotonic`, `Worker.cs` 48) is the harness clock, so every
  run is deterministic. Setup seeds instance rows directly into the store. Outside messages enter through the real
  `AddAsync` (`InMemoryStore.cs` 227–232). Every write after setup is a `CommitBatch` through the real `CommitAsync`
  (87–194), which checks the fence, counts every consumed input and refuses a send whose key exists.
- **Turns of the actor type are the real ones.** The terminate is the built `WorkflowInstance` terminate
  (`WorkflowInstance.cs` 179–203), reached through the real `Command` (31–93). A failed turn parks through the real
  `WorkflowInstance.Park` (116–132). Each outcome becomes a `TurnCommit` as `Worker.cs` 1450–1455 builds one.
- **The models.** P in NT-2 is a real Native row. `NativeModel.TerminateMetadata` (`NativeModel.cs` 171–178) runs
  inside the built terminate, and `NativeModel.ProtectsDeletion` (180–183) decides the `ThenDelete` protection.

## NT-2 (R7-B1): the projection counts every retained field

### The schedule

Q is P's parent, and G is P's one registered child. P is a running Native instance with creation 1.

1. **P's three spawn turns.** Each is a real model turn through `WorkflowInstance.TurnAsync` (`WorkflowInstance.cs`
   95–112) and the real `Turn` (`Turn.cs`). The harness model `Spawner` (`Kernel.cs`) stands for an authored model's
   detached starts: it keeps P's Native state as it is and calls the real `ITurn.Start` with `detached: true` and
   `Placement.Spread` 500 times. `nt2-no-handles` runs no spawn turn.
2. **P's growth turn.** P's turn writes a synthesized Native state. The harness composes this commit: the synthesized
   state stands for a model turn's result, and the row's other fields, the handles included, are kept.
3. **The operator's terminate.** The terminate goes to P (`nt2-thendelete-protected` adds `ThenDelete`). The real
   terminate runs `TerminateMetadata`, which closes every open invocation inbox, and composes the terminate to G and
   the one notice to Q.
4. **G's terminate.** Any terminate stored for G is delivered through the real terminate.

### How the handles accumulate (existing prototype behaviour)

- `Turn.Start` (`Turn.cs` 53–61) calls `DurableTurn.StartChild` with no parent for a detached start and adds the child
  to the registry only when it is not detached (61).
- `DurableTurn.StartChild` (`Turns.cs` 364–376) sends the start under the creator handle
  `{Origin}#{Revision}.{ordinal:D4}` in the child-creator scope (370–371).
- `DurableTurn.Seal` (`Turns.cs` 490–498) adds every sent message in that scope to the registry of composed handles and
  removes a handle only when this actor consumes or cancels its row. A child's consumption of its start, on its own
  partition, removes nothing. The row's `CustomHandles` (`Store.cs` 14) is written at 498 and read back at 253.
- The terminate writes `row with { … }` (`WorkflowInstance.cs` 183), so the ending row keeps `CustomHandles`.

After the three spawn commits P's stored row holds 1,500 handles, 27,001 bytes, and its registry still lists only G.

### The synthesized state

The state is the reviewer's boundary structure, built as Review6.Blockers builds it:

- 3,500 frames, each with an open, empty invocation inbox;
- null-valued variables with 506-character labels;
- one padding string, sized to the exact byte.

`nt2-handles` uses the reviewer's figure, 16,416,460 bytes, the largest state the sixth round's formula admits with one
child. `nt2-at-bound`, `nt2-thendelete-protected` and `nt2-no-handles` use the largest state that the rule set's own
check admits.

### The rules held by the harness (proposed)

- **The admission projection.** Every commit of a live row (one that is `Running` after the commit) is checked first:

  > state + `TerminalGrowth(state)` + registries at the marker's per-entry sizes + `CustomHandles` + 16,384 ≤ 16,457,728

  - **State** is the UTF-8 byte count of the row's `State`.
  - **`TerminalGrowth(state)`** is the sixth round's Native bound, 7 bytes per open inbox and 23 per non-final receipt,
    measured by Review6.Blockers `nt1-growth`. It is counted in every rule set.
  - **The registries** count 384 bytes per child and 12,416 per job.
  - **`CustomHandles`** is counted at its encoded size, the UTF-8 byte count of its JSON text, under `repaired`, and as
    0 under `reviewed` and `negative`. It is the one variable-length field of the `Instances` row
    (`storage-model.md` 44 and 124) beyond the state and the registries.
  - **The reserve** is 16,384 bytes. The fixed fields measure 366 bytes in every run, 398 with a Park's `Fault`.

  A turn over the projection fails and parks through the real `Park`, as an over-envelope turn does.
- **The row cap.** Every commit, in every rule set, meets the row cap: the store refuses as a definite error a row
  that measures above 16,457,728 bytes. The harness sets it as the real store's `Refuses` hook (`InMemoryStore.cs` 49),
  which `CommitAsync` checks for each row before it writes anything (110). A definite refusal fails the turn, and the
  turn parks. The ending commit writes an ended row, so the row cap applies to it and the projection does not.
- **The measure.** The harness measures a row as the JSON UTF-8 size of its fields: its four JSON text columns
  (`State`, `Jobs`, `Children`, `CustomHandles`) at their UTF-8 size, and every other field as one JSON object in UTF-8
  (`Nt2.Measured`). The provider's stored encoding is qualified at K4: MongoDB's BSON, and SQL Server's UTF-16, which
  doubles JSON text. The reviewer's offline BSON measurement of the same ending row gave an excess of 10,448 bytes
  (`MongoStore.cs` 612–618); the JSON UTF-8 measure gives 10,599.

### The assertions

Each assertion has an id, shown in brackets in the output.

- **The handles** (`handles-accumulated`). After the spawn commits, P's stored row holds 1,500 creator handles (0 in
  `nt2-no-handles`) and its registry lists only G.
- **The growth turn parks** (`growth-parked`, `nt2-handles` only). The stored row after the growth turn is Faulted
  with its pre-growth state.
- **Room for the terminate** (`room-for-terminate`). Every row P committed leaves room for its own terminate's ending
  row: the real terminate, run on a copy of each landed row, writes a row that measures at most 16,457,728 bytes.
- **The exact boundary** (`at-cap`, `nt2-at-bound` under `repaired` only). The growth commit is admitted with a
  projected total of exactly 16,457,728, the ending row also projects to exactly 16,457,728, and the ending commit
  lands with a measured row within the cap.
- **The first child send** (`child-terminate`). The first counted child terminate is stored by a landed commit, and G
  ends Cancelled.
- **One notice** (`one-notice`). Exactly one notice from P to Q is stored.
- **Forensic state** (`inboxes-kept-closed`). P's terminal row keeps every inbox of its pre-terminate row, and each is
  closed.
- **P's end** (`p-cancelled`, except in `nt2-handles`). P ends Cancelled, and kept for `ThenDelete`.

### What the runs show

| Run | Growth commit | Ending row, measured | Outcome |
|---|---|---|---|
| `nt2-handles`, `reviewed` and `negative` | projected 16,457,728, lands (row 16,443,865) | 16,468,327, refused by the row cap (10,599 over) | P parks Faulted with its inboxes open; no terminate to G; G Running |
| `nt2-handles`, `repaired` | projected 16,484,729, refused; P parks with its 140-byte state | 27,539 (the parked row's terminate), lands | the terminate to G is stored; G Cancelled |
| `nt2-at-bound`, `reviewed` and `negative` | as `nt2-handles` | as `nt2-handles` | as `nt2-handles` |
| `nt2-at-bound`, `repaired` | state 16,389,459, projected exactly 16,457,728, lands | 16,441,326, lands (margin 16,402) | P Cancelled, every inbox closed; G Cancelled |
| `nt2-thendelete-protected`, `reviewed` and `negative` | state 16,416,437, projected 16,457,728, lands | 16,468,279, refused (10,551 over) | P parks Faulted; G Running |
| `nt2-thendelete-protected`, `repaired` | state 16,389,436, projected 16,457,728, lands | 16,441,278, lands; its projection has a margin of 48 | P Cancelled and kept; G Cancelled |
| `nt2-no-handles`, every rule set | state 16,416,460, projected 16,457,728, lands | 16,441,326, lands | P Cancelled; G Cancelled |

- **`reviewed`.** The growth commit fits both the sixth round's formula and the row cap. The terminate's ending row
  then holds the 24,500-byte terminal growth and the 27,001 bytes of handles, and the row cap refuses it. P parks
  through the real `Park`, which keeps the handles and stores the Faulted notice to Q, and no terminate to G is
  stored on this schedule.
- **`repaired`.** In `nt2-handles` the projection counts the handles and refuses the growth turn, which parks with
  P's previous state. The operator's terminate then reaches the parked row, and the built terminate of an ended row
  (`WorkflowInstance.cs` 63–64) sends G its terminate. In `nt2-at-bound` the projection admits a state 27,001 bytes
  smaller, and the live terminate's ending commit lands.
- **The control.** With no handles the sixth round's formula is sufficient: every rule set lands the ending commit.

## MX-1 (R7-M1): workflow-issued and caller-issued event ids are disjoint

### The schedule

P and W are instances of one tenant, each with creation 1. W waits on `acme_topic`, registered once through the real
`SubscribeAsync`.

- **The turn's publish.** P's turn is a real `DurableTurn` (`Turns.cs` 249–260) over a stored trigger. It calls the
  real `DurableTurn.Publish` (429–434) with payload `from-turn`, which returns `c8_0/acme_p_1#2.0000` and stages the
  publish ticket, and is sealed by the real `DurableTurn.Seal` (481–508). Its commit lands through the real
  `CommitAsync` with the ticket.
- **The outside publish.** The real `Events.PublishAsync` (`Store.cs` 288–296) with payload `from-outside`: the real
  `InMemoryStore.PublishAsync` (263–267), a no-op when (key, id) exists, then `PublishedMatchesAsync` (281–285), which
  routes from the stored row, then `AddEventAsync` (235–246) for each route.
- **The ticket's first step.** As `Worker.cs` 1323–1325 runs it: the real `Events.PublishAsync` of the ticket's
  `EventRow`. Then a commit consumes the ticket, as `MeetAsync` enqueues it (1339).
- **W's turn.** W's pending deliveries are committed at store level through the real commit, so the rows are kept
  Done. No workflow turn runs.

The runs order these steps:

- `mx1-ticket-first`: the turn's commit, the outside publish under the returned id, the ticket's first step;
- `mx1-outside-first`: the outside publish under the id the turn will return, the turn's commit, the first step. The
  caller learns that id from a real `DurableTurn.Publish` on P's current row, in a turn that is never sealed or
  committed;
- `mx1-owner-change`: the turn's commit; P's lease runs out and a new node takes partition 0 at epoch 2 through the
  real `Worker.TakeAsync`; the outside publish under the returned id; the new owner's first step;
- `mx1-distinct`: as `ticket-first`, with the outside id `source-42`.

### The rules held by the harness (proposed, owner decision of 18 November)

- **The prefix.** A turn's event id is stored and returned with the reserved first code unit `~`. The harness writes
  `~` before the id `DurableTurn.Publish` returned, and into the sealed ticket's `EventRow`; the ticket's own id and
  key are unchanged.
- **The refusal.** The outside publish refuses a caller event id whose first code unit is `~` with `InvalidEventId`.
  Nothing is written, no receipt is issued, and the call is not retryable.
- **The delivery key** is the built one (`Store.cs` 247–251). The conflict is at the event row, keyed by
  (key, id), where the sixth round's incarnation does not reach: a second insert finds the row and writes nothing.
- **Size.** The prefix adds one code unit to a turn's event id, one byte in UTF-8 here. SQL Server's two bytes and
  EM-4's figures are measured at K4.

`~` and the key encodings. `DurableKeyJson` already treats a leading `~` as a tag when it writes a JSON property
name (`DurableKeyJson.cs` 17–25): such a name is written as `~` and the base64 of its code units, and read back
exactly, so the encoding stays injective. Event ids are written in value position (`Store.cs` 35–37 and 230–235, the
`EventRow` and `EventMatch` converters), where `~` has no meaning, and no prototype path writes an event id as a
property name. The harness records this; the choice of the reserved code unit is the coordinator's.

### The assertions

- **The refusal** (`outside-refused`, all but `mx1-distinct`). The outside publish under the turn's id is answered
  `InvalidEventId` and writes nothing: the event rows and message rows are unchanged by it.
- **The control's outside publication** (`outside-accepted`, `mx1-distinct`). The outside publish is accepted as New,
  and its event row carries `from-outside`.
- **The turn's event** (`turn-event`). The event row under the turn's id carries `from-turn`.
- **What W receives** (`payloads`). W receives `[from-turn]`, or `[from-outside, from-turn]` in `mx1-distinct`.

### What the runs show

- **`reviewed`.** In `ticket-first` and `owner-change` the outside publish finds the (key, id) row empty and inserts
  `from-outside`. The ticket's first step then finds the row occupied: its insert writes nothing, the route is read
  from the stored row, and the delivery key exists. One event row is stored, and W receives `from-outside` only.
  `outside-first` ends the same way.
- **`repaired`.** The turn's id starts with `~`, so the outside publish under it is refused and writes nothing, in
  either order and across the owner change. The first step inserts the turn's row, and W receives `from-turn`.
- **The negative and the mutation.** With the prefix and no refusal, the caller's `~` id is accepted and occupies the
  row. With the refusal and no prefix, the turn's id has no `~`, the refusal does not apply, and the caller's id
  occupies the row. Both fail as `reviewed` does.
- **The control.** A distinct outside id gives two event rows, and W receives both payloads in every rule set.

## Executed and proposed

| Part | Label |
|---|---|
| NT-2: `Turn.Start`, `DurableTurn.StartChild` and `Seal` with the handles, Native terminate, `TerminateMetadata`, `ProtectsDeletion`, `Park`, the commits | executed in this repair, real prototype code |
| NT-2: the spawn turns' model | harness model (`Spawner`) standing for an authored model's detached starts |
| NT-2: the projection with `CustomHandles`, the row cap and its measure, Native's `TerminalGrowth` bound | proposed, held by the harness |
| NT-2: the growth turn's state | synthesized (no authored workflow drives it), written by a harness-composed commit |
| NT-2: `ThenDelete`'s protected-row decision | proposed semantics, the real `ProtectsDeletion` |
| MX-1: `DurableTurn.Publish` and `Seal`, the commits, `Events.PublishAsync` with the store's insert, match read and delivery insert, `Worker.TakeAsync` | executed in this repair, real prototype code |
| MX-1: the `~` prefix and the `InvalidEventId` refusal | proposed, held by the harness (owner decision, 18 November) |

Existing prototype behaviour that the cases rest on:

- `Turn.cs` 53–61: a detached start adds no registry entry.
- `Turns.cs` 364–376 and 490–498: a creator handle is kept in `CustomHandles` until this actor consumes or cancels it.
- `WorkflowInstance.cs` 179–203: the terminate keeps the row's other fields and writes the `TerminateMetadata` state.
- `Turns.cs` 429–434: a turn's event id is `{Origin}#{Revision}.{ordinal:D4}`, returned at acceptance.
- `Worker.cs` 1323–1325: the owner inserts a turn's event after the commit that sent the ticket.
- `InMemoryStore.cs` 263–267: the event insert is a no-op when (key, id) exists.

## What this proves, and what it does not

- **NT-2.** Retained creator handles make a parent's ending row larger than the sixth round's formula projects, so a
  growth commit that formula admits leaves an ending commit over the row cap, and the first child terminate is not
  sent on this schedule. Counting `CustomHandles` at its encoded size in the projection of every live commit keeps
  every landed row terminable, at the exact boundary, for a plain terminate and a protected `ThenDelete`.
  - **Not covered.** It does not measure a provider's stored encoding: BSON, UTF-16, revision and clock growth and
    upgrades are qualified at K4 (C1). It does not drive the handles or the state through authored workflow turns
    (K6), and it does not page a cascade beyond one child.
- **MX-1.** Without disjoint identities an outside publication under a turn's id occupies the turn's event row before
  the turn's insert, in either order and across an owner change, and W receives the outside payload only. The `~`
  prefix with the refusal keeps the two producers apart, and the turn's publication is delivered.
  - **Not covered.** It does not exercise a provider's key encoding of `~`, a random or provider-generated id, an
    outside retry, post-sweep reuse or interprocess concurrency. Those are K3/K4 cases on the pages.
- **The store boundary.** No run measures a database: the in-memory store has the semantics the SQL is meant to have.
