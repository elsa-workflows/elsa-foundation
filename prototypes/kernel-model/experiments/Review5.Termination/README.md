# Review 5 termination: RT-2, RT-3 and RT-4

Executable evidence for the termination findings of the fifth Astra High review of the kernel proposal
(elsa-workflows/elsa-foundation#4229, [the review](https://github.com/elsa-workflows/elsa-foundation/issues/4229#issuecomment-6973554094)):

- **R5-B1:** a settled job ticket rolls back the marker's cascade (RT-2);
- **R5-B2:** a valid registry needs more sends than one commit may carry (RT-3);
- **R5-M5:** a running recipient has no parent binding (RT-4), under the coordinator's update of 18 November: only
  kernel-composed terminates carry the cascade origin mark and are parent-bound;
- **R5-m3:** RM-1's foreign-job mutation, with jobs that carry generations (RT-2).

Every turn's write after setup is a `CommitBatch` handed to the real prototype `InMemoryStore.CommitAsync`
([InMemoryStore.cs](../../src/Kernel.Durable/InMemoryStore.cs) 87–194). Setup seeds instance rows directly into the
store, outside commands enter through the real `AddAsync`, the first keyed start enters through the real
`AcceptAsync`, and the proposed state (obligations, `KeptBy`, marks) is kept beside the rows. The commit checks the partition fence and
counts every consumed input: a consume of a row that is missing or done raises
`StoreException: consumed message … is not there` (116–117), and a send whose key exists raises "exists already"
(118–119). So the reviewed rule reproduces the reviewer's store witness through the same commit, and the repaired rule
lands through it.

Each case runs one schedule under three rule sets:

- `reviewed`: the design at `0a1bb5e0c`;
- `repaired`: the fifth fix round's repair rules, as the coordinator's design brief states them;
- `negative`: the repaired rules with the case's one repaired rule put back as reviewed:
  - RT-2: the counted cancel;
  - RT-3: the durable cursor (the cursor lives in the owner's memory); for the ThenDelete cases, the widened
    `Validate`; for the key-reuse case, admission's cascade read;
  - RT-4: the parent binding of marked terminates at running and ended recipients.

Mutations of the repaired set run where they bite:

- RT-2: `presence-from-memory`, `foreign-derivation`, `generation-blind`, `generation-inside-key`,
  `accept-off-registry` and `accept-duplicate-pair`;
- RT-3: `page-above-cap`, `no-next-control` and `delete-drops-discharging`;
- RT-4: `binding-running-only`, `bind-every-issued-terminate` and `commit-accepts-mark`.

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

`run.sh` reads its individual runs from `--list`, so they and `--all` run the same rows.
[results.txt](results.txt) holds the output of `--all` and the commit it ran at. The whole matrix takes about ten
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
| `rt2-live-pending-g0` | R5-B1 on the live path at generation 0: the cancel key is the built key itself | PASS | PASS | PASS | |
| `rt2-generations` | R5-m3, a newer same-origin generation and another instance's ticket | PASS | PASS | PASS | `foreign-derivation` FAIL, `generation-blind` FAIL |
| `rt2-off-registry` | R5-m3, retirement refuses a pair the registry does not hold | PASS | PASS | PASS | `accept-off-registry` FAIL |
| `rt2-duplicate-pair` | R5-m3, retirement refuses `[j1@g1, j1@g1]` for registry `[j1@g1, j2@g1]` | PASS | PASS | PASS | `accept-duplicate-pair` FAIL |
| `rt2-ticket-collision` | R5-m3, `(j, g1)` and `(j.1, g0)` are different tickets | PASS | PASS | PASS | `generation-inside-key` FAIL |
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
| `rt3-thendelete-root-p-plus-1` | R5-B2, ThenDelete on a running root, `P_cascade` + 1 entries | FAIL | PASS | FAIL | |
| `rt3-thendelete-root-10001` | R5-B2, the same with 10,001 children and 2 jobs | FAIL | PASS | FAIL | |
| `rt3-thendelete-child-p-plus-1` | R5-B2, ThenDelete on a running child, `P_cascade` + 1 entries | FAIL | PASS | FAIL | |
| `rt3-thendelete-child-10001` | R5-B2, the same with 10,001 children and 2 jobs | FAIL | PASS | FAIL | |
| `rt3-thendelete-kept-p-plus-1` | R5-B2, ThenDelete on a protected child: terminated and kept | PASS | PASS | PASS | |
| `rt3-thendelete-kept-10001` | R5-B2, the same with 10,001 children and 2 jobs | FAIL | PASS | PASS | |
| `rt3-key-reuse-discharging` | R5-B2, a keyed root deleted while Discharging, its key reused | PASS | PASS | FAIL | |
| `rt4-running-unrelated` | R5-M5, a marked cascade terminate at a running unrelated H with a child | FAIL | PASS | FAIL | `binding-running-only` PASS |
| `rt4-ended-registry` | R5-M5, the same at an ended H with a registry | FAIL | PASS | FAIL | `binding-running-only` FAIL |
| `rt4-genuine-child` | R5-M5, a genuine child through the actual command path | PASS | PASS | PASS | |
| `rt4-operator` | R5-M5, an operator's terminate is not parent-bound | PASS | PASS | PASS | |
| `rt4-model-send` | R5-M5, a model's application terminate to a running non-child applies as built (no narrowing) | PASS | PASS | PASS | `bind-every-issued-terminate` FAIL |
| `rt4-forgery` | R5-M5, the forgery control: a model's marked send is refused at the commit | FAIL | PASS | PASS | `commit-accepts-mark` FAIL |

129 runs. A case whose three rule sets all pass is a control: it shows the reviewed rule covers that schedule, and a
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
  key. `CreationProtocol.StartError` (89–101) for every start, `InMemoryStore.AcceptAsync` and `KeyRequest`
  (`KeyAdmission.cs`) for the key cases, and `DurableTurn.Send`, `Add` and `Seal` for a model's sends. Each report
  lists the real calls it made.
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

**The ticket's exact key** is derived from (instance, creation, job key, generation); the derivation is proposed. The
built `Worker.TicketId` takes the first three (`c{n}_{actor}_{creation}!{length}_{key}`), and generation 0 keeps exactly
that key. A positive generation appends `#g{generation}` after the built id. The job key sits under its own length
prefix, so nothing a key spells reaches past it. The two forms, and any two generations, therefore never derive the
same id: `(j.1, g0)` is `…!3_j.1` and `(j, g1)` is `…!1_j#g1`. The fourth round's form put the generation inside the
key (`key.generation`), where those two meet; `rt2-ticket-collision` runs it as the mutation `generation-inside-key`.
The witness case uses the built three-part key, as the reviewer did.

On the live and open-drain paths the real `Terminate` composes its cancels with the built three-part key
(`WorkflowInstance.cs` 198). The harness cancels the generation-suffixed key in their place, and every run checks that
each built key is the generation-0 form of the key the harness cancels, in order.

**The cancel, by rule set:**

| Rule set | Pending ticket | Done and kept | Absent |
|---|---|---|---|
| reviewed, negative | counted consume | counted consume: the store refuses it, the row is done | counted consume: the store refuses it, the row is missing |
| repaired | consumed (counted 1); the in-process driver cancel is not modelled | kept state cleared, nothing counted | nothing |
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
  owner takes over, and the dead owner's retry of the same batch is refused by the fence. Then a duplicate of T (a
  transport redelivery of the same row) reaches the new owner at its current epoch: the fence passes, and the count
  on T refuses the second consume with "consumed message … is not there". The store records that as I9, and the run
  expects exactly that one entry. Nothing is sent twice. T's row is gone, so the harness's delivery (standing in for
  the poll) never offers it again.
- `rt2-takeover`: J is settled by one owner, and another takes over before T arrives. The new owner decides presence
  under its own epoch.
- `rt2-open-drain-mixed`: T arrives while A's delete drain is open. It is refused as "resolution open", and the
  refusing commit runs the real terminate of the ended row (J-1). J1 is pending and J2 is done and kept; the
  reviewed counted cancel fails on J2.
- `rt2-live-pending`, `rt2-live-pending-g0`: A is running, and its real terminate composes the cancel of the pending
  J. The harness commits its own cancel key in place of the built one, and checks that the built key is that key's
  generation-0 form. At generation 0 the two keys are the same key, and the cascade consumes the built key itself.
- `rt2-generations` (R5-m3): the marker lists `j@g1` and the cascade consumes exactly `A`'s `j@g1`. Two fixtures,
  placed directly and below admission, test the derivation itself: a same-origin `j@g2`, and an unrelated B's own
  `j@g1`. Two mutations of the derivation:
  - `foreign-derivation` derives from B's origin and cancels B's job;
  - `generation-blind` takes every same-origin ticket of the key and also consumes `j@g2`.
- `rt2-off-registry` (R5-m3): A's registry holds `j@g2`. A retirement list naming `x@g1` (not in the registry) and
  one naming `j@g1` (an older generation) are both refused as definite errors by the proposed retirement check, and
  A stays unretired. The correct list is then accepted, and the cascade consumes `j@g2`. `accept-off-registry` lets
  the first list through, so the marker lists `x@g1`.
- `rt2-duplicate-pair` (R5-m3): A's registry holds `j1@g1` and `j2@g1`. The handed-over jobs must equal the
  registry's current-generation pairs as a set, each once (and each child appears once), so `[j1@g1, j1@g1]` is
  refused under every rule set and the correct list is accepted. The reviewed text's two checks (each pair in the
  registry, no more pairs than it holds) admit that list; `accept-duplicate-pair` keeps only those two, and the
  cascade consumes j1 twice and leaves j2 uncancelled.
