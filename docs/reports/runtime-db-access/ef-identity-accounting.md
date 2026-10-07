# EF command, context and transaction accounting (T02)

Two finite diagnostic captures on the preserved before source establish request-traced EF command, context, connection and transaction-lifecycle identities. Root and independent Sol accepted the actual captures. This report publishes the sanitized ledger without SQL text, parameters or retained database reads. It supplies no timing or optimization claim and does not close T02. [The separate before timing report](before-timing.md) records the seven low-logging windows.

## Capture pins and review scope

Both captures used host candidate `5d28bd0cd3d76004988a01b0f0abd7af3af313d6`, expected host HEAD `b3f55bef32576011d39b93d664949a0cb705fb3b`, runtime DLL SHA-256 `05c8eb6ac3aac744cb4878678d90dfd27dce9b37d139422ec1dba0f0a38bb08d`, observer source label `c52c5ba`, observer assembly SHA-256 `aba10828b05d23364fc73a3aac4839577cf186833d41f94546977e464f3d7926`, and fixture expected HEAD `87698620370ca2b7e9e682b1693c4f3296ec3741`. Coalesced and Immediate used separate observer processes; their HMACs and ordinals are not joinable across captures.

Both actual capture artifacts were accepted by root review and independent Sol artifact review:

| Cadence | Capture artifact SHA-256 | Independent Sol review SHA-256 | Root review SHA-256 |
|---|---|---|---|
| Coalesced | `7b3155f7d5055c0fef81d0dce0da201ebc0878b5cfe7e085b54c981d9506a7d9` | `06b90fcbe917285c62830fb74d0a9791d2c373aa1ac606b3e0f03e7b6957aa8d` | `da61b1b1a87c36b467434221978d8fc29fa25a450ae78df00b87aed895c6f20b` |
| Immediate | `b51d9ef398f22038ee15657cd68307ddb012b1bc2ac9df506bf1729a038bf03b` | `364802e17fe5349b0b883aeecdae8bb743db5819af86183233139dae1e4b3382` | `6ac64945b7197ac87477b1c15fbc49a93d96cc675a625037f0160f2b83f7f878` |

The accepted scope covers source/build/fixture/config pins; the exact two-trace allowlist and command-pair reconciliation; provider-reference/generation/scope joins; span closure to request anchors; publication and instance-detail identities; output/cadence/terminal status and zero incidents; and owned-resource cleanup. It does not establish exact repository callers, transaction participants, SQL statement counts, network round trips, server settlement, or performance. Both wrappers exited 0, the self-checks passed, the artifacts matched their final-copy hashes, and there were zero observer faults, overflows, or open generations. The owned hosts and containers were gone after capture. HTTP and REST controls returned 200 and `Completed`, with zero incidents and metadata `outputMatched=true`; values and workflow input content remain omitted. The rejected `f6f58…` live capture remains excluded.

## Successful EF command callback pairs

| Cadence | Request | EF command pairs | Runtime PostgreSQL | Placement SQLite | Diagnostic SQLite | IAM SQLite |
|---|---|---:|---:|---:|---:|---:|
| Coalesced | HTTP | 277 | 195 | 2 | 80 | 0 |
| Coalesced | REST | 101 | 82 | 2 | 16 | 1 |
| Immediate | HTTP | 635 | 505 | 2 | 128 | 0 |
| Immediate | REST | 556 | 441 | 2 | 112 | 1 |
| **Total** |  | **1,569** | **1,223** | **8** | **336** | **2** |

A pair is one `CommandExecuting` and one `CommandExecuted`, keyed by `(traceId, commandOrdinal, executeMethod, occurrence)`. All 1,569 pairs completed successfully. The normalized rows retain each pair’s source, method, occurrence, outcome, leading verb, table-token membership, provider/context/connection identity, exact transaction join and scope comparisons, reference-match booleans, client-response timestamp relation, and start/terminal span endpoints. Provider, context, connection, and span identities are referenced through per-process registries; no references cross capture processes. The span endpoint defaults and nullable fields are defined in the JSON schema notes, and the records reconstruct exactly to the source rows.

Storage groups and table-token counts describe EF command callback rows. Table-token memberships can overlap when one EF command contains multiple tokens. EF command callbacks are not SQL statement counts, rows, commits, or provider network round trips.

## EF SaveChanges callbacks

