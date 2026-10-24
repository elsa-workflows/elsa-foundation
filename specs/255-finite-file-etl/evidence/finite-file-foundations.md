# Finite-file implementation checkpoint — 20 October 2026

This records successive component, design-persistence and bounded-runtime checkpoints for #3109, not complete native/Studio acceptance. The branch remains a draft implementation lane.

## Reviewed corrections

The self-review loop preserved saved artifact versions instead of replacing them with the current version; bounded the complete artifact envelope and forged-artifact deserialization; retained exact string/whitespace lookup semantics; recompiled publication requests rather than trusting self-hashed JSON; kept genuine storage failures distinct from revision conflicts; corrected SQLite configuration-name versus provider-name comparison; and corrected two test-fixture mistakes without attributing them to storage behavior.

Bounded worker reviews supplemented root integration/review. They are not external GitHub approval. No independent approval is required by the owner's standing delegation; platform protections remain in force.

## File publication finding and safeguard proof

An integrated test observed two different-content writers both reporting successful `File.Move(..., overwrite: false)` publication to one destination. The .NET 10.0.8 [Unix implementation](https://github.com/dotnet/runtime/blob/v10.0.8/src/libraries/System.Private.CoreLib/src/System/IO/FileSystem.Unix.cs#L193-L214) checks destination existence before a replacing rename, so that API does not establish the required concurrent create-only boundary. Retained logs: `etl-primitives-green.log` (78 passed/1 failed) and `chunk-race-diagnostic.log` under `/tmp/elsa-program-20261020-reprioritization/`.

The feature now uses atomic hard-link creation on Unix, with no copy fallback, and no-replace move on Windows. The staging file is already flushed, closed and hashed before publication; cleanup removes only its own staging name after the writer unwinds.

The attempted mutation back to `File.Move(false)` passed four cases on that later run. It did **not** establish a deterministic regression bite; the earlier observed race and source mechanism remain separate evidence. Removing create-only behavior by allowing replacement failed all four concurrent cases; restoration passed all four. The latter is the deterministic safeguard-removal proof, not a claim that the intermittent reproduction fails on every run.

For `Extensions/Etl/src/Runtime/Output/FileChunkStore.cs`:

- Original/restored SHA-256: `37d737cfe1c76e8bf2ea6b0b58eeaf3c990fcda0cab4825c35418bc7e6324320`.
- Replacement-allowed mutation SHA-256: `f52a22118039a218af5c014e0f356bc0690e1dff2d70c9d1d3cc6e13953554df`.
- Command: `dotnet test Extensions/Etl/tests/Tests/Elsa.Etl.Tests.csproj --no-restore --filter FullyQualifiedName~Concurrent_identical_writers --logger 'console;verbosity=normal'`.
- Receipts: `chunk-no-replace-proof.json`, `chunk-no-replace-mutated.log`, `chunk-no-replace-restored.log` in the same temporary evidence directory.

## Verification boundary

Root's integrated component/design-store run passed 108 tests with zero failures/skips. It includes a real plain host AutoMigrate → publish → stop → separate Validate host → reload journey and SQLite model/migration consistency. Parsing, transforms, preparation, scopes/pins and output effects are covered. Focused preparation passed 16/16; parser-with-offsets passed 15/15. Parser tests were observed failing against its empty implementation; no red-first execution is claimed for the preparation suite.

The root checkpoint review ran four review/fix passes and stopped with no actionable finding in the included component/design-store boundary. Final local gates:

- ETL tests: **108 passed, 0 failed/skipped**, after extracting shared design/host pipeline test setup.
- Complete architecture suite: **579 passed, 0 failed/skipped**. This includes the new exact EF package admission, real feature composition, all runtime-library loading, schema stamps, internal visibility and solution layout.
- New-project isolated Debug locked restore passed; normal project/test restores supplied Release/default assets. All five new projects have tracked lock files; the architecture test's lock changes contain the four intended ETL project references.
- Deliberate map refresh and maps freshness check passed. Reviewed changes add four source projects, one test project, two design features and the ETL domain/spec task state; there are no new map findings.
- Source and staged diff checks passed. Existing unrelated warnings in the broader dependency tree are not described as newly introduced ETL warnings.

Initial architecture runs identified wiring/freshness gaps while the worktree was changing; those failed runs are not passes. In particular the stale dependency dataset was regenerated before the final 579/579 run. Final exact-commit CI and the later product journey remain separate gates.

No durable ETL run/partition acceptance, native invocation/continuation, actual two-worker recovery, API/Studio or browser qualification exists yet. Partial snapshot/staging retention, platform/filesystem durability and live worker memory accounting remain explicit operational boundaries. Parent #2908 and Program #2892 remain open; Orleans remains deferred and Proto.Actor is not selected.

## Integration with main61abce1

Merged main `61abce1dbb0c010817c44ca28b22ed031bc52803` normally into the existing implementation branch after GitHub reported a conflict and did not start new-head CI. Explicit conflicts were the active-spec selector and generated maps. Kept spec255 selected for this branch and regenerated maps from both sets of source; all native compensation #3089 and Connection creation #3105 behavior is retained. No ETL production source changed during integration.

Integrated local validation: **108 ETL tests, 19 bookmark-boundary tests and 579 architecture tests passed**, zero failures/skips. Maps freshness, reviewed generated findings, solution-filter validation and diff checks passed. The now-landed CompensationScope is added to the pending native child-completion proof; this does not promise automatic reversal of ETL effects. Current-head GitHub CI and the remaining product acceptance are still owed. Historical main-red issue #2293 remains with its existing owner and cause investigation.

## CI dependency correction

CI run `39414984392` on `2ed6b3b8c9656da9f422c2eaa7271343fda7d4d4` failed during locked restore, before compilation or tests. Adding a central `Microsoft.Extensions.Hosting` package pin for the ETL host test unintentionally promoted an existing probe's transitive Hosting dependency, invalidating its lock (`NU1004`). The earlier focused restores did not include that tool project. Generated maps passed, but the downstream architecture/core jobs were skipped; they are not passing evidence.

Removed the new central Hosting pin and the ETL test's corresponding PackageReference. The test now uses `Microsoft.AspNetCore.App` as a test-only FrameworkReference, following the existing Secrets host-test pattern. No probe source or lock changed, and this does not resume deferred Orleans work. The ETL test lock drops the unnecessary Hosting implementation package closure; production ETL dependencies are unchanged.

The correction passed the complete CI-shaped locked Release and isolated Debug restores (`bash tools/architecture/restore-ci-project-graph.sh --locked-mode -p:WarningsNotAsErrors=NU1603`), all **108 ETL tests with zero failures/skips**, and regenerated maps freshness. Logs are `host-framework-locked-restore-verified.log`, `host-framework-tests.log`, and `host-framework-maps-check.log` under `/tmp/elsa-program-20261020-reprioritization/`. Fresh CI on the corrective head remains required; the failed run is not repaired by a local pass.

The corrective checkpoint also normally merges main `5a08c2148`, which stops tracking the active-feature pointer. Resolved the modify/delete conflict by retaining main's deletion; spec255 remains selected in the ignored local pointer. The complete architecture suite then passed **579/579, zero failures/skips**. Two root correction/integration review passes found no remaining actionable finding in this dependency-only delta. The central package file and the probe source/lock are identical to integrated main. Runtime contracts and scope compatibility remain subsequent work, not evidence from this checkpoint.

## Durable runtime checkpoint

Core contracts now freeze exact nullable-tenant/required-partition scope, artifact and native result address; admission pins source/lookup/main/error revisions. Shared identities cover invocation, batch and output keys. Scope identity uses lossless UTF-16 code units rather than replacement-prone UTF-8; the private runtime-store string codec retains those units in scope, run IDs and native addresses across JSON/database reopen, without rewriting the authored pipeline JsonElement. This does not change authored pipeline JSON.

`EtlRuntime` registers bounded background attempts and executable validation without design services. `EtlRuntimeEntityFrameworkCore` supplies a separate runtime context/module/history/migration. Fresh SQLite schema/reopen tests assert no design tables; feature DI asserts no compiler/design-store registrations. The persistence package still references the design assembly: this is feature/database isolation, not a design-free package closure. No other provider qualification is claimed.

Preparation snapshots/index metadata and independently leased partitions are durable. Batches are reconstructed from immutable input; reservation charges both record and input-byte quotas before physical effects. Renew/release retain ownership and fence checks without incorrectly requiring the old cursor; reserve/accept still require the observed cursor. Fenced acceptance commits output manifest, cursor, counters and quota settlement together. Last acceptance or empty preparation atomically records a terminal result and owed outbox. Cancellation before admission creates a tombstone; delivery acknowledgements accept only consumption/already-consumed/proven-parent-terminal dispositions. Actual native delivery/cancellation enrichment remains unimplemented.

Process-local attempts acquire record/input/lookup/output allowances until actual unwind. Heartbeats use separate service scopes/contexts, stop before claim release and cancel the attempt on loss. Store leases have a five-second minimum against the one-second renewal cadence; expiry is not evidence that a worker has stopped. The plain-host worker waits for root host startup. In CShells the same idempotently started worker uses the Tasks Start/Stop lifecycle, after Prepare-phase migration, and does not wait on a shell-local copy of the host lifetime token. It alternates preparation/partition preference across explicitly configured scopes.

A real compiler-produced fixture runs across two actual hosts sharing SQLite/filesystem storage and is reopened by a separate host. It verifies two accepted rows, one rejected row, one filtered row, typed JSONL, safe error JSONL, ordered accepted manifest, zero remaining quota and retained terminal delivery. Three more host tests cover empty input, bounded diagnostic fields, missing bindings and cancellation-before-admission. All hosts are in one test process. There is no two-process recovery, native composite, API/Studio, browser or final materialization acceptance yet.

### Runtime self-review corrections and regression evidence

Focused red/green tests exposed and fixed cursor-sensitive renewal, replay accepting changed counters or duplicate routes, nonempty work claiming zero bytes, and scope JSON replacement. The runtime reader checks the raw UTF-8 envelope bound before making a raw JSON string copy. Publication, runtime reader and tests now share one mechanically tested artifact-ID function. Long optional field names are omitted from bounded run diagnostics instead of preventing terminal settlement; row data remains excluded.

The acceptance fence safeguard was also removed temporarily in the real EF store. Both the initial ownership/fence check and the corresponding pre-commit fence check in `AcceptBatchAsync` were removed; no other method was changed. The same two takeover tests failed at the stale acceptance assertion: expected `LeaseLost`, actual `Applied`. Restoring the exact original source made both pass. The fake clock and separate contexts make this independent of machine load. One test goes directly to acceptance after takeover; the other first probes renewal/reservation refusal. The restored tests also check unchanged outstanding quota, empty accepted manifest and unchanged cursor.

- Source: `Extensions/Etl/src/Persistence/EntityFrameworkCore/Stores/EfEtlRuntimeStore.cs`.
- Original/restored SHA-256: `a7f93fab8e461f3b04dc977e23f940d8b112bbec3201487dafc539f8c5d06c92`.
- Mutated SHA-256: `2b2fcb8ed30a05bedcf6e685c194403ae1e1fe1d56e63d15a5c436ee1412de16`.
- Command: `dotnet test Extensions/Etl/tests/Tests/Elsa.Etl.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~Reused_context_refuses_old_acceptance --logger 'console;verbosity=normal'`.
- Mutation exit code 1, 2 failed; restored exit code 0, 2 passed. No mutation is committed.
- Retained receipts: `runtime-final-safeguard-proofs.json`, `runtime-final-fence-mutated.log`, `runtime-final-fence-restored.log` under `/tmp/elsa-program-20261020-reprioritization/`.

The previous pushed dependency checkpoint `4860105db4d8808a85e22c1a02351840209adffb` completed [CI39417076067](https://github.com/elsa-workflows/elsa-foundation/actions/runs/39417076067) and Studio39417076078 successfully, including backend, architecture, core-only and maps. Scheduled-only EF/container/Secrets jobs were skipped. Those green checks do not validate the subsequent runtime source in this checkpoint; fresh-head CI remains required.

### Final host/provider integration corrections

The first architecture run reported **578 passed / 1 failed**, because its minimum-settings fixture omitted the new required snapshot root. The fixture now supplies a temporary root; no production fallback was added. Focused feature-composition then passed both tests.

Source review confirmed CShells does not start shell-owned `IHostedService` registrations. `EtlRuntime` now depends on Tasks, and its same worker instance is registered as a shell `IBackgroundTask` and a plain-host hosted service. Start is idempotent; the activity/activation caller token does not become the generation lifetime. A real CShells host migrates, admits and executes a compiler-produced artifact, then stops and awaits its worker on disposal. An intermediate real-shell test failed because a shell-local copied `IHostApplicationLifetime` never signaled root readiness. The shell Tasks Start phase is the correct readiness boundary; the corrected test passes. This is a real shell lifecycle proof, still within one test process.

A separate actual-host run observed `Preparing` paired with a terminal result. Command-interceptor barriers reproduced the split-read race in FindRun/ListRuns and the post-commit admission-view boundary. Find/list now assemble run/prepared/terminal in one joined SQL statement; partition/owed-reservation views do the same and retain the parent scope/residual validation. New admissions build their returned view inside the write transaction before commit. This avoids reader-held SQLite write transactions while preserving coherent views. The root extracted shared query and barrier setup; its initial record-constructor projection did not translate in EF, was corrected to a member-initialized projection, and the full suite then passed.

The private codec was widened from scope-only encoding to every persisted CLR string value after separate run/address round-trip tests failed with fingerprint mismatch. It uses tagged lossless UTF-16 projections, accepts no legacy/plain-string fallback, and leaves pipeline JsonElement tokens unchanged. Current persisted shapes contain no string-keyed dictionaries. A host lookup-cell ceiling (default one million per attempt) also bounds declared rows × schema columns before materialization; a wide sparse schema was observed passing without this combination check and refusing with it. Byte budgets remain logical allowances, not a heap/RSS guarantee.

Acceptance now snapshots at most two caller-owned output effects before any await. Removing only that bounded snapshot and changing the caller's second array entry during the first manifest query committed the changed output key. The precise regression failed its expected-key assertion; restoration passed. On the same final source SHA-256 `a7f93fab8e461f3b04dc977e23f940d8b112bbec3201487dafc539f8c5d06c92`, the effects mutation hash is `3337e1a3657a5eeeb49e52e603c72746563af966b68f1b1ff1432a7e82fd12ae`. Command filter: `FullyQualifiedName~Acceptance_snapshots_bounded_effects`, with the same Release/no-restore command prefix as the fence proof. Mutation exit 1 / one failed; restoration exit 0 / one passed. The fence proof was also repeated on the final source: two failed with the checks removed, two passed after exact restoration. Neither mutation is committed. Logs are `runtime-final-effects-mutated.log` and `runtime-final-effects-restored.log`; `runtime-final-safeguard-proofs.json` records both source hashes and commands.

### Final local verification

The runtime checkpoint completed seven review/fix passes across contracts/identity, attempts/allowances, EF transition validation, persisted strings, shell lifecycle/composition, coherent reads/caller ownership, and final integration. The final pass found no remaining actionable defect in the included checkpoint. Worker findings were reviewed and integrated by root; they are not an external approval. Native/API/Studio/recovery acceptance remains open.

- Release ETL build/test: **180 passed, 0 failed/skipped** (`runtime-final-release-tests.log`). Includes real host and shell lifecycle, fresh migration/reopen, canonical artifacts, exact scopes, reservations/fences, accepted output, quota, cancellation tombstone and retained terminal-outbox component evidence.
- Complete architecture suite: **579 passed, 0 failed/skipped** (`runtime-final-architecture.log`), including actual feature composition, EF/schema admission, public API and evaluated dependency/lock guards.
- Complete CI-shaped locked Release and isolated Debug restore: passed (`runtime-shell-locked-restore.log`). No central package pin, probe source or probe lock was changed by this runtime checkpoint.
- Both final-source safeguard-removal proofs failed precisely and passed after byte-identical restoration, as recorded above.
- Generated maps were deliberately refreshed and their findings reviewed. Solution-filter and map freshness are checked on the final staged snapshot; fresh GitHub CI is required after push.

There are **14/36** completed spec tasks. Separate terminal native delivery/consumption, cancellation enrichment, composite continuation, final materialization, two-process interruption recovery, API/Studio/browser and resulting-main checks are still owed. This remains a draft, active #3109 lane; no parent/program completion or merge is claimed.

## Integration with main d628fc0

The source checkpoint `4269796d80ade139dfeaa9e18a6533dfcb7d8137` was pushed, then GitHub reported conflicts. The authoritative main ref had advanced to `d628fc0da1e44ce5ea61768fbf6377b43919bacb` even though the PR's baseRefOid still displayed 5a08. No new-head backend/Studio CI pass was inferred from advisory checks or old runs.

The normal merge retains main's #3031 reusable-activity publishing and owner-approved #3116/spec257 map policy. Documentation maps/manifest/findings stay deleted from the tracked tree and are generated locally or published by CI. Only `docs/maps/dependency-map.json`, its freshness check and the solution filters stay committed. The constitution conflict combines main's 6.0.3 published-map link amendment with ETL's additive 6.1.0 domain pin; both impact reports and the existing provisional exclusions remain. The complete ETL source tree is byte-identical to 4269796; both safeguard proofs still match store hash `a7f93fab8e461f3b04dc977e23f940d8b112bbec3201487dafc539f8c5d06c92`. The workflow file is identical to integrated main; this unit did not redesign workflows or change permissions.

Integrated Release ETL tests passed **180/180, zero failures/skips**, matching `--list-tests`. Full normal and locked CI-shaped restores passed. The new committed dependency-map check, solution-filter check and catalog link check passed (zero broken relative links in 132 catalogs/readmes); the ignored local maps were generated and reviewed. Diff checking against the integrated main passes. Diff checking the imported main changes against the old branch reports pre-existing whitespace in spec246, which is outside this unit and is preserved. The full integrated architecture suite passed **579/579, zero failures/skips**. `--list-tests` exposes 570 discovery names: the two non-serialized theory placeholders expand to five domain-capability rows and six collectible-metadata rows; a focused no-build run executed all eleven, accounting for the nine-name difference. No skipped coverage is inferred from that comparison. One integration review pass found no remaining actionable finding in the merge delta. Logs use the `runtime-main-d628-` prefix under the retained temporary evidence directory. Fresh exact-head CI remains required after the integration push. Historical main-red #2293 remains with its existing owner; this merge does not claim its cause is resolved.