- `rt2-ticket-collision` (R5-m3): A's registry holds `j@g1`. A fixture places a pending generation-0 ticket of job key
  `j.1` beside it. The cascade consumes `j@g1`'s ticket, and `(j.1, g0)`'s stays pending (observed before the poll
  can reach it). `generation-inside-key` derives `(j, g1)` as `…!3_j.1` and cancels the wrong ticket.
- In `rt2-generations` and `rt2-ticket-collision` the fixture tickets the cascade must leave alone are checked right
  after the cascade, before the harness releases them to the retired A's refusal path.

RT-2 runs assert:

- T consumed;
- one terminate to G stored, and G and G2 Cancelled;
- no obligation open;
- exactly one notice from A to P;
- exactly one landed cascade commit, whose counted inputs are T and the pending tickets;
- each ticket's fate (consumed, kept state cleared, or no row);
- no I9 divergence in the store, other than the one `rt2-duplicate` provokes;
- that each built cancel key is the generation-0 form of the harness's cancel key.

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

The page numbers above are those at the default `P_cascade` of 1,000. With `--p-cascade`, each schedule's page is
kept inside the cascade: the takeover and Complete come after page 3 or the last page but one, the crash at page 5 or
the last page but one, and the delete after page 2 or the last page but two. The expected matrix holds for every
`P_cascade` from 2 to 4,999 (checked at 2, 3, 333, 1,000 and 4,999):