| Cadence | Request | SaveChanges callback pairs | Runtime PostgreSQL context | Placement SQLite context | Diagnostic SQLite context |
|---|---|---:|---:|---:|---:|
| Coalesced | HTTP | 41 | 20 | 1 | 20 |
| Coalesced | REST | 13 | 8 | 1 | 4 |
| Immediate | HTTP | 158 | 125 | 1 | 32 |
| Immediate | REST | 138 | 109 | 1 | 28 |

Diagnostic SQLite `SaveChangesCompleted.entitiesSavedCount=0` occurs alongside observed diagnostic SQLite command pairs (80/16 Coalesced; 128/112 Immediate). That EF field does not prove there was no SQL. SaveChanges pair rows retain their context and exact span endpoints, but the callbacks do not expose exact provider-transaction identity or participant-registry membership.

The source-backed baseline ledger reports six Coalesced HTTP and 44 Immediate HTTP savepoint event groups. These are grouped logger observations, not EF SaveChanges callback pairs (41 and 158 respectively); exact event-group-to-callback associations remain unresolved.

## Transaction identities and event counts

| Cadence | Request | `TransactionStarted` events (Npgsql + SQLite) | Logical generations |
|---|---|---:|---:|
| Coalesced | HTTP | 3 + 10 | 13 |
| Coalesced | REST | 1 + 2 | 3 |
| Immediate | HTTP | 22 + 16 | 38 |
| Immediate | REST | 18 + 14 | 32 |

Coalesced observed 16 physical provider transaction references and 16 logical generations. Immediate observed 38 physical references and 70 logical generations; provider reference ordinals reopen across distinct lifecycle generations. The v2 registry preserves each exact tuple `(transactionOrdinal, providerTransactionOrdinal, generationOrdinal, connectionOrdinal, contextOrdinal)`, provider type, per-request generation ordering, event counts, and exact full-tuple command-pair join counts. It also preserves every transaction event row and its span reference. These are mixed-provider EF events, not PostgreSQL-only transaction totals or checkpoint counts. Savepoint begin/create/release event names remain distinguishable in the ordered event rows.

`notEnlisted` means the EF callback observed `DbCommand.Transaction` as null. It does not establish absence of an ambient or server-side transaction. EF callback identity and transaction events do not prove server commit acknowledgment, transaction atomicity, or a checkpoint participant mapping.

## Checkpoint ancestry and marker associations

