# PR3410 — independent correctness review, round7

- Reviewed head: `9508e25e57672c569f462aff9a1e0282c5780eb9`.
- Base: `ab46bc22a52aa6e456bca6ac0e803c80904de081`.
- Result: **not clean — one P2 finding**.
- Prior rounds: six completed, none clean; the owner's ten-round extension makes16 total. This is not a reset and not merge permission.
- No current standards/acceptance review output was read. No shared source, tracked files, Git history, GitHub state, running developer host or shared build output was changed by this reviewer.

## C1 — cancellation delivery acknowledges a nested model before its native work settles

**Location:** `Essentials/src/Activities/Bpmn/Elsa.Activities.Bpmn/Internal/BpmnEmbeddingDelivery.cs:62`.

The cancellation arm returns successfully for any retained model with a non-null `TerminalResult`, including a valid `TerminalPendingSettlement` model which still has native cancellation obligations. That success acknowledges `Elsa.Bpmn.CancelEmbedded.v1` in the ordinary outbox. In a native worker owned by another model, that exact intent is part of the worker's retained downstream-cancellation inventory. `BpmnNativeWorkBoundary.RequireCancellationSettlementAsync` then treats the delivered child-cancel intent as sufficient settlement proof, so the ancestor model can become `TerminalSettled` while the nested model's service worker and ETL cancellation are still owed.

This is premature settlement, not loss of the existing durable transport: the nested cancellation can eventually finish independently. It reproduces the distinction that the initial review's ETL result/cancel ordering fix was intended to preserve.

### Minimal supported reachability

1. Publish an existing finite ETL pipeline and a closed native `RunEtl` worker W2, with its complete published pin and explicit empty workflow input contract.
2. Publish model B using the supported native-service profile, binding its service task to W2. The profile permits the ordinary start → service → end shape.
3. Publish a closed native workflow W1 whose root is the new `RunBpmn`, selecting B. The ordinary executable compiler freezes B's published source/pin/incarnation in W1. Give W1 an explicit empty workflow input contract.
4. Publish model A with its supported native-service binding to W1. This is a finite acyclic material graph A → W1 → B → W2; neither `ValidatePublishedWorkerAsync`, `Graph`, nor the shared closure checks exclude this composition.
5. Start A normally. Let both native workers be admitted and B's real ETL run remain Running, for example at a saved HTTP retry.
6. Cancel A through its supported command. The normal native cancellation checkpoint for W1 contains both B's `CancelEmbedded` intent and A's native-worker terminal-result intent. `BpmnNativeWorkCheckpointEnricher` runs last and inventories the former in the latter's `CancellationSources`.
7. Deliver B's cancel input, so B has `TerminalResult.status=Cancelled` and `Lifecycle=TerminalPendingSettlement` with W2 cancellation still owed. Delay W2's cancel delivery/ETL settlement; this is an ordinary outbox scheduling/interruption order.
8. Retry W1's B-cancel source. Line62 returns, so the source becomes Delivered although B still has unsettled work. Process W1's native result and A's model input. A's existing cancellation-settlement guard sees that source Delivered and can accept the result and settle A before B/W2/ETL settle.

The same short circuit is reached if B was already terminal with unfinished cleanup when its native parent is cancelled. Requiring only semantic terminality is insufficient where the source is used as physical downstream-settlement proof.

### Verification performed

A separate console project under `probe/` links the already built exact9508 production DLLs. It constructs one current supported format6 embedded-cancellation frame with a complete matching invocation, model pin/incarnation, native-worker origin, deterministic worker address, exact cancellation source and one unsettled cancellation. **`BpmnDirectExecutionDriver.Read` accepts the frame.** The claimed `CancelEmbedded` source also passes the normal scope/fingerprint/current-claim revalidation and the exact embedding intent comparison.

