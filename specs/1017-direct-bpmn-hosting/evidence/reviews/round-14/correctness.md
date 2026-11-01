# PR #3410 — full author-review round 14, correctness

Verdict: **no actionable correctness findings**. This is the independent correctness lens, not merge clearance or broader program acceptance.

Reviewed head: `d3f6ff69593200610f983c47ac4643a5489ec0ce`.
Base: `588da3eb01d0949ba3c9721b61c09fbc2334a335`.
Launch: https://github.com/elsa-workflows/elsa-foundation/pull/3410#issuecomment-6551250416

HEAD matched at entry and exit; the shared checkout stayed clean. No source/GitHub mutations, builds, host groups, push, or peer-review contact occurred. No other round-14 reviewer output was read. Tests ran from private copies under `correctness-work/`.

## Governing context and complete scope

Applied the supplied current org AGENTS (including independent complete-diff review and concrete-scenario findings), repository AGENTS, contributor review checklist, ratified framework 6.1.0/Elsa 6.3.0 gates, and spec1017 specification, plan, data model and hosting contract. Draft/provisional material retains that status. The existing owner decisions concerning preview versions, scanner resolution, ordinary repair/retest continuation, review count and delivery authority were not reopened.

The review uses the complete base-to-head inventory, saved at `correctness-work/complete-diff-inventory.txt` (963 changed paths), not just the archive correction. Production tracing covered:

- BPMN contracts/material hashes/profile validation; public publication/start/inspection/human/cancel/incident/retry and authorization before retained-result disclosure, including the pending-admission race.
- Released interpreter sharing with legacy BpmnProcess; direct model progression; human, duration, signal and native-worker inputs; typed business-error/fault/cancellation; bounded held-failure retry; obsolete results; terminal settlement and retained historical frames.
- Native service start/source authority, deterministic attempt identity, atomic admission against the authorizing model, start retirement, terminal result capture/consumption, RunBpmn native waits/results, cancellation before admission, nested cancellation and startup recovery of old final-fault work.
- Scoped protocol selection/routing, unknown-protocol refusal before queue claim, the shared ownership heartbeat/release boundary, FIFO claims, model-specific failure retention, native terminal purge and settled-model mailbox eviction.
- Atomic model state/input/consumed claim/outbox/fence/marker participation, immutable pins/authority/result, physical CAS for retained-state inputs and cross-model admission, coalescing/default-provider refusal, provider error boundaries and protected schema upcasters/writers.
- Publication dependency closure, physical incarnations, separate admission leases, source/root fencing, queued-first-input/model retention, staging grace and garbage collection.
- All affected Runtime/Design provider migration families, target models/configuration and cumulative-history metadata/decorator, retained identifiers, public compatibility constructors, host/API composition, projects/locks/tier metadata, migration tooling, qualification controllers/verifiers, archival restoration and size guard. Generated migrations/locks were assessed against the schema, dependencies and relevant behavioral tests rather than as independent handwritten algorithms.

Discussion corpus: five cached issue/PR bodies, **343 program comments + 143 issue2898 comments + 379 PR comments**, the sole submitted Code Review and zero inline comments. Every cached record is included in `correctness-work/discussion-inventory.json`, with author, URL, body hash, subject and finding/decision excerpts (871 records, 437 unique bodies). Substantive requirements, owner decisions, prior findings/corrections, scanner/skipped-review notices and repeated CLA notices were reconciled. Earlier review findings were checked against current production paths and tests, not assumed to remain defects or assumed fixed from a receipt alone.

## Current correction reconciliation

The terminal native-result path first establishes the exact physical worker, scope, pin, authority and terminal source. For a Cancelled worker it checks earlier durable responsibility as well as the current terminal checkpoint's inventory. Only native-result transport and the obsolete local scheduler-continuation kind are excluded; genuine downstream cancellation remains owed regardless of availability time, claim or retry state. The dispatcher restricts that scheduler continuation to its own source execution, whose native terminal drain refuses progression.