- at 2 or more, page 1 holds both jobs of the rows that carry two;
- at 4,999 or less, 10,001 children take three pages or more, and the key-reuse root's 2 `P_cascade` + 1 children stay
  under the send cap for its reviewed one-commit run.

RT-3 runs assert:

- every child Cancelled;
- one stored terminate from A per child, and exactly one delivery per child;
- no obligation open;
- exactly one notice from A to P;
- A's end state;
- in the crash cases, that the stale batch did not land, and, for `crash-lost`, that the crash kept exactly one page
  from the store and the new owner landed that page from the same control;
- for the boundary cases under the default page size, the number of cascade commits.

### ThenDelete on a running instance (PC-5)

An operator's `kernel.terminate` with `ThenDelete` (proposed payload `{"ThenDelete":true}`) reaches a running A with
`P_cascade` + 1 entries (`P_cascade` − 1 children and 2 pending jobs) or 10,001 children and 2 jobs. A is a root, a
child of a running P, or a child whose row `ProtectsDeletion` protects (a fixture; the stub model has no metadata).

- **The rule (repaired).** One commit terminates A through the real `Terminate`, sends page 1 and retires the row
  with a Discharging obligation. The obligation's list equals the previous row's registry and jobs, and its cursor is
  at most `P_cascade`. The marker is `Retire(previous)` with the terminal status, at the commit's revision.
  - `Validate` is widened for exactly that shape, and only in a commit that also terminates the row (proposed).
  - The built `Validate` inside `CommitAsync` refuses it ("invalid lifecycle retirement", printed in every run). So
    the real commit carries the terminate (row write, counts, sends), and the harness writes the marker in its place
    in the same step.
  - The pages then go on at the marker.
  - A protected row is terminated and kept, and its obligation pages on the live row.