- Calling `BpmnEmbeddingBoundary.RequireWorkerCancellationSettled(model)` throws the existing `RuntimeSchedulerWorkUnavailableException`: `The embedded native worker cancellation remains owed before parent settlement.`
- Calling the real `BpmnEmbeddingDelivery.HandleAsync(source)` returns successfully for the very same state, without reading a cancellation receipt or waiting for the outstanding native work.
- An assertion that this source must remain owed fails with exit134 at `probe/Program.cs:48`. This is a deliberate failed assertion, not a build or setup failure.
- Product informational version: `1.0.0+9508e25e57672c569f462aff9a1e0282c5780eb9`.
- `Elsa.Activities.Bpmn.dll` SHA256: `70447dc4e6dee1a94597ed12f22d389f7abb246466f62e3d42447faef9f1e3a7`.
- Raw command/result: `probe-red.json`; stdout/stack: `probe-red.log`; original diagnostic output: `probe.log`; complete source/project: `probe/`.

**Limit:** this is a synthetic valid-frame boundary reproduction over exact current product binaries. It is not an actual nested HTTP-host execution, old-writer checkpoint, upgraded-host proof, Studio acceptance or a new whole-suite pass. The supported finite nested publication/start/cancel path above is traced through the production code. The actual-host counterexample should be captured before changing this writer/delivery behavior.

### Narrow correction direction and acceptance of a fix

Keep the CancelEmbedded source owed until B's retained native cancellation responsibility is settled, using the existing model/native settlement boundary. Preserve already-completed/faulted semantic results and retain start-intent replay behavior. Do not add a new framework abstraction or silently prohibit the previously admitted finite composition.

Before a correction, preserve a real current-host checkpoint showing the original A/W1/B/W2/ETL identities and sources while B is cancellation-pending. Then show the reverse outbox order produces premature A settlement on this source, and that the corrected host keeps A pending until B's real worker/ETL cancellation and receipt/ACK proofs exist. Restart on the same captured stores and verify all original pins, semantic outcomes, logical admissions, remaining work and physical output. This also supplies the meaningful red/green regression boundary.

## Coverage and adversarial checks

The review scopes the complete PR against its actual main base, not just the two lockfile additions or the last correction. I inspected the new direct driver and its profile/refusal rules, model runtime/API/authorization/replay paths, native and embedded bindings/enrichers/deliveries, timers/retry and legacy fault recovery, material hash/closure/leases/GC, optional Core contracts/fingerprints, shared lease/drain routing, EF model/receipt/admission staging, queue write versions/upcasters and native-origin preservation, and the migration-history compatibility strategy. I followed the relevant existing regression and actual-host qualification coverage. Generated migration snapshots and the large evidence tree were treated as generated/evidence material; I did not rerun all historical qualification.

Discussion context came from `/tmp/elsa-2898-review7-9508-inputs`: five current issue/PR bodies,747 comments /423 unique bodies, no submitted or inline reviews; all non-Codex discussion, all six authoritative round receipts, owner/claim/hold/current scope history and the newest launch checkpoint6541193887 were consulted. The long repeated historical qualification receipts were indexed and consulted as source pointers, rather than independently accepting every historical result as new evidence. The latest launch changes no source. The three settled owner decisions remain settled. No CLA agreement was signed or scan policy changed.

Concrete rejected suspicions:

- **Replacing an older timed cancellation inventory while starting another service:** not retained. Current format4 native-service admission forbids concurrent outgoing branches and requires service/boundary outcomes directly to end events; the proposed continuing service topology does not pass `Graph`. Human-only timed profiles have no native cancellation inventory. This is not a reason to change the implementation.
- **Native/model identity race merely from separate state tables:** not retained. The qualified built-in execution path shares its execution-ID ownership lease; a mere absent-row observation does not establish a reachable concurrent committed alias.
- **SaveModel accepts same behavior hash with different producer metadata:** not retained as a new defect. The direct publication command derives its full pin from behavioral content, verifies sources/closure pins and refuses conflicting publication content. The native immutable-store boundary already uses its artifact/hash identity convention.
- **The resolved fingerprint hold or scoped4.0 previews:** no finding. The owner's current decisions supersede the chronological earlier hold text.
- **Direct-model Studio and wider ETL/provider acceptance:** no finding that these must close in this leaf. They remain separate open program obligations. The real saved native/embedded Studio proof at source25148 is not relabelled9508.

No whole test suite, actual nested host, browser run, fresh CI result or resulting-main verification is claimed by this review. The unit-boundary reproduction is the additional executed verification for C1. The shared worktree remained clean at completion.