RunBpmn cancellation does not acknowledge old terminal frames with remaining native Work or unsettled cancellation entries. The existing parent-result acknowledgment can still trigger historical cleanup. Model finality preserves its original semantic result while physical retirement/result/parent acknowledgment settles. Initial instant-completion model admission retains the native wait compare. Start replay remains available after source retirement. Concurrent admission has distinct retention leases. Deadline overflow is refused for new admission and becomes a visible durable host fault for already-accepted input.

No supported concrete failure remained after those traces and the independent verification below. No new guard weakness was promoted into a finding without an isolated counterexample.

## Fresh independent verification

Private copies of the existing Release outputs identify executing source `e28ef6701cc468d342803425ba77bed4594fb8db`; the relevant product DLL hashes and embedded informational-source checks are saved in `correctness-work/binary-provenance.json`. Git confirms that product/test/tool sources are unchanged from e28 to the reviewed head; subsequent differences are evidence documentation/receipts and untracking the archived probe lock. These are source-equivalent current-product tests, not a newly compiled exact-HEAD product claim.

All **655 freshly executed .NET cases pass, zero failures/skips**:

- Whole BPMN project: **509**.
- EF model checkpoint/admission, migration and schema-gate cases: **59**.
- Additional EF durable-responsibility and protected queue-format cases: **33**.
- Runtime model material/checkpoint/coalescing cases: **48**.
- Native drain ownership/heartbeat/release lifecycle: **6**.

Commands are in `correctness-work/commands.md`; complete per-case TRX and hashes are under `correctness-work/results/` and `correctness-work/test-counts.json`. The first EF selection used two filenames that are partial-class files; its actual 59 cases are reported as such. The intended durable-responsibility/queue methods were separately selected and executed in the 33-case run. Likewise ownership's actual class was separately executed in the six-case run. No zero-match filter is counted as a pass.

Additional read-only verification:

- Current original-pending nested audit passes on closed snapshots: same two model executions/two native workers, original material/full pins/authority/invocation/bindings, same ETL run Running before cancellation and Cancelled afterward, two unchanged historical effects, no outstanding reservation/batch/partition/preparation lease, truthful terminal delivery, and byte-identical ETL files after cold recovery. Output: `correctness-work/nested-audit/validation.json`.
- Current two-family Studio portable verifier passes: four new completed runs, nine old-run observations, twelve coupled whole stores, retained 84/92 native materials/incarnations, unchanged opening JSON/XML and frozen authoring. Output: `correctness-work/studio-audit.json`.
- All **104** frozen assets rehashed successfully. All **94** fixture-pack members match both their pinned archive and the restored local bytes. Restoration controls **6/6** and size controls **2/2** pass; the current tracked evidence size guard passes.
- Independently parsed current retained TRX: actual hosts **46/46** (receipt hash also matches); BPMN509/interchange130/Runtime3100/RuntimeEF1816/DesignEF147; corrected architecture594 and migration508. The initial architecture593+1 failure and zero-executed migration filter remain distinct from the subsequent passing runs. These are inspected existing receipts, not newly rerun whole-project/provider/host gates.

## Limits and delivery state

The current nested journey has compute bindings offline; its two connector effects remain historical. It does not establish current connector execution/retry/new effects or wider ETL acceptance. Studio verification establishes existing saved-work compatibility, not first-class direct-model Studio. The fixed original delayed-compensation deadline has expired; its present replay is not new before-deadline evidence. The fresh native compensation test still exercises settlement while its newly produced retry remains in the future. Earlier actual6bd before-deadline proof keeps its original scope.

No new actual hosts, browser journeys, provider containers, whole solutions or builds were run by this lens. Strict required native-provider CI, the pending owner history-route decision, the existing changes-requested review, branch protection/protected merge and resulting-main verification remain separate gates. Their pending state is not a correctness defect. #2898/#2907/#2908/#2892 and broader Studio/ETL/provider/cluster acceptance retain their stated open boundaries; Orleans, task-runner, alteration/migration and native fault-policy deferrals are unchanged.