- **Under `reviewed` and `negative`** (the built `Validate`, not widened), the one commit goes to the store with the
  marker. It is refused as a definite error on every attempt, so A keeps running, and so do its children. Under
  `reviewed` a 10,001-child turn exceeds the send cap first and parks. The kept variant's reviewed run passes at
  `P_cascade` + 1 (one commit within the cap) and fails at 10,001 (the cap).
- **Every ThenDelete run also asserts** that page 1 holds both job entries (jobs first) and consumes both pending
  tickets. Where A retires, it asserts the retiring commit's shape.

### Key reuse while Discharging (PC-3)

A keyed root R is admitted by the real `AcceptAsync` (`KeyAdmission.cs` 48–84) and started at creation 1. That is
the proposal's creation in place of the built unqualified start; the start commit is the harness's, with no model
turn. R has 2 `P_cascade` + 1 children (2,001 at the default). An operator terminates R; after page 1 an operator's
delete arrives, and the real delete retires R while its obligation is Discharging. The caller then reuses key K:
a new operation op-2, the same operation op-2 again, and, after the last page, op-2 once more.

- **Existing prototype behaviour.** The built admission binds a reused key to a fresh physical id (`…/key/2`,
  printed from a scratch store), so it never meets the marker.
- **The proposal's binding** keeps the actor id and advances its creation (tenancy, R4-M6). The harness mirrors
  `AcceptAsync` lines 54–82 with that change, over the real `KeyRequest` (`Validate`, `WireId`, `Digest`).
  - **`repaired`.** Admission reads the bound actor's cascade state in its transaction. While it discharges, the
    answer is `in-progress` with reason ObligationOutstanding, and no receipt is written. After the last page op-2
    is admitted, and its start writes the successor at creation 2 through the real `StartError` and `Validate`.
  - **`negative`** (no admission read). op-2 is admitted and its receipt written. The worker's ObligationOutstanding
    refusal, below ingress, consumes the start. The same-operation retries return the stored receipt and insert
    nothing, so the start is lost.
  - **`reviewed`** passes. The reviewed design does not page, and 2,001 children fit in one commit under the cap, so
    nothing is Discharging when the key is reused, and op-2 is admitted and starts creation 2.
- **The run asserts** that:
  - no start was admitted while the cascade discharged;
  - no start was refused with ObligationOutstanding;
  - exactly one successor creation, 2, was written at R's id;
  - op-2's receipt names it;
  - every descendant is Cancelled.

## RT-4 (R5-M5): the parent binding of marked terminates

The rule, as the coordinator updated it on 18 November with the transitions docs author, so that built behaviour is
not narrowed:

- **The cascade origin mark.** Every terminate the kernel composes to end a subtree carries the mark. That is the
  actor type's tree terminate, and every cascade page, from an ended instance, an open-drain refusal or a retirement
  marker. The mark is a one-byte flag on an issued-origin message. No turn method sets it, outside entry clears it,
  and the commit refuses a marked message that is not a `kernel.terminate` from the committing row.
