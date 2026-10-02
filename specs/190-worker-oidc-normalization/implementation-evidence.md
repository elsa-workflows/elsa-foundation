# Worker OIDC implementation evidence

Implementation owner: [Task #2308](https://github.com/elsa-workflows/elsa-foundation/issues/2308), prerequisite [#2304](https://github.com/elsa-workflows/elsa-foundation/issues/2304). This is an implementation in progress, not a delivered adapter or Worker profile.

## T001 source and fixture preflight

- Baseline: clean `codex/2308-worker-bearer-normalization` at `c79a00272b7ae73d273d2b6306e62f1eb57fab84`, verified on 2026-10-02. Whole-leaf [claim](https://github.com/elsa-workflows/elsa-foundation/issues/2308#issuecomment-5947888362); fresh issue comments and open PRs show no competing adapter implementation. Project51 is In Progress / Ready for implementation.
- Platform: macOS Darwin arm64; installed SDK `10.0.300`; repository target `net10.0`; JwtBearer package `10.0.10`. No global.json is present. Builds use the shared-machine slot wrapper and remain scoped/serial.
- Spec Kit prerequisite check selected Spec190 and found research/data-model/contracts/quickstart/tasks. Requirements checklist: 12 complete, 0 incomplete. Optional before-implement commit hook skipped because the tree was clean. Existing .gitignore/.dockerignore cover build output, test receipts and temporary database files; no new ignore boilerplate needed.
- Installed-version source ordering checked against [JwtBearerHandler v10.0.10](https://github.com/dotnet/aspnetcore/blob/v10.0.10/src/Security/Authentication/JwtBearer/src/JwtBearerHandler.cs): MessageReceived may return before validation; TokenValidated and AuthenticationFailed may return successful results; handler creates the ticket after TokenValidated. All three boundaries therefore need guarded result admission. Cancellation thrown in validation reaches AuthenticationFailed; the adapter must preserve active cancellation. [Base events](https://github.com/dotnet/aspnetcore/blob/v10.0.10/src/Security/Authentication/JwtBearer/src/JwtBearerEvents.cs) supply ordinary no-op delegates; actual default delegate equality will be tested, not assumed reference identity.
- Source fixture preflight: existing WorkerHttpFixtureHostEvidenceTests exercises Runtime HTTP/SQLite using fabricated authentication and is retained as a regression control, not external-token evidence. ShellActivationHost registers root scope before CShells; AddPersistenceCore uses TryAdd and preserves that ordinary scope. IAM EF has an explicit provider/connection/schema and does not inherit a generic Runtime resource. Existing WorkbenchProcess lacks the nondefault scope seam; the approved non-test WorkerOidcHost executable remains necessary for actual fresh-process proof. No new actor has run yet.
- Root owns production wiring, integration and QA; supporting workers receive disjoint fixture/test allocations and do not run concurrent builds or commit shared changes. The fixed root runtime cannot be switched through available tools; delegates use GPT-6 Luna Extra High as prescribed.

## Execution evidence

The checkpoints below certify local implementation behavior on the recorded source. Delivery, exact-head CI/review and resulting-main verification remain open; earlier preflight limitations are historical.

## Scoped Identity checkpoint — 2026-10-02

Command: `dotnet test tests/essentials/Foundation/Identity/Tests/Elsa.Foundation.Identity.Tests.csproj --filter 'FullyQualifiedName~Oidc' -v minimal --blame-hang --blame-hang-timeout 60s`. Actual terminal result: **154 passed, 0 failed, 0 skipped**, net10.0 on macOS arm64. Log: `/tmp/runtime-2308-identity-focused-reviewed.log`. Baseline remains c79 with the in-progress working-tree source; this is not exact-head CI or final publication evidence.

This completes T002/T003/T004/T006/T013/T014: shared real RSA/handler controls and canaries, opt-in settings/public feature, independently constructed named configurators, opt-in direct feature/provider service resolution, actual declarative valid/blank/absent Audience binding, legacy audience/default compatibility. Positive controls use the selected actual JwtBearer scheme; no signature validator is replaced. A C# feature/provider test and a genuine activated CShells test supply the same required configuration fields and guarded events.

Root review also found and fixed scheme forwarding and callback reload gaps. Final options reject all forwarding, preserve an unchanged callback/type recreation, and reject changed callback/raw-type/namespace configuration through actual options monitors. This does not yet check all T005/T007-T009/T015/T017-T019/T022 obligations: final-publication cancellation, independent every-branch audit, restored mutations and full affected suites remain required.

Earlier integration attempts are retained as failures: test drafts used readonly Result/Ticket APIs and omitted imports; those were corrected without weakening cases. A genuine DI cycle (OIDC validator depending on its own validated IOptions) stalled the scoped run; source inspection identified it, owned process handles were terminated, and the validator now depends only on the registration snapshot. The first executing run passed137/149 with12 failures from test-scheme/shell-name mismatches; the corrected harness then passed148/149. Its remaining new mixed-layout assertion assumed a provider could overwrite an elected default; the test now explicitly selects host first-party defaults before both registration orders and proves they remain authoritative. Existing test objectives and legacy fixtures remain intact.

The real Runtime EF actor is separately compiling/executing. No HTTP/database/fresh-process actor result is certified by this Identity checkpoint.

Identity checkpoint source-file-set SHA-256 (OIDC root/registration and Identity OIDC test files): `85f5a484a970363f5551c60c9ec01fd74336f73e537689907a539beacbb36444`. Final integrated checks will run again after all changes and mutations are restored.

## Real Worker actor checkpoint

On 2026-10-02, command `dotnet test tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj --filter 'FullyQualifiedName~WorkerOidcHostTests' -v minimal` passed **2, failed 0, skipped 0** on the T001 platform/toolchain. Local log: `/tmp/runtime-2308-worker-actor-activation.log`. Working-source OIDC + child executable + parent fixture/test file-set SHA256: `5c08ca69b5161660cc3c624144ccec74c127690fd8bf5ed2ff5a9b0663655312`. This is local actor evidence, not final integrated delivery or CI.

- Actual Kestrel/CShells feature closure, real local discovery/JWK RSA signature/issuer/lifetime/API audience, shell-owned stock JWT middleware and distinct normalized trust type; 19 exact effective features, actual Runtime execute/stimulus routes. The shell uses ordinary nondefault static persistence context, explicit IAM SQLite and a separate named Runtime SQLite resource; real migrations/stores/physical targets are asserted. No fabricated authentication handler is used.
- Anonymous, tampered, wrong issuer/audience, expired and not-yet-valid controls return401 with zero observed actual EF mapping SELECTs and zero runtime effects. Hostile mismatched/global/privileged/across-scope HTTP contexts refuse before lookup; cross-provider/tenant grants, valid unmapped and forged internal claims cannot authorize.
- Owned persisted external-claim grant authorizes execute; exact sole event activity becomes Suspended with one persisted bookmark. Workflow status remains Running, as `WorkflowCheckpointSchedulerWorkHandler.BuildWorkflowStartedStateChange` records; the initial expectation of workflow Suspended was a fixture error, not an adapter regression. Real HTTP stimulus with the same valid token resumes/completes; database state records Completed, completed activity and zero bookmarks.
- Persisting the same rule with empty GrantPermissions makes the next request using the exact same valid token403. First child exits0 and is awaited before a distinct process identity starts from the same parent-selected/child-verified SHA256 executable, configuration and IAM/Runtime files; issuer/key/token remain in the parent. New child reloads the empty grant and completed state, then denies403 again. Actual user/external-identity row counts remain zero.
- A separate real-child negative control supplies a static persistence scope disagreeing with configured tenant. Actual shell activation refuses at activate, the child is reaped, and a subsequent correct child activates with zero mapping/runtime/user/link rows.

Earlier compile attempts exposed missing imports and invalid collection syntax; repaired. The first executing actor stopped at the incorrect workflow-status assertion; the corrected actor passed1/1, then the added actual activation refusal control passed with it2/2. No earlier failed run is counted as successful evidence. Restored mutations, full affected suites, architecture/maps, exact-head review/CI and resulting-main source/package proof remain open.

## Independent review and trust boundary

A read-only production reviewer identified forwarding, callback-recreation, classification and malformed-admission gaps; root integrated final forwarding refusals, frozen ordinary callback/raw-type intent, separate fixed configuration/scope classifications and protected output admission. Direct final-postconfigure callback replacement, service lifetimes and actual handler descriptors have independent controls.

The reviewer also flagged replacement token handlers/validation delegates. The approved [bearer contract](contracts/bearer-normalization.md) explicitly leaves those as trusted host code rather than sandboxing them. A draft blanket refusal was discarded because it would widen the approved compatibility boundary. The final implementation preserves host validation contributions, with a direct retained-host-validator control and explicit README limitation. Real acceptance uses the stock handler/validator with local discovery/JWKs. The signature mutation below demonstrates that the actor actually detects loss of that cryptographic verification; no whole-host protection against arbitrary trusted code is claimed.

The completed focused Identity baseline after direct branch/admission controls passed **201, failed0, skipped0** (`/tmp/runtime-2308-identity-direct-final-baseline.log`). Earlier added-test compilation exposed private public-Theory enum parameters and an incorrect AuthenticationFailedContext constructor; root corrected both. No compile failure is reported as a test result. Full restored suites below will include the final additional handler-lifetime refusal.

## Production mutation controls

Five bounded mutations changed production source, never an assertion or fake fixture. Each selected executed test failed and each source file was restored byte-for-byte before the next mutation. Identity baseline201/201 and real actor baseline2/2 passed before the controls. Final restored affected gates remain separately recorded below.

| Mutation | Executed adverse evidence | Restoration/source |
|---|---|---|
| Cryptographic signature verification bypass | Temporary SignatureValidator parses the token and key-validation delegate accepts that parsed token. The actual Worker actor fails the tampered-token401 assertion: Unauthorized expected, Forbidden observed (`WorkerOidcHostTests.cs`, invalid-token loop). One failed, zero passed/skipped. This proves the invalid signature reached authentication/mapping rather than signature refusal. | Registration-extension SHA256 `eaf6ab33f8ac3cc7b77c802d7f36cd280103464d13de67fae09d621010d2580c` restored exactly; `/tmp/runtime-2308-mutation-signature.log`. |
| Normalization call replaced with raw filtered result | Actual signed-JWT positive request fails; one failed, zero passed/skipped. | Events SHA256 `ff191e28870317bcae6332796d3bb3ff0e06ab51d134e40b9b5a10f313f9589f` restored exactly; `/tmp/runtime-2308-mutation-normalization.log`. |
| Successful callback result fence removed | All three actual-handler bypass controls fail; three failed, zero passed/skipped. | Same events SHA256 restored; `/tmp/runtime-2308-mutation-success-fence.log`. |
| Output admission predicate disabled | Direct malformed identity/type/marker/namespace control fails; one failed, zero passed/skipped. | Same events SHA256 restored; `/tmp/runtime-2308-mutation-output-admission.log`. |
| Final pre-publication cancellation check removed | Deterministic direct cancellation during output admission publishes the guarded output instead of throwing/retaining original principal; the control fails. One failed, zero passed/skipped. | Same events SHA256 restored; `/tmp/runtime-2308-mutation-publication-abort.log`. |

An earlier signature-delegate experiment failed a valid-token positive control, so it did not establish the decisive bad-signature boundary and is not counted above. The revised signature/key-validation bypass produced the specific bad-token assertion failure. Receipt files `/tmp/runtime-2308-mutation-receipts.json` and `/tmp/runtime-2308-mutation-signature.json` retain the commands/filters/source hashes/restoration/results; the revised signature receipt is authoritative for that row.

## Restored local gates and legacy registration review

All five production mutations were restored before the complete affected gates. Commands use `dotnet test <project> -v minimal` on the T001 macOS arm64 / SDK10.0.300 / net10.0 baseline c79 plus this working-tree implementation. No gate below has skipped tests.

| Existing project | Passed / failed / skipped | Local log |
|---|---|---|
| Foundation Identity | 431 / 0 / 0 | `/tmp/runtime-2308-full-identity.log` |
| IAM EF persistence | 191 / 0 / 0 | `/tmp/runtime-2308-full-iam.log` |
| Runtime EF persistence, including real Worker actor | 835 / 0 / 0 | `/tmp/runtime-2308-full-runtime.log` |
| Workbench, including retained first-party host checks | 39 / 0 / 0 | `/tmp/runtime-2308-full-workbench.log` |
| Architecture | 635 / 0 / 0 | `/tmp/runtime-2308-full-architecture.log` |

The exact project paths/commands and terminal results are retained in `/tmp/runtime-2308-full-gate-receipts.json`. Identity includes existing PermissionAuthorizationSemanticsTests operational resource/evaluator propagation and cancellation checks (Spec151), plus new value-bearing callback/mapping/normalizer/malformed-output canaries. These distinguish authentication401, ordinary policy403, operational failures and active cancellation without changing the shared permission evaluator.

Final root compatibility review narrowed duplicate refusal to normalization opt-in. Default-false distinct named bearer registrations retain their legacy behavior and share one inert guard. One legacy success and two mixed-mode refusal rows were added. After this change, the entire Identity project passed **434 / 0 / 0** (`/tmp/runtime-2308-full-identity-legacy-reviewed.log`), and the actual Worker actor filter passed **2 / 0 / 0** (`/tmp/runtime-2308-worker-actor-legacy-reviewed.log`). The other full gates precede this isolated registration change; their scope is not mislabeled as a later exact-head run.

Generated maps were deliberately refreshed with `dotnet run --project tools/maps/Elsa.Maps.Generator -- all`, and generated findings reviewed. Every changed map including manifest and the findings report was explicitly staged. Solution filters were refreshed with the same project's `solution-filters` command. The new WorkerOidcHost executable is IsTestProject=false/IsPackable=false; test-map path counting does not create a new test suite or CI job. Freshness checks and revalidation after integration of newer main remain pending.

## Integrated source checkpoint

Normal merge `18a1d4985` incorporates current main `fc8973e300b84b7be1691ecff89128898ea4436c` without conflicts or replacing its secrets-activation changes. Source commit **`63b137fbca18c723beddd598b6300935f3fa4968`** additionally consolidates the real-handler callback refusal setup into nine Fail/NoResult/Exception cases across all three events. That table passed **9 / 0 / 0** (`/tmp/runtime-2308-callback-table-reviewed.log`), closing the reviewer-noted T017 actual-handler proof gap without adding repeated arrange blocks. The independent read-only review found no production bypass against the approved contract; root reviewed the findings, actor source and complete owned diff. Review did not substitute for execution.

The OIDC/Identity/actor source file-set SHA256 is **`801e8f29db2508c2e7182e1b68a76b9b4f063cd5903d63976c4a302994fc14ca`**: 21 sorted relative paths, each encoded as UTF-8 path, NUL, file bytes, NUL; OIDC .cs excluding bin/obj, Identity *Oidc*.cs, child Program.cs/csproj and parent fixture/tests. All these source files are committed at the stated source commit.

Integrated full existing-project results already terminal on this source and the T001 platform/toolchain: Identity **440 / 0 / 0**, IAM EF **191 / 0 / 0**, Runtime EF **835 / 0 / 0**, including the actual Worker actor. Workbench and architecture are still running and are not reported passed by this checkpoint. Commands are the exact earlier full-project paths with `dotnet test <project> -v minimal`; current terminal receipts are `/tmp/runtime-2308-integrated-gate-receipts.json`, with individual `/tmp/runtime-2308-integrated-<gate>.log` files.

`dotnet run --project tools/maps/Elsa.Maps.Generator -- all`, `check` and `solution-filters-check` each exited0 on the integrated tree. Findings review found only the expected Spec190 task-count snapshot update after the prior staged refresh; merged maps describe both changes. T026 remains open until the integrated architecture run completes. Workbench tests remain live while shared-machine load has exceeded200; no observation timeout is treated as process termination or failure and no unrelated process is stopped.

The newer baseline main CI [36987123862](https://github.com/elsa-workflows/elsa-foundation/actions/runs/36987123862) failed its core-only test leg in `EfSchemaBackfillTests.Content_addressed_rows_are_never_rewritten_and_while_any_is_below_the_target_the_family_is_never_complete` with ObjectDisposedException / SQLitePCL.sqlite3. [#2293](https://github.com/elsa-workflows/elsa-foundation/issues/2293) remains open; its latest alert records this exact main source. The other six source workflows, including nightly, are green on that baseline, but this adapter claims no causal repair of the SQLite incident and still requires its own PR and resulting-main gates.

### First integrated gate completed

The pending source63b137fbc gates completed successfully: Workbench **39 / 0 / 0** (8m12 under high shared-machine load) and Architecture **635 / 0 / 0**. Thus all five integrated full suites passed, alongside maps and solution-filter freshness. Commands/terminal results are in `/tmp/runtime-2308-integrated-gate-receipts.json`; no skipped evidence is counted as passed.

During that run, main advanced to `c331197f981fe8e0ee5003daf3b97b1cfe7d3af3` through Secrets bridge PR2309, changing Workbench's default composition and activity-runtime initialization. Root reviewed the overlap and integrated it normally as **`5aa5cb532`**. Conflicts were confined to generated maps; regenerating from combined source preserves both changes. Reviewed findings show the expected peer-owned Secrets source/test project and feature additions, with no package-version multiplicity. All changed maps including manifest/findings were explicitly staged in the merge; the OIDC/Identity/actor source hash above remains unchanged.

The new architecture graph restored successfully (Release and isolated Debug), and regenerated maps and solution filters pass freshness on this combined tree. All five affected suites are being revalidated because the new default Workbench composition and runtime initialization changed; no second run is claimed passed before its terminal receipt. Current logs/receipts use `/tmp/runtime-2308-bridge-<gate>.log` and `/tmp/runtime-2308-bridge-gate-receipts.json`. T026/T027/T028 and publication remain open pending the complete final combined gate, PR review/CI and resulting-main source/package verification.
