# PR3410 complete-PR author review, round 4: correctness

Reviewed head: `cdc02819f852684dc855ecb111bfa261171f40b6`.
Merge base: `3386a1abcc01b5ff07f127e4bc0cc07ecbe0bc37`.
Fresh observed `origin/main`: `8b5f2b3d8662a7f31fd8b2b39d2d19e588b1cbbf`; the PR head was kept fixed throughout review. This is full-PR round 4 within the existing six-round bound, not a component review or a reset of the counter.

Verdict: **one actionable P2 finding; not clean**. No second actionable correctness finding survived review.

## P2 — Give every concurrent model-admission write its own retention lease

Location: [RuntimeExecutionProtocolRouter.cs, line43](https://github.com/elsa-workflows/elsa-foundation/blob/cdc02819f852684dc855ecb111bfa261171f40b6/Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Executions/RuntimeExecutionProtocolRouter.cs#L43), in the lease protecting lines42–46.

The new router uses `"model-admission:" + work.WorkItemId` as the material retention lease ID. `work.WorkItemId` is derived only from `InputId`. Distinct initial executions of the same model with a common request ID such as `start` therefore acquire the same physical lease. This is possible through ordinary BPMN starts from different authorized callers in the same scope; per-execution actor serialization does not serialize those distinct executions.

The existing lease contract explicitly warns that every acquirer of an unexpired lease ID receives the same lease and the first release ends it for all acquirers (`IWorkflowExecutableRootWriteLeaseManager`, remarks; EF's existing `TryAcquireRootWriteLeaseAsync` returns the existing token). If one request is cancelled before enqueue, its finally block releases the other request's protection. Publication retirement and ordinary reference GC can then remove the pinned model before the other request commits its first queue root. The queue write succeeds, but the router's post-enqueue `SelectAsync` throws `RuntimeSchedulerWorkUnavailableException: The exact pinned model material is unavailable.` A durable `ExecuteModel` command is stranded with a pin/incarnation whose material no longer exists.

Use a fresh lease identity per concurrent admission attempt, for example by appending a new attempt UUID. Adding only the execution ID would still fail to isolate any overlapping attempts for that same execution. This needs a focused concurrency regression, not a new retention abstraction.

### Executed counterexample and control

`router-probe/Program.cs` invokes the **actual compiled router and selector**. It uses real EF SQLite material, model-state reader/checkpoint capability, source-reference, scheduler queue and garbage-collector implementations. A queue decorator pauses immediately before calling the actual EF `EnqueueAsync`; this is after the router's second checked material selection, inside its retention lease. It does not modify any persisted command or execution row.

1. Save valid model material and establish its live publication through the normal fenced source-publication port.
2. Start two router calls for distinct execution IDs, with the same model and input ID `start`. Both reach the queue barrier.
3. Cancel the first caller through its actual request cancellation token. Its queue root was never written.
4. Retire the original publication through `RetireAsync`, then run the actual reference collector. Staging grace is explicitly configured to zero through the supported production option; the real system clock is unchanged. An existing publication older than its configured grace has the same eligibility.
5. Release the second queue barrier. The actual EF queue commits its original pinned command, then the actual router reselects material.

| Observation | Current shared lease ID | Control: only make each lease ID unique |
|---|---:|---:|
| First request | TaskCanceledException | TaskCanceledException |
| Publication retired through port | true | true |
| Artifacts collected before second enqueue | 1 | 0 |
| Second router call | exact pinned material unavailable | succeeds |
| Durable pending commands | 1 | 1 |
| Physical model material rows | 0 | 1 |
| Model execution/input rows at this cut | 0 / 0 | 0 / 0 |

The control changes only the retention lease ID passed to the existing manager. The semantic protocol contribution is a minimal public extension driver; its validation has no side effects. A drain stub is used only by the successful control after admission, so this does **not** claim BPMN semantic completion or actual upgraded-host recovery. The failing path never reaches semantic draining. The defect is in the shared runtime admission boundary used by BPMN.

The earlier `lease-probe` independently reproduced the underlying provider/manager interaction. `router-probe/first-pass-without-publication.log` is an earlier diagnostic; the retained final proof adds real publication and retirement through normal ports.

### Durable pre-fix capture

`captured-router-publication-race/` contains separate coherent whole SQLite snapshots for the failing case and unique-ID control, each backed up after the probe process exited through the existing read-only .NET SQLite observer. The original producer stores also remain under the probe output. No SQL write, row seeding, clock manipulation, claim mutation or product-source edit was used.

- Failing closed snapshot SHA-256: `2d6a86081f177e280e0bc2b6174fe1e2ab5acb6106c86613cf22282ae8839474`.
- Control closed snapshot SHA-256: `87f2a9ed6223c6da7b1167bdb5ed02c2b2289d8c07b02e892c2f436f8d9e1fe6`.

`result.json` records the actual router failures and physical table counts. The `*-backup.json` files bind the provider backups and integrity/read-only receipts. Gzipped whole-store copies are retained before any fix changes the implicated consumer of the lease writer. These are actual outputs of a controlled fresh-provider counterexample, not legacy workflow checkpoints or positive host qualification.

## Source, verification and review boundary

The executing Workbench product set is source `76e8139dc7744781fba864f8247c180134eaaaed`. All102 changed non-generated product C#/project files are byte-equivalent between that source and the reviewed head; the individual Git blob ledger is `all-product-source-equivalence.json`. `provenance.json` separately lists the seven directly implicated files. `product-metadata.json` records the actual product/source hashes and released BPMN0.5.0 libraries. The probe references these existing products, without rebuilding or editing repository source.

The probe and control builds both pass with zero warnings/errors. The initial primitive probe needed the product's packaged SQLite native library copied into its isolated output; its missing-library error is retained as a setup diagnostic, not behavioral evidence.

Focused surrounding Runtime material/checkpoint/root-lease tests pass **66/66, zero skips**, using the existing built test assembly. All104 frozen baseline asset hashes remain unchanged. The unchanged Studio mechanical verifier accepts the current66-asset bundle, four completed new runs and12 stores, with its explicit `directModelStudioAcceptanceProved:false`. I did not repeat the browser journey or claim a separate visual inspection. The author's source-bound6189-project pass is not relabelled as this review's execution or exact-head CI.

Review covered the full PR's production changes across material identity/hash/retention, protocol registration and routing, shared lease/drain/queue recovery, BPMN direct/human/timer/retry semantics, native worker admission/result/cancellation, RunBpmn parent proof, physical model/native EF checkpoint conditions, coalescing, schema/write-version fences, migration-history compatibility and native/public wire preservation. Historical generated migrations, test and evidence changes were inspected as supporting context; this review adds no acceptance claim for non-SQLite upgraded hosts or schema downgrades.

Current repo/org AGENTS and full issue/PR discussion were refreshed: #2898 has87 comments; PR3410 has214 comments, zero submitted reviews and zero inline review comments. All are retained in the adjacent JSON files, with an index of the chronological checkpoints. Repetitive CLA requests are not authorization to sign. The current round-start checkpoint, prior three round receipts, fixes, evidence boundaries and owner instructions were considered.

Known owner API/version-magnitude/needs:decision, deferred GitGuardian39189012/scan, final required CI, protected merge and resulting-main verification remain separate delivery gates. I made no GitHub writes, repository edits, commits or branch changes. No owned host remains running; only the short-lived probe processes were started. Existing user hosts/tabs were untouched.