- **The binding.** A recipient checks `Sender` and `SenderCreation` against its recorded `Parent` and
  `ParentCreation` on every marked terminate, in every state: running, parked, draining, ended with a registry or
  obligation, and retired. A mismatch is refused with reason "parent" and no effect.
- **A model's application terminate** (`ITurn.Send`, FailFast included) carries no mark. It keeps its built
  authorization at a running recipient (`WorkflowInstance.cs` 63–64, 155–158, 177–203), and P-7's rule at a parked or
  draining one.

The harness holds the mark beside the stored message, written only when its commit lands (proposed, `Harness.Marks`).
It marks every child terminate of a terminate's page, a marker's cascade page and an open-drain refusal's page, under
every rule set; only the repaired binding and commit check read it. Its commit check (`CommitRefusal`) refuses two
things before the store:

- under every rule set, a composed `kernel.terminate` whose `Sender` and `SenderCreation` are not the committing
  actor's own: the proposal's existing rule, "An actor type's composed send is checked";
- under `repaired`, a mark on a model turn's send, or on anything but an issued `kernel.terminate`.

A model's turn runs as a real `DurableTurn` over the reflected input. `DurableTurn.Send` (the method behind
`ITurn.Send`, Turns.cs 313–335) or `Add` (339–351) composes the send, and `Seal` (481–508) seals it at the next
revision.

The tree for the marker cases: P → A → G is the genuine tree, and Q → H → HK is unrelated, with H at the right
creation in the same tenant. X is a running root, unrelated to H. A is parked with its delete drain open, and RM-1's
negative fixture lists H in A's retirement handoff beside G. `Validate` at retirement checks no membership, so the
marker accepts H. P's delayed terminate then discharges A's marker, which sends marked terminates to G and to H.

