# Review 5 termination: RT-2, RT-3 and RT-4

Executable evidence for the termination findings of the fifth Astra High review of the kernel proposal
(elsa-workflows/elsa-foundation#4229, [the review](https://github.com/elsa-workflows/elsa-foundation/issues/4229#issuecomment-6973554094)):

- **R5-B1:** a settled job ticket rolls back the marker's cascade (RT-2);
- **R5-B2:** a valid registry needs more sends than one commit may carry (RT-3);
- **R5-M5:** a running recipient has no parent binding (RT-4);
- **R5-m3:** RM-1's foreign-job mutation, with jobs that carry generations (RT-2).

Every write in every run is a `CommitBatch` handed to the real prototype `InMemoryStore.CommitAsync`
([InMemoryStore.cs](../../src/Kernel.Durable/InMemoryStore.cs) 87–194). That commit checks the partition fence and
counts every consumed input: a consume of a row that is missing or done raises
`StoreException: consumed message … is not there` (116–117), and a send whose key exists raises "exists already"
(118–119). So the reviewed rule reproduces the reviewer's store witness through the same commit, and the repaired rule
lands through it.

Each case runs one schedule under three rule sets:

- `reviewed`: the design at `0a1bb5e0c`;
- `repaired`: the fifth fix round's repair rules, as the coordinator's design brief states them;
- `negative`: the repaired rules with the case's one repaired rule put back as reviewed:
  - RT-2: the counted cancel;
  - RT-3: the durable cursor (the cursor lives in the owner's memory);
  - RT-4: the parent binding at running and ended recipients.

Mutations of the repaired set run where they bite:

- RT-2: `presence-from-memory`, `foreign-derivation`, `generation-blind` and `accept-off-registry`;
- RT-3: `page-above-cap`, `no-next-control` and `delete-drops-discharging`;
- RT-4: `binding-running-only`.

## Run

From `prototypes/kernel-model`:

```
dotnet restore experiments/Review5.Termination --locked-mode
dotnet build experiments/Review5.Termination -c Release --no-restore
BIN=experiments/Review5.Termination/bin/Release/net10.0/Kernel.Review5.Termination.dll
dotnet $BIN --list                                   # every case, rule set and expected outcome
dotnet $BIN --case rt2-absent --rules repaired       # one run: exit 0 when every assertion holds, 1 otherwise
dotnet $BIN --all                                    # every run, then a summary; exit 0 only if all match the expected matrix
dotnet $BIN --p-cascade 500 --case rt3-live-p --rules repaired   # P_cascade is configurable; the matrix is stated for 1,000
experiments/Review5.Termination/run.sh               # every run one by one, then --all; exits with the matrix result (2 if setup fails)
```

[results.txt](results.txt) holds the output of `--all` and the commit it ran at. The whole matrix takes about five
seconds.

## The run table

Expected outcome per case and rule set. Every run's actual outcome equals its expected one (see
[results.txt](results.txt)).

| Run | Backs | reviewed | repaired | negative | Mutations |
|---|---|---|---|---|---|
| `rt2-witness` | R5-B1, the reviewer's store witness replayed as built | FAIL | PASS | FAIL | |
| `rt2-pending` | R5-B1, ticket pending | PASS | PASS | PASS | |
| `rt2-done-kept` | R5-B1, ticket done and kept | FAIL | PASS | FAIL | |
| `rt2-absent` | R5-B1, the reviewer's trace: a fresh owner settles J before T arrives | FAIL | PASS | FAIL | `presence-from-memory` PASS |
| `rt2-mixed` | R5-B1, one pending, one absent and one done-and-kept ticket | FAIL | PASS | FAIL | |
| `rt2-settle-after` | R5-B1, the other arrival order: T before the settle | PASS | PASS | PASS | |
| `rt2-duplicate` | R5-B1, duplicate parent delivery after an unknown outcome | FAIL | PASS | FAIL | |
| `rt2-takeover` | R5-B1, a takeover between the settle and T | FAIL | PASS | FAIL | `presence-from-memory` FAIL |
| `rt2-open-drain-mixed` | R5-B1 on the open-drain refusal path (J-1) | FAIL | PASS | FAIL | |
| `rt2-live-pending` | R5-B1 on the live-actor terminate (`WorkflowInstance.cs` 196–198) | PASS | PASS | PASS | |
| `rt2-generations` | R5-m3, a newer same-origin generation and another instance's ticket | PASS | PASS | PASS | `foreign-derivation` FAIL, `generation-blind` FAIL |
| `rt2-off-registry` | R5-m3, retirement refuses a pair the registry does not hold | PASS | PASS | PASS | `accept-off-registry` FAIL |
| `rt3-live-p` | R5-B2, exactly `P_cascade` (1,000) children, live actor | PASS | PASS | PASS | |
| `rt3-live-p-plus-1` | R5-B2, `P_cascade` + 1 children, live actor | PASS | PASS | PASS | |
| `rt3-live-10001` | R5-B2, 10,001 children, live actor | FAIL | PASS | PASS | `page-above-cap` FAIL |
| `rt3-marker-10001` | R5-B2, the reviewer's trace: 10,001 children at the retirement marker | FAIL | PASS | PASS | `page-above-cap` FAIL |
| `rt3-open-drain-10001` | R5-B2 on the open-drain refusal path, then Complete while Discharging | FAIL | PASS | PASS | |
| `rt3-jobs-marker` | R5-B1 and R5-B2 together: 999 children and 2 jobs (one absent) at the marker, two pages | FAIL | PASS | PASS | |
| `rt3-takeover` | R5-B2, a takeover after page 3 | FAIL | PASS | FAIL | `no-next-control` FAIL |
| `rt3-crash-landed` | R5-B2, a crash with an unknown outcome at page 5; the page landed | FAIL | PASS | FAIL | |
| `rt3-crash-lost` | R5-B2, a crash with an unknown outcome at page 5; the page did not land | FAIL | PASS | FAIL | |
| `rt3-delete-discharging` | R5-B2, an operator's delete while the obligation is Discharging | FAIL | PASS | PASS | `delete-drops-discharging` FAIL |
| `rt4-running-unrelated` | R5-M5, a running unrelated H with a child | FAIL | PASS | FAIL | `binding-running-only` PASS |
| `rt4-ended-registry` | R5-M5, an ended H with a registry | FAIL | PASS | FAIL | `binding-running-only` FAIL |
| `rt4-genuine-child` | R5-M5, a genuine child through the actual command path | PASS | PASS | PASS | |
| `rt4-operator` | R5-M5, an operator's terminate is not parent-bound | PASS | PASS | PASS | |

89 runs. A case whose three rule sets all pass is a control: it shows the reviewed rule covers that schedule, and a
mutation, where listed, shows the repaired rule is what makes it pass. RT-3's negative passes where no owner loses its
memory, since only a takeover or a crash can tell a durable cursor from one held in memory.

## The kernel the cases run on

- **The store.** One real `InMemoryStore` with 4 partitions and a harness clock. Every partition has an owner that
  takes its fence through the real `TakeAsync` (`InMemoryStore.cs` 70–83). A takeover advances the clock past the
  10-second lease and lets another node take the partition, so the epoch moves and the old owner's commits stop
  landing. After an unknown commit outcome the owner resets and the same process takes the partition again, as the
  Worker's `Reset` does (`Worker.cs` 476–502).
- **Turns of the actor type are the real ones.** The terminate of a live or ended row, the delete, a child's notice
  at an ended parent and the drop of input at an ended row all run through the real `WorkflowInstance.Command`
  (`WorkflowInstance.cs` 31–93; terminate 179–203, delete 161–166). It runs on a real `TurnInput` over a real `Slot`,
  built by reflection, with a `Worker` that is constructed and never started. The model is a harness stub with no
  metadata (`Real.cs`, `StubModel`), and no run gives it a turn. A message that needs a model turn (a child's notice
  at a running parent) stays in the inbox and the report says so.
- **The built refusal path.** A delivery the real `CreationProtocol.TargetError` refuses (`Creation.cs` 103–109)
  becomes the real `CreationProtocol.Refuse` outcome (111–117), as `Worker.cs` 1051–1055 builds it. That is how a
  fresh owner settles a pending ticket at a retired row (R5-B1's trace; `Worker.cs` 1167–1177 lets the normal dispatch
  refuse it).
- **The commit's shape.** An outcome becomes a `TurnCommit` as `Worker.cs` 1450–1456 composes one: durable consumed
  input without `KeepUntil` plus the cancelled tickets as counted consumes, consumed input with `KeepUntil` as done,
  and the sends. The rule set decides only the cancelled-ticket part and the paging.
- **Other real calls.** `CreationProtocol.Retire` (119–120) for every marker, and `CreationProtocol.Validate`
  (145–176) inside every commit. `WorkflowInstance.Finished` (210–212) for the parent's notice, and
  `WorkflowInstance.Park` (120–132) for the owner card's "a turn above the send cap fails and parks".
  `ChildRegistry.Read` and `Write` (`ChildRegistry.cs` 8–17) for every registry, `DurableKeyJson` for every `Jobs`
  column, and `InboxKey.Ticket` over `Worker.TicketId` (`Messages.cs` 31, `Worker.cs` 295–296) for every ticket
  key. Each report lists the real calls it made.
- **Delivery.** The harness delivers the lowest stored seq first. It holds a row back where a schedule delays it, and
  stops redelivering a row after three attempts that did not land (the step bound). A live instance's job ticket is
  its driver's and is never delivered as a turn. The Worker's poll, Seen marks, trials and batching are not modelled.
- **The send cap.** The harness refuses any commit with more than 10,000 sends before it reaches the store (proposed;
  the store has no cap). A refused terminate turn of a running instance parks it through the real `Park`. A refused
  kernel refusal commit stays pending and is redelivered.

## RT-2 (R5-B1, R5-m3): idempotent cancellation of a job ticket

The tree is P → A → G → G2. P is at creation 2, and A, G and G2 sit at generated child ids. A is parked with an
unprotected delete drain open, and holds job J (or several). An operator terminates P. P's built terminate sends the
parent-bound terminate T to A, and the schedule delays T past A's Complete. Complete sends the one Faulted notice
through the real `Finished` and retires A through the real `Retire`. The marker keeps the proposed obligation: P, P's
creation, G, and each job as (key, `JobGeneration`). T then reaches the marker, is refused with `RetiredCreation`, and
the refusal path composes the cascade: a terminate to G, a cancel of each listed ticket, and T consumed.

**The ticket's exact key** is derived from (instance, creation, job key, generation). The built `Worker.TicketId`
takes the first three, so the harness carries the generation as the suffix `key.generation` inside the built
length-prefixed key (proposed derivation). The suffix is the digits after the last dot, so the key stays injective.
The witness case uses the built three-part key, as the reviewer did.

**The cancel, by rule set:**

| Rule set | Pending ticket | Done and kept | Absent |
|---|---|---|---|
| reviewed, negative | counted consume | counted consume: the store refuses it, the row is done | counted consume: the store refuses it, the row is missing |
| repaired | consumed (counted 1), with the in-process cancel | kept state cleared, nothing counted | nothing |
| `presence-from-memory` | as repaired, but the owner decides presence from its own memory of the tickets it settled; after a takeover it remembers none, counts the absent ticket and fails | | |

Under `repaired` the owner reads the ticket's row from the store at its own epoch (a ticket of A lives in A's
partition, which only A's owner consumes). The cascade's batch then counts T (1) and each present pending ticket (1
each). "Done and kept" is a done row with the proposed `KeptBy = Consumed`: the in-memory store has no `KeptBy`
column, so the harness holds it, and clears it in the transaction of the commit that landed.

The cases:

- `rt2-witness`: the reviewer's `event/Program.cs` probe as it was built. It uses one partition, the command
  `parent-command`, the ticket `c3_0/a_1!1_j` placed in the store or not, and one cascade turn. Under `reviewed` the
  absent run raises `StoreException: consumed message c3_0/a_1!1_j is not there`, with no child send stored and the
  parent command pending, as in the reviewer's `logs/store-witness.out`. Under `repaired` both runs land.
- `rt2-pending`, `rt2-done-kept`, `rt2-absent`, `rt2-mixed`: J's state at T's arrival. In the absent cases a fresh
  owner takes A's partition and polls J. A is retired, so the refusal path consumes J while the marker still lists
  it. Under `reviewed` the cascade's commit fails on every attempt, each a reset and a retake. The real store records
  the failure as an I9 divergence (`InMemoryStore.cs` 187–192), T stays pending, and G and G2 keep running.
- `rt2-settle-after`: T arrives before the settle. The cascade consumes the pending J, and the fresh owner's later
  poll finds no row.
- `rt2-duplicate`: J is absent. The cascade's commit lands, and the owner crashes before it learns the outcome. A new
  owner takes over. The dead owner's retry of the same batch is refused by the fence. T is gone, so the new owner's
  poll does not deliver it again, and nothing is sent twice. A duplicate parent delivery is a duplicate by wire id:
  the count on T refuses any second consume of it.
- `rt2-takeover`: J is settled by one owner, and another takes over before T arrives. The new owner decides presence
  under its own epoch.
- `rt2-open-drain-mixed`: T arrives while A's delete drain is open. It is refused as "resolution open", and the
  refusing commit runs the real terminate of the ended row (J-1). J1 is pending and J2 is done and kept; the
  reviewed counted cancel fails on J2.
- `rt2-live-pending`: A is running. Its own real terminate cancels the pending J, which shows the live path and the
  repaired rule agree for a pending ticket.
- `rt2-generations` (R5-m3): the marker lists `j@g1` and the cascade consumes exactly `A`'s `j@g1`. Two fixtures,
  placed directly and below admission, test the derivation itself: a same-origin `j@g2`, and an unrelated B's own
  `j@g1`. Two mutations of the derivation:
  - `foreign-derivation` derives from B's origin and cancels B's job;
  - `generation-blind` takes every same-origin ticket of the key and also consumes `j@g2`.
- `rt2-off-registry` (R5-m3): A's registry holds `j@g2`. A retirement list naming `x@g1` (not in the registry) and
  one naming `j@g1` (an older generation) are both refused as definite errors by the proposed retirement check, and
  A stays unretired. The correct list is then accepted, and the cascade consumes `j@g2`. `accept-off-registry` lets
  the first list through, so the marker lists `x@g1`.

RT-2 runs assert:

- T consumed;
- one terminate to G stored, and G and G2 Cancelled;
- no obligation open;
- exactly one notice from A to P;
- exactly one landed cascade commit, whose counted inputs are T and the pending tickets;
- each ticket's fate (consumed, kept state cleared, or no row);
- no I9 divergence in the store.

## RT-3 (R5-B2): the paged, durable cascade

P → A → N children, the children at `g2/child/1.1.n`, each running with parent A. A's registry is written by the
real `ChildRegistry.Write`; at 10,001 children it is 398,936 UTF-8 bytes, the reviewer's figure.

**Under `repaired`,** the commit that ends A (A's terminate, the refusing commit of the open drain, or the marker's
refusing commit) sends the first page. A page holds at most `P_cascade` entries, job cancels first and then child
terminates; it is 1,000 here, set with `--p-cascade`. The ids are those the built terminate composes
(`{origin}#{revision}.{ordinal}`, `WorkflowInstance.cs` 196–197). For every terminate and open-drain refusal, the run
checks that the first page's ids equal those the real `Terminate` composed.

When entries remain, the commit records the obligation as Discharging with the cursor at the page's end, and sends
the private control `kernel.cascade` to A. The control uses scope 2^62, and its sender is A. Each control's page then:

- consumes the control (counted 1);
- sends the next entries;
- writes A's row at revision + 1 (through the real store and `Validate`), or, at a marker, the proposed marker
  revision;
- advances the cursor, and sends the next control or clears the obligation.

A takeover finds the outstanding control in the store and the cursor on the obligation. A delete while Discharging
keeps the obligation on the marker. The commit that carries a page also carries its control and, on the ending
commit, the parent's notice.

**Under `reviewed`,** the cascade is one commit. Above the send cap it is refused. A's terminate turn then fails and
parks through the real `Park`: A ends Faulted, T is consumed, and the children keep running. The marker's or the open
drain's refusing commit stays pending.

The cases:

- `rt3-live-p`, `rt3-live-p-plus-1`, `rt3-live-10001`: A is running. Exactly `P_cascade` children finish in the
  ending commit (asserted: one cascade commit). `P_cascade` + 1 takes two. 10,001 takes 11 pages, and under
  `reviewed` the 10,002 sends of the one commit exceed the cap.
- `rt3-marker-10001`: the reviewer's trace. T is committed while A's delete drain is open and delivered after
  Complete. The marker's obligation moves from Held to Discharging when T arrives.
- `rt3-open-drain-10001`: T arrives during the open drain (J-1). Complete runs after page 3, and paging goes on at
  the marker.
- `rt3-jobs-marker`: 999 children and 2 jobs, one settled before T, so B1 and B2 meet. Page 1 holds both cancels and
  998 terminates, and page 2 holds the last terminate. Under `reviewed` the counted cancel of the absent job fails.
- `rt3-takeover`: another node takes A's partition after page 3. The new owner continues from the durable cursor.
  - `negative` (cursor in memory) restarts at entry 0 and sends 3,000 children a second terminate.
  - `no-next-control` sends no control: the old owner continued from memory, and its continuation dies with it, so
    the obligation stays Discharging and 7,001 children keep running.
- `rt3-crash-landed`: page 5 lands and the owner crashes before it learns that. The dead owner's retry of the same
  batch is refused by the fence, and the new owner continues at page 6.
- `rt3-crash-lost`: the owner crashes before page 5 reaches the store. The new owner finds the same control and
  redoes page 5. Under `negative` both crash cases restart at entry 0 and duplicate the terminates.
- `rt3-delete-discharging`: A has ended Cancelled with its obligation Discharging. An operator's qualified delete
  is added after page 2 and delivered after page 3, and the real delete retires A. The obligation stays on the marker, still Discharging, and
  the next control's page runs at the marker with no parent terminate. `delete-drops-discharging` leaves 7,001
  children running.

`page-above-cap` sets `P_cascade` to 10,001, above the cap, and fails on both 10,001 cases.

RT-3 runs assert:

- every child Cancelled;
- each child delivered exactly one terminate;
- no obligation open;
- exactly one notice from A to P;
- A's end state;
- in the crash cases, that the stale batch did not land;
- for the boundary cases under the default page size, the number of cascade commits.

## RT-4 (R5-M5): the parent binding at every recipient state

P → A → G is the genuine tree. Q → H → HK is unrelated, with H at the right creation in the same tenant. A is parked
with its delete drain open, and RM-1's negative fixture lists H in A's retirement handoff beside G. `Validate` at
retirement checks no membership, so the marker accepts H. P's delayed terminate then discharges A's marker, which
sends terminate to G and to H.

- **Under `repaired`,** a terminate with an issued origin (an instance's send: a tree terminate, a cascade page, a
  FailFast) is checked against the recipient's recorded `Parent` and `ParentCreation` before the actor type acts, at
  a running recipient and at an ended one that still has a registry or jobs. A mismatch is a plain refusal: the
  terminate is consumed with no effect and no cascade.
- **Parked and draining recipients** check the binding under every rule set (the open-drain refusal of #4756).
- **Under `reviewed`** a running or ended H runs the real `WorkflowInstance.Command` → `Terminate`. That is the
  existing prototype behaviour the brief cites (`WorkflowInstance.cs` 63–64, 155–158, 177–203).
- **An outside add** never carries the issued scope (`AddAsync` takes `AsOrdinary`, `InMemoryStore.cs` 227–232 and
  `Messages.cs` 49–51), so it is an operator's terminate and is not parent-bound.

The cases:

- `rt4-running-unrelated`: under `reviewed` H's real terminate cancels H and sends HK a terminate, and H's notice
  reaches Q. Under `repaired` H refuses, and H and HK keep running.
- `rt4-ended-registry`: H has finished but still owns HK. Under `reviewed` the real control path terminates HK.
  `binding-running-only` checks running recipients only and fails here.
- `rt4-genuine-child`: A is running. P's terminate ends A through the real control path, and A's ends G.
- `rt4-operator`: an operator's terminate of the running H ends H and HK under every rule set.

RT-4 runs assert G's, H's and HK's states, the terminates sent to H and HK, H's refusal reason, the notices to Q and
P, and that no obligation stays open.

## What this proves, and what it does not

**Executed in this repair** (the runs above):

- the reviewer's R5-B1 store witness, reproduced through `InMemoryStore.CommitAsync`;
- the repaired idempotent cancel, landing through the same commit on the marker path, the open-drain path and the
  live path, for pending, done-and-kept, absent and mixed tickets. That covers both arrival orders, a duplicate
  delivery after an unknown outcome and a takeover;
- the R5-B2 trace (10,001 children at a marker), the boundary at `P_cascade` and `P_cascade` + 1, and paging through
  a takeover, a crash with each unknown outcome, a delete while Discharging and the open drain;
- the R5-M5 parent binding at a running and at an ended recipient, a genuine child, and an operator;
- R5-m3: the generation-carrying key, an off-registry pair refused at retirement, a newer same-origin generation
  left alone, and the two derivation mutations.

**Existing prototype behaviour, observed in the runs:**

- the counted consume of a cancelled ticket (`Worker.cs` 1450–1456) and the store's refusal of a missing or done
  row (`InMemoryStore.cs` 116–117), recorded as an I9 divergence (187–192);
- the refusal path's consumption of a ticket at a retired row (`Worker.cs` 1051–1055, `Creation.cs` 103–117);
- the running and ended rows' acceptance of any correctly issued terminate (`WorkflowInstance.cs` 63–64, 155–158,
  179–203);
- the built `Validate`'s refusal of the marker's retired-to-retired write, "invalid lifecycle retirement"
  (`Creation.cs` 156–160). Each marker cascade prints it; it is not asserted.

**Proposed, and held by the harness** (labelled in each report):

- the obligation, its state, cursor and marker revision, kept beside the real rows and written only in the
  transaction of a commit that landed (`Harness.cs`, `Obligation`). The prototype's rows have no such columns, and
  the built `Validate` refuses the marker's next-revision write, so the K3 change must widen it;
- `KeptBy` on a done ticket;
- the generation suffix in the ticket key;
- the `kernel.cascade` control and its scope;
- the send cap;
- the retirement check of job pairs;
- the parent binding at running and ended recipients.

**Left for qualification at the milestones** (the design brief places R5-B1 at K3, K4 and K6, R5-B2 at K3, K4, K6
and K12, and R5-M5 at K3 and K6):

- the `Worker`'s own presence decision, cancel, paging and parent binding, with its poll, Seen marks, trials,
  batching and reset (K3, K6);
- `kernel.cascade` as a reserved control in the Worker: never a suspect, exempt from the poison check. No run
  exercises that (K3);
- the obligation, its cursor and `KeptBy` as store columns on SQL and MongoDB, the widened `Validate`, and the
  marker's revision fence against a concurrent writer (K3, K4);
- a page's row budget and the per-page notice and control on top of `P_cascade` entries, against a provider's
  transaction limits, and cascade throughput under load (K4, K12);
- a parked recipient with no drain open, which RT-4 does not reach (K3, K6);
- real ownership (leases and takes across processes) and cross-partition transport (K4).