An independently recomputed derivative joins the existing callback rows to their nearest exported checkpoint ancestor. Start and terminal endpoints resolve to the same checkpoint. This adds no capture and changes none of the published ledger values. Root and independent Sol accepted the derivative and its limits in [the T02 evidence record](https://github.com/elsa-workflows/elsa-foundation/issues/2386#issuecomment-6011963583).

| Cadence | Request | Logical checkpoint spans | Observed modes | Runtime command pairs inside / outside checkpoint ancestry | Runtime SaveChanges pairs inside / outside | Marker INSERT pairs / Npgsql Started events / exact associated tuples |
|---|---|---:|---|---:|---:|---:|
| Coalesced | HTTP | 22 | 19 Deferred, 3 Immediate | 59 / 136 | 17 / 3 | 3 / 3 / 3 |
| Coalesced | REST | 18 | 17 Deferred, 1 Immediate | 19 / 63 | 5 / 3 | 1 / 1 / 1 |
| Immediate | HTTP | 22 | 22 Immediate | 211 / 294 | 52 / 73 | 22 / 22 / 22 |
| Immediate | REST | 18 | 18 Immediate | 186 / 255 | 44 / 65 | 18 / 18 / 18 |

Each of the 44 marker INSERT pairs has `exactProviderReferenceAndScope`, one candidate, matching scope and start/terminal provider-reference equality. Its full `(transaction, provider reference, generation, connection, context)` tuple equals exactly one observed Npgsql `TransactionStarted` tuple beneath the same nearest checkpoint. These are exact EF/provider and exported-span associations. They do not identify the checkpoint participant set, join each SaveChanges to a provider transaction, prove server commit acknowledgment or atomicity, or reconstruct queue/outbox row history. A command outside checkpoint ancestry is outside that observed span group, not proven unnecessary work.

Logical checkpoint spans, marker INSERT command pairs and physical provider references are distinct units. These representative HTTP controls have 22 logical spans in each mode and 22/3 marker INSERT pairs for Immediate/Coalesced. They do not reproduce the supplied historical 24/4 marker-statement counts. The initial derivative selected a nonexistent join-enum value and produced empty tuple arrays; it remains rejected. Correcting that parser supplies measurement integrity, not a new capture or runtime repair.

Accepted derivative SHA-256: `dfc0efb6421a9d4251327cd2e5463ac7fe9ba84e9798f5aee759e0932edbc98b`; independent raw-observer/engine-span/public-ledger recomputation receipt: `725e3585e860c3db32df63cbf294b66007f35155f60f1d5af37a7a3805fcc4db`. The normalized ledger below contains the callback endpoints, exact tuples and exported spans needed to recompute the associations.

## Command ancestry by phase and dispatch

An offline crosswalk now makes the existing command endpoints and exported span attributes directly usable for grouping. It preserves all 1,569 source pair identities exactly once, with references isolated to their original capture and request. Root replayed the extractor and independently reconstructed every pair's engine-parent chain and nearest phase, checkpoint and dispatch; a separate reviewer recomputed the same associations. The 44 marker tuple associations above also agree. This adds no live capture or database read.

The following counts include **runtime PostgreSQL command pairs only**, grouped by the nearest exported runtime span. A command at the unexported request Activity has no assigned runtime phase.

| Cadence | Request | Request Activity, no exported phase | Drain | Dispatch | Activity execution | Checkpoint commit | Runtime total |
|---|---|---:|---:|---:|---:|---:|---:|
| Coalesced | HTTP | 19 | 8 | 3 | 106 | 59 | 195 |
| Coalesced | REST | 15 | 4 | 3 | 41 | 19 | 82 |
| Immediate | HTTP | 93 | 104 | 45 | 52 | 211 | 505 |
| Immediate | REST | 79 | 92 | 41 | 43 | 186 | 441 |

Across all providers, 1,353 command pairs have exported engine ancestry and 216 have the explicit unexported request-Activity context. All 336 diagnostic SQLite pairs have drain ancestry in this packet. A combined drain count therefore includes diagnostics storage as well as runtime work; it is not a runtime-provider count.

Nearest dispatch ancestry supplies the observed handler, command kind and work-item attributes for 168/63 Coalesced HTTP/REST pairs and 308/270 Immediate pairs: 809 pairs in total. The other 760 retain explicit absence of a dispatch ancestor. These attributes identify the surrounding exported dispatch, not the repository caller, the queue row touched by a command, an exclusive owner, or awaited work. Table-token memberships can overlap.

For example, the preserved Coalesced HTTP baseline has 65 durable-value SELECT pairs under activity execution (25 under schedule-activity dispatch, 20 under start-activity dispatch and 20 under invoke-activity dispatch), plus one under checkpoint commit. Those 66 reads precede the delivered paging correction; they are not the post-M2 remainder. The [separately reviewed M2 delivery](https://github.com/elsa-workflows/elsa-foundation/pull/2442) reduced durable-value SELECT commands from 66 to eight and activity-state SELECT commands from 37 to 12. No pair identities are joined between these separate captures.

The [compact crosswalk summary](evidence/command-phase-crosswalk-2026-10-07.json) contains request/provider/phase/handler/checkpoint groups and all 44 marker checks. The [finite extractor](evidence/extract-command-phase-crosswalk.py) reconstructs every pair and its exact ordered engine-parent links from the SHA-pinned published identity ledger. To reproduce it from the repository root, choose a private output directory:

```sh
python3 docs/reports/runtime-db-access/evidence/extract-command-phase-crosswalk.py \
  --input docs/reports/runtime-db-access/evidence/ef-identity-accounting-2026-10-06.json \
  --out-dir /path/to/private/crosswalk-output
```

The output includes the full crosswalk, compact summary and a Markdown inventory. Four bounded controls cover a valid chain, a malformed endpoint, ambiguous parents and cross-trace isolation. All command-pair terminal references are omitted in the source normalization and defined as the start reference; the checked equality follows that contract and is not independent terminal telemetry. The 112 registry endpoints with null parents belong to other retained callback records, not these command-pair endpoints. They remain in the original ledger and are not filled from another endpoint. Chains stop explicitly at unexported parents; observer endpoint aliases are not exported engine spans.

Full derivative SHA-256: `0ca1cc77bf2d58dd8194e72e58ecf77b53bb2cec1deba6ec87b31ba5c90d0d90`; compact summary SHA-256: `77ee66147a18ec0005d9d27d648d51ef87969f78a8843669b4d435899e286f69`. The full derivative records separate unexported endpoint and parent nodes. Exact endpoint metadata and all engine-parent links were compared separately and agree. No source ledger values or runtime behavior changed.

## Span and response-time boundaries

Command span IDs either close to an exported engine span and its parent chain, or match the unexported request Activity that exactly parents exported drain roots. The latter has no runtime phase label. Exported engine attributes are limited to observed values such as checkpoint ID/mode/mandatory/post-commit intent, work-item ID, handler, command kind, and drain fields. These span relationships do not identify repository callsites or participants.

The `responseFinishedUtc` used in the EF ledger is the **client HTTP IWR `finally` timestamp**. It is not the server ASP.NET request-finished timestamp. Relative to that client boundary, the observer rows show:

| Cadence | Request | EF command callbacks before or at client IWR completion | After client IWR completion |
|---|---|---:|---:|
| Coalesced | HTTP | 277 | 0 |
| Coalesced | REST | 85 | 16 |
| Immediate | HTTP | 635 | 0 |
| Immediate | REST | 556 | 0 |

This is timestamp ordering against the client completion boundary only; it assigns no causal phase. A separate earlier census counted 40 diagnostic commands after the server ASP.NET request-finished timestamp. That server-side comparison is a different boundary and is not contradicted by these client-IWR-relative buckets.

## Historical count differences and unresolved attribution

A retained REST packet census counted 102 Coalesced and 557 Immediate packets; this EF callback ledger counts 101 and 556 REST command pairs. These are distinct capture/instrumentation measures, and their one-count differences are unreconciled. They do not establish one fewer SQL statement, an optimization gain, or a causal repair.

No repository/provider method callsite is carried by EF command events. Exact trace ancestry proves exported span-parent relationships only; the unexported request Activity is the exact parent shared by exported drain roots, with no phase assigned. No SQL text, statement cardinality, result rows, provider wire round trips, server transaction state, exact participant registry, SaveChanges-to-provider-transaction association, or outbox row lifecycle is proven. Outbox table-token membership is not a repository caller or row-lifecycle join. The diagnostics database was not read. No latency comparison or optimization claim is made. The accepted low-logging before windows are a separate evidence packet; comparable final after measurements and remaining T02 attribution are still unfinished.

## Published ledger and preservation proof

[The normalized identity ledger](evidence/ef-identity-accounting-2026-10-06.json) retains every command, save callback, transaction event and exported span from the reviewed candidate. Array rows are formatted compactly for review; parsing it produces the exact same values as candidate SHA-256 `064952341f5180fbfff06774cbb178c0cfc8f075743bfd7a74c0e05e16328f29`. Its embedded private-candidate and historical review-state labels preserve when that normalization was authored; current actual-capture acceptance is stated above. This report and the separate timing report provide the current evidence checkpoint.

Published file: 1,590,396 bytes, SHA-256 `924aa56281cea7a7b72872c0453b7a5aa76c5e07d005aab2a56ea87f7f3d6e1e`. The unchanged original candidate was 3,990,269 bytes, SHA-256 `83d94fd56614051fcc1a43ecf31acfb19e5a3cfc7a8a02f7e9fc10b54f3d77f1`. Its private round-trip verifier exactly reconstructed 1,569 command pairs, 350 SaveChanges pairs, 696 transaction event rows, 86 per-request generation observations and 223 engine spans. There are 86 unique process-local generations in total; provider-reference inventories remain 16 Coalesced and 38 Immediate. Root executed the verifier separately. Per-request generation ordering, full correlated tuples and reference-match booleans are retained; no values were inferred from totals.

The observer's corrected source SHA-256 is `c52c5ba964d3b0942386c747aed1c2f9ec78e08247abedb17c04824def15f1aa`. Its two private Release projects built with zero errors/warnings. A fixed 58-case synthetic gate ran once, including provider-object reuse, complete correlated tuples, Cartesian-negative, missing/ambiguous identity and externally delivered SIGTERM finalization controls. Root and independent Sol accepted that actual gate before the two live captures. The earlier collector's failed live join remains rejected; repairing the collector supplies measurement integrity, not a runtime performance repair.

The newer [observed durable-value method accounting](method-accounting.md) adds exact method scopes in separate captures. Its process-local identities and changed background activity remain separate from this preserved ledger.

## Remaining ownership

T02 owns exact request caller and checkpoint/participant reconciliation, with these unobserved associations kept explicit rather than derived from table names. T04/#2389 reuses the process-local identity taxonomy for a separately bounded post-response/outbox outcome. T17/#2412 and T18/#2413 own integrated correctness and a comparable final before/after packet. The prior Coalesced concurrency failure and historical valid-input 202 remain distinct; this pair of sequential successful controls does not resolve either cause. No original transform/export prerequisite is imposed.