- **Under `reviewed`** a running or ended H runs the real `WorkflowInstance.Command` → `Terminate`.
- **Parked and draining recipients and markers** check the binding under every rule set (the open-drain refusal of
  #4756, RM-1).
- **An outside add** never carries the issued scope (`AddAsync` takes `AsOrdinary`, `InMemoryStore.cs` 227–232 and
  `Messages.cs` 49–51), so it is an operator's terminate and is not parent-bound.

The cases:

- `rt4-running-unrelated`: the marked cascade terminate reaches a running H. Under `reviewed` H's real terminate
  cancels H and sends HK a terminate, and H's notice reaches Q. Under `repaired` H refuses with reason "parent", and
  H and HK keep running. `negative` turns the binding off.
- `rt4-ended-registry`: H has finished but still owns HK. Under `reviewed` the real control path terminates HK.
  `binding-running-only` binds running recipients only and fails here.
- `rt4-genuine-child`: A is running. P's terminate ends A through the real control path, and A's marked terminate
  ends G.
- `rt4-operator`: an operator's terminate of the running H ends H and HK under every rule set.
- `rt4-model-send`: X's model turn sends `kernel.terminate` to H through `DurableTurn.Send`. The send is issued and
  unmarked, so H applies it as built under every rule set: H and HK end Cancelled, and nothing is refused.
  `bind-every-issued-terminate` binds every issued terminate, refuses X's, and fails.
- `rt4-forgery`: three model turns of X, each with the mark set on its send:
  - (a) a `kernel.terminate` to H;
  - (b) an application message to H;
  - (c) a composed `kernel.terminate` naming Q, H's parent, as `Sender`.

  Under `repaired` all three are refused at the commit: (a) and (b) because no turn sets the mark, and (c) by the
  sender rule (asserted by its reason). X's row is unchanged, nothing is stored, and H keeps running. The reviewed
  design has no mark; the harness still attaches the test marks in the `reviewed` run but checks none, so (a) and (b)
  land with their marks recorded, and X's application terminate ends H as built. (c) is refused there too, by the
  sender rule. `commit-accepts-mark` lets (a) and (b) land with their marks: H then refuses (a) with reason
  "parent", but a mark no kernel composition set is stored, and the run fails on that.

  This is a structural check of the mark rule. The real harm of a forged mark is not reached: a model marking its
  own terminate to a parked child of the forger, which P-7 would treat differently.

RT-4 runs assert:

- G's, H's and HK's states;
- the terminates sent to H and HK, and whether they carry the mark;
- H's refusal reason;
- the notices to Q and P;
- in the forgery case, which commits landed and why, X's revision, and the stored messages and marks among the inbox
  keys the three attempts composed (by provenance, so (c)'s forged `Sender` cannot hide it);
- that no obligation stays open.

## What this proves, and what it does not

**Executed in this repair** (the runs above):

- the reviewer's R5-B1 store witness, reproduced through `InMemoryStore.CommitAsync`;
- the repaired idempotent cancel, landing through the same commit on the marker path, the open-drain path and the
  live path, for pending, done-and-kept, absent and mixed tickets. That covers both arrival orders, a stale replay
  after an unknown outcome, a duplicate delivery refused by the count on T, and a takeover;
- the R5-B2 trace (10,001 children at a marker), the boundary at `P_cascade` and `P_cascade` + 1, and paging through
  a takeover, a crash with each unknown outcome, a delete while Discharging and the open drain; ThenDelete on a
  running root, child and protected child; and key reuse while a keyed root's marker is Discharging;
- the R5-M5 parent binding of marked terminates at a running and at an ended recipient, a genuine child, an operator,
  a model's application terminate applying as built, and the commit's refusal of a forged mark;
- R5-m3: the injective generation-carrying key and its collision run, an off-registry pair and a repeated pair
  refused at retirement, a newer same-origin generation left alone, and the derivation mutations.

**Existing prototype behaviour, observed in the runs:**

- the counted consume of a cancelled ticket (`Worker.cs` 1450–1456) and the store's refusal of a missing or done
  row (`InMemoryStore.cs` 116–117), recorded as an I9 divergence (187–192);
- the refusal path's consumption of a ticket at a retired row (`Worker.cs` 1051–1055, `Creation.cs` 103–117);
- the running and ended rows' acceptance of any correctly issued terminate (`WorkflowInstance.cs` 63–64, 155–158,
  179–203), and a model's terminate composed by `DurableTurn.Send` (Turns.cs 313–335);
- the built `Validate`'s refusal of the marker's retired-to-retired write, "invalid lifecycle retirement"
  (`Creation.cs` 156–160). Each marker cascade prints it; it is not asserted. The same check refuses ThenDelete's
  terminate-and-retire commit, which the reviewed and negative ThenDelete runs hit through the real store;
- the built key admission's fresh physical id on reuse (`KeyAdmission.cs` 60–67), and its stored receipt for a
  repeated operation (55–56).

**Proposed, and held by the harness** (labelled in each report):

- the obligation, its state, cursor and marker revision, kept beside the real rows and written only in the
  transaction of a commit that landed (`Harness.cs`, `Obligation`). The prototype's rows have no such columns, and
  the built `Validate` refuses the marker's next-revision write, so the K3 change must widen it;
- `KeptBy` on a done ticket;
- the generation suffix `#g{generation}` in the ticket key;
- the `kernel.cascade` control and its scope;
- the send cap;
- the retirement check: the jobs equal the registry's current-generation pairs as a set, each once, and each child
  appears once;
- the widened `Validate` for ThenDelete's one-commit retirement: the harness checks the shape and writes the marker
  after the real commit lands, since the built `Validate` refuses it;
- the key binding that keeps the actor id and advances creation, and admission's cascade read;
- the cascade origin mark, the commit's checks on composed terminates, and the parent binding of marked terminates
  at running and ended recipients.

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
- a parked recipient with no drain open, which RT-4 does not reach, and P-7's rule for an unmarked terminate there
  (K3, K6);
- outside entry clearing the mark, which no run exercises: an outside add never carries the issued scope, so no
  harness path carries a mark in (K3);
- real ownership (leases and takes across processes) and cross-partition transport (K4).
