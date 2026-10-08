# Observed durable-value method accounting

Two finite Azure captures add exact store-method attribution to the preserved before-source workload. The Coalesced HttpEndpoint run made 65 observed calls to `EfDurableValueStateStore.ListPageAsync`, each with one matching SELECT command; Immediate made seven. The valid REST companion made 15 and five respectively. These observations locate the repeated page reads within an actual repository method. They do not establish a new optimization gain: the [M2 paging correction](https://github.com/elsa-workflows/elsa-foundation/pull/2442) already has separate before/after evidence.

The [allowlisted evidence](evidence/method-accounting-2026-10-07.json) contains all-command group counts, every admitted operation start/end, and both callback endpoints of every command linked to those operations. Raw SQL, parameters, credentials, workflow input and local paths are excluded. Private raw captures remain separately retained by their hashes. T02 remains In Progress / Verification Running because other repository callers, checkpoint participants and queue/outbox histories are still unresolved.

## Scenario and acceptance

On 7 October 2026, the owner-approved Skywalker ISP runner executed the [deterministic HttpEndpoint fixture and valid REST companion](workload-reproduction-plan.md). Both modes used the same host build, fixture revision, PostgreSQL image and capture tools. Their prelaunch descriptors differ only in `configuration.cadence`. Each capture used a fresh owned database and one HTTP request plus one REST invocation, with no in-run harness retry.

All four requests returned HTTP 200, matched the expected Alice Smith body/output, reached Completed, and had zero incidents. Coalesced read back a 50-checkpoint segment limit and boundary-level inspection; Immediate read back no segment limit and activity-level inspection. Both primary read-only repeatable-read snapshots were terminal with zero queued items. Coalesced had no outbox rows; Immediate had 15 unique Delivered rows and no other statuses. Each snapshot describes the primary execution at that instant; the REST terminal readback is not a second settlement snapshot.

Root and independent artifact reviews accepted the bounded accounting evidence. The captures stopped admitting evidence at 90 seconds, finalized within the three-second allowance, and finished inside the 120-second whole-run bound. All 585 before/after binary pins, five source-overlay pins and four fixture pins matched. Base and derived configuration hashes are recorded separately: the harness deliberately binds fresh runtime/diagnostics resources and applies the selected cadence, so those maps are not claimed identical. The loaded helper hash and zero observer-fault counts are recorded; exported artifacts do not contain the full host image for an additional local rehash.

Both hosts exited normally after the requested shutdown signal. Neither capture needed forced cleanup; all owned process groups and the disposable database containers were removed. Harness durations below are execution bounds, not workflow latency samples.

## Command ledger

One matched EF start/terminal pair is one command execution. It is not a count of SQL statements, network round trips, rows, checkpoints or exclusive workflow ownership. Selected request and source-bound sweep scopes are not a census of every host command.

| Cadence / observed scope | Runtime PostgreSQL | Diagnostics SQLite | Placement SQLite | IAM SQLite | Total EF commands |
|---|---:|---:|---:|---:|---:|
| Coalesced HTTP | 195 | 80 | 2 | 0 | 277 |
| Coalesced REST | 82 | 16 | 2 | 1 | 101 |
| Coalesced seven sweeps | 42 | 0 | 0 | 0 | 42 |
| Immediate HTTP | 505 | 128 | 2 | 0 | 635 |
| Immediate REST | 444 | 112 | 2 | 1 | 559 |
| Immediate seven sweeps | 49 | 0 | 0 | 0 | 49 |

| Cadence | Command pairs | SaveChanges pairs | Observed transaction generations | Transaction callbacks | Outer harness duration |
|---|---:|---:|---:|---:|---:|
| Coalesced | 420 | 54 | 16 | 96 | 94.797 s |
| Immediate | 1,243 | 298 | 71 | 600 | 94.636 s |

Transaction callbacks include lifecycle and savepoint events; the callback count is not a transaction count. Every command start has one successful terminal, with matching context, connection and observed transaction reference. Method-scope outcomes remain unobserved, as explained below.

The earlier [Immediate settlement packet](azure-settled-accounting.md#accepted-immediate-companion) had 556 REST commands and 42 sweep commands. This capture has 559 and 49. Its first sweep has 13 commands: four outbox SELECTs, three outbox UPDATEs, two scheduler-item SELECTs and four liveness SELECTs. The six later sweeps each have six SELECTs. This is observed background activity, not proof that the updates belong to the primary execution or an explanation of the three extra REST commands. The earlier packet remains valid within its own scope; neither packet is rewritten to match the other.

## Exact method joins and their limits

The private, opt-in overlay places entry scopes in four public durable-value methods: save, delete, find and list-page. A command joins only to the exact observed Activity ancestry and matching trace, operation ordinal, span and parent identity; table tokens alone never establish that join. The existing bounded collector preserves the command start and terminal independently.

| Cadence / request | Admitted list-page invocations | Exactly joined command pairs | Other durable-value table-token commands |
|---|---:|---:|---:|
| Coalesced HTTP | 65 | 65 SELECT | 1 SELECT + 1 INSERT |
| Coalesced REST | 15 | 15 SELECT | 1 SELECT + 1 INSERT |
| Immediate HTTP | 7 | 7 SELECT | 1 SELECT + 1 INSERT |
| Immediate REST | 5 | 5 SELECT | 2 SELECT + 1 INSERT + 1 UPDATE |

All admitted operation scopes were `durable_value.list_page`. No admitted save/delete/find scope occurred; that does not mean no durable-value writes occurred. The table above explicitly retains writes without a method join. Each capture also has two target-excluded operation starts; those are outside this selected ledger, not silently added as measured invocations.

All 92 admitted operation scopes ended with `outcomeObserved=false` and `outcome=null`. Scope disposal identifies its end; it does not prove that the method returned successfully. Successful EF command terminals and successful workflow controls are separate observations. No rows-read count is captured.

Across both packets, 92 of 1,663 commands have these exact method joins. The other 1,571 keep `noOperationAncestor`. This label means the four-method overlay supplied no matching ancestor, not that a command had no caller or was unnecessary. Source-bound drain/sweep attribution, exported engine ancestry and repository-method attribution remain different evidence dimensions. Identities are process-local and cannot be joined between captures or to the older phase crosswalk by matching ordinals.

## Source and verification pins

The runtime baseline is `e6faa5689814c33353009b13f5b9e44d0e9982a4`, with diagnostic revision `b3f55bef32576011d39b93d664949a0cb705fb3b` and five private source overlays. It is preserved before-source, not current main or the post-M2 candidate. The fixture revision is `6f3a07e24df4d2baf73dec7e52f501db55e755d9`.

The normal Workbench build completed with 96 warnings and zero errors. Before live execution, the frozen collector passed 40 distinct synthetic controls with their expected outcomes, including required rejection cases. Synthetic controls do not replace the actual host captures. The private scopes are not directly exported by the pinned Workbench engine tracing bridge, which subscribes only to `Elsa.Workflows.Runtime`; that source check does not establish zero instrumentation overhead or unchanged callback timing.

| Artifact | SHA-256 |
|---|---|
| Five-overlay source delta | `3e7b274922f3777d4f8058b7ef7a5a8ca19ce1a00e9fb8dbaa82bc4a52332e15` |
| Host output inventory, 520 files | `bc03f97270434242c42223086daf0a82b943912218b19d3ee5c87ad32dddfaae` |
| Collector DLL | `721487eae591429fe47d73a7671eff91ed9fa7a6e54250b21408be8ec6220346` |
| Actual host operation-helper DLL | `67b8fe74e8ea2634c4b7372512a41c76a5d8e65d9909acd1125f1016c6c49bfc` |
| Coalesced private archive | `53a1a3a7bf0eecb56c4f61d07586d95a6d4850aa2fe2c8aeb423ed1b0099add6` |
| Immediate private archive | `0e2b59651916b3fd7f9a5d4efe3ec34703ff503c244029fefd569c009606cf20` |

The first live T02 Coalesced attempt failed and remains excluded. Its snapshot helper retained two stale source paths, and the capture wrapper omitted the host-closure manifest from its post-run pin map. Prospective corrections changed those private tools; the host build and product source were not rebuilt or repaired. Earlier failed synthetic/tooling attempts also retain their original outcomes. A later successful capture does not turn an earlier attempt into accepted evidence.

The [checkpoint, queue, and outbox accounting packet](checkpoint-queue-accounting.md) adds exact attribution for checkpoint stages and flushes plus observed scheduler/outbox methods on the preserved-before Coalesced workload. Participant input counts remain distinct from written rows, combined flush commands remain unattributed to individual participants, and all method outcomes remain unknown. Final candidate comparison, concurrent correctness, interruption/replay checks, and finding-by-finding responses remain program gates.
