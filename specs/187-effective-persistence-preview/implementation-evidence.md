# Effective persistence preview implementation evidence

Issue [#2177](https://github.com/elsa-workflows/elsa-foundation/issues/2177), implementation branch `codex/2177-effective-persistence-preview`, foundation base `d87a5bcb1291b76348ae48aa14740a73e9eb46b5`.

## Foundations: T001–T004

Executed on 2026-09-30 in the existing CLI test project. No new test project, provider container matrix or workflow change.

- Initial protocol/source baseline: 59 executed, 40 new cases red on missing candidate APIs, 18 pre-existing cases and one new legacy-reader control green.
- Host-response framing baseline: 27 executed, all red on the missing response reader after repairing the test harness compilation error.
- Capture owner baseline: seven executed, all red on missing ownership API.
- Independent capture review exposed opener/null-path sanitization: six new cases executed red, then fixed.
- Count admission before Build: one of two cases red on wrong refusal precedence. Identity admission: four of six cases red (129-character IDs, case collisions and cross-set overlap), while exact128 selected/removed controls passed. Fixed through shared finite selection admission before Build; worker retains independent admission and separate sorted-wire checks.
- Final focused foundation run: **146 executed, 146 passed, zero skipped**, with temporary reflection removed from the new response tests. Existing legacy request/source behavior controls remain green. Real supported-name unused Unix FIFOs refuse before content reads on macOS.

Reproduce the focused run:

```sh
dotnet test tests/essentials/Cli/Tests/Elsa.Cli.Tests.csproj --no-restore \
  --filter 'FullyQualifiedName~WorkerProtocolTests|FullyQualifiedName~CompositionFileSourceTests|FullyQualifiedName~CompositionInspectionCaptureTests'
```

Root reviewed the complete foundation delta. Independent reviewers audited response framing and capture ownership/admission; reproduced findings were corrected. The frontend and worker are distinct assemblies, so their shared private-transport admission predicate requires public accessibility without adding friend assemblies or duplicating the protocol grammar.

## Meaningful foundation mutations

Each source mutation was restored in `finally`; after restoration, the 140-case run passed. Six additional null/excessive-selection admission cases then brought the final focused run to146 passing cases.

| Temporarily broken behavior | Executed | Failed as expected | Control passes |
|---|---:|---:|---:|
| Enforcing actual file-byte limit | 1 | 1 | 0 |
| Rechecking owned supplied intent before result | 2 | 1 | 1 |
| Correlating host invocation token | 16 | 1 | 15 |

These failures executed assertions against deliberately broken production behavior; they were not compilation failures.

## Actor, host and removal baselines: T005–T006

Executed in the existing CLI, Planning and migrations projects on 2026-09-30; no new suite or provider container was introduced.

- Real built CLI journeys: both import/edit/accept and workspace-profile/edit/accept reached the accepted input, then their preview expectations failed on the missing `composition inspect` command. Both corresponding missing-trust expectations failed on that same absent command. In the combined run, these four new actor expectations were red; three pre-existing resource-aware live CLI checks and three new correlated-input-refusal admission cases passed (10 executed, zero skipped).
- Host capability/public operation: repaired test-only compile errors before counting evidence. All seven expectations then executed red on the missing streamed host capability or public operation. The real fixture separately composed its descriptor closure before attempting the missing operation. Temporary operation-type reflection must become direct public calls when production lands; reflective capability negotiation remains under test.
- Independent review tightened correlated input refusals: six feature/resource-bearing input-refusal cases executed red before the reader was changed to admit code-only errors. The malformed selected-file host expectation now checks version, both correlation tokens and the exact closed top-level error envelope.
- Restored focused capture/framing run after that correction: **155 executed, 155 passed, zero skipped**. This is source/input/protocol evidence, not a passing host or actor journey.
- Shared candidate builder removal baseline: the first four cases yielded three red and one settings-preservation control green. Two additional base-disabled/overlay-idempotence cases brought the baseline to six executed, four red and two green. After the narrow union-of-explicit-removals fix, **all 16 existing and new candidate-builder cases passed, zero skipped**. Independent and root review found no remaining defect in that delta.
- Deliberately removing the explicit-removal union made the absent-feature assertion fail while the existing-overlay-false control passed (two executed, one red, one green). Restoring the source returned all 16 candidate-builder cases to green. The shared builder is corrected; survival of that false declaration through the real host merge remains an integration gate.
- Worker capability-negotiation baseline: nine cases executed red on the missing binder; they require exact independently versioned streamed negotiation without any legacy fallback.

The new worker capability-negotiation expectations are tracked separately from host production. These baselines do not close T005 or any acceptance-matrix row; per-owner branch, actor integration, configuration parity and adverse/process obligations remain open.

## Host producer and capability checkpoint: T007–T008 in progress

The new host-owned version1 streamed capability and public sealed operation now exist. They build configuration only from supplied post-edit bytes, verify the separate loaded host identity, use the declared composer and real CShells dependency resolver, reconcile selection before shared EF preparation with configured-value checks enabled, and emit a safe logical projection. The old v2 path still uses `verifyConnectionValues:false`. Both paths now share the same narrow defaults-to-selected-shell merge helper; the independent runtime comparison fixture remains separate.

- Initial integrated host execution: 10 cases executed, nine passed; the positive projection case exposed an incorrectly sorted expected field list in the test harness. After repairing that list, the 10 candidate and 10 existing configuration-probe cases passed (20 executed, zero skipped).
- Independent producer review found unreachable correlated oversize refusal and ambiguous duplicate correlation. Four new expectations executed red before corrections: one oversized correlated response and three duplicated candidate/token fields. The host now parses only the bounded prefix, emits code-only `candidate-request-too-large` when unique exact independent tokens are available, and emits no bytes when they are ambiguous or unavailable.
- Corrected host run and subsequent shared-merge restoration run: **13 candidate cases plus 10 existing configuration-probe controls, all 23 passed, zero skipped**. Operation behavior tests directly call the public operation; reflection remains only for actual capability discovery/version negotiation.
- Meaningful producer byte-limit mutation: replacing the fixed read limit with rented-buffer length made the counting test read 8,392,704 bytes instead of the required 8,388,609. That assertion failed while token-reuse refusal stayed green; restoring the source returned all 23 host/probe cases to green.
- Exact independently versioned worker capability binding: nine initial expectations ran red on its missing API; after implementation and two additional generic-method/wrong-version-type controls, all 11 direct public-surface cases passed. The selected compatibility run included all existing ToolingEntryPoint controls, 155 capture/framing cases and three resource-aware live CLI controls: **186 executed, 186 passed, zero skipped**.
- Independent and root static review cleared the corrected producer, shared merge and strict binder. The worker binder does not yet dispatch candidate requests. The CLI actor tests still require the missing inspection command.

This checkpoint proves a bounded host operation and its focused logical projection, admission/correlation controls, and selected legacy compatibility. It does not establish the full runtime-preparer parity matrix, every producer branch/count boundary, installed-closure dispatch, final worker/client content validation, human/JSON journey, process cleanup, or complete acceptance. T005, T007–T020 remain open. No acceptance row is closed and the specification is not Implemented.

## Installed-closure worker checkpoint: T008 in progress

The candidate-only worker now dispatches through the exact capability without Restore, live configuration readers, legacy fallback or raw loader diagnostics. It privately observes captured deps bytes, the selected runtimeconfig and host assembly, package-state presence/content, selected install declarations and stateless completed-package identity before dispatch. Nuplane's existing state/probe loaders remain the loading authority. The contract records the state reread/ABA, pre-Main runtimeconfig and unobserved dependency-asset limits; this is neither an atomic snapshot nor deployed runtime parity.

- Initial direct invocation/closure/dispatch baseline: **35 executed, all 35 red** on missing APIs; captured dependency parsing separately had **five executed, all five red**. Harness compilation failures were repaired before counting either baseline.
- Real built worker baseline: **three executed, all three red** on the missing mode branch. The corrected branch reaches the real host producer and returns its root-default SQLite resource projection; both composer console canaries are suppressed. Database-file, DbContext-construction and post-migration-action sentinels remain absent. Other child controls refuse an old host capability and Restore before dependency-path access. These are handcrafted private-transport controls, not proof that the frontend used the candidate builder.
- Review exposed and reproduced timestamp-only completed-package drift, host-response disposal, strict SDK project/reference alias admission, legacy reader-refusal sanitization and null feed metadata. The alias fix admits repeated identical normalized runtime asset keys but refuses competing paths for one case-equivalent assembly identity.
- The corrected selected compatibility run yielded **198 executed, 198 passed, zero skipped**: direct closure/dispatch/invocation controls, captured deps parsing, existing worker protocol and entry-point controls, the three real candidate child controls, existing resource-aware live CLI checks and legacy Nuplane package-root checks.
- Final independent review found synchronous fatal-exception reflection classification and an outdated WorkerLaunchTests property inventory. The focused baseline executed nine cases: four red (synchronous constructed OutOfMemoryException/AccessViolationException, asynchronous malformed image and the inventory), five green, zero skipped. Root added fatal-inner stack-preserving rethrow, retained safe malformed-image classification, and updated the Candidate inventory without changing launch-argument or Restore objectives. The corrected compatibility run yielded **207 executed, 207 passed, zero skipped**. Independent corrected-delta review found no remaining material issue in those corrections; command/process obligations remain open.

- Meaningful console mutation: temporarily removing candidate stderr suppression made the real built-worker assertion fail on the synthetic composer stderr canary (**one executed, one failed, zero skipped**). Program.cs was restored in finally. The restored compatibility run passed **all 207 cases, zero skipped**.

The 198-case run predates those new fatal-boundary tests; the 207-case run includes them and the retained launch controls. The following route checkpoint now exercises both candidate-observed routes; it does not establish deterministic in-load drift or atomic package identity. Full response-body validation, bounded production process ownership and the four actual frontend actor expectations remain open. No acceptance-matrix row is closed.

## Real candidate package routes and response-content baseline

A bounded supporting worker prepared the shared install/state fixture and three route expectations on an isolated branch. Root reviewed and integrated the test-only commit, repaired one xUnit collection-overload compilation error before counting any baseline, and added corrupt-state/corrupt-probe controls. The general NuplaneHost stays composer-free: adding a declaration there would change ordinary unscoped tooling from legacy-only to host-composition-unavailable. Instead the existing declared-composer ResourceAwareLiveHost now carries test-only Nuplane/Nuplane.Loading references using the unchanged central pins; its lock was recomputed normally, including the transitive versions already required by that Nuplane closure. No production package pins changed. No new project, provider matrix or workflow was added.

- Combined initial executed baseline: **41 executed, 35 failed, six controls passed, zero skipped**. The two valid installed-route expectations failed on absent Nuplane in that fixture; all three package-refusal controls passed. The initial closed-content expectations accounted for the other 33 failures.
- After fixture references and review refinements, **69 executed: 35 passed, 34 expected response-content failures, zero skipped**. All five real candidate routes passed: state-backed markerless install, marker-backed stateless install, incomplete install refusal, corrupt state-backed assembly refusal and corrupt probe-backed assembly refusal. All 15 prior candidate-worker controls, six ordinary Nuplane package-root checks and three resource-aware live CLI controls passed. No package-root bytes/paths/ready timestamps changed, no staging directory appeared, no DB/context/action marker appeared, and no console/path/private-value canary escaped.
- Meaningful route-loading mutation: skipping both actual candidate loaders after metadata observation made the corrupt-state and corrupt-probe assertions fail, while both valid controls and the incomplete-install refusal remained green (**five executed, two failed, three passed, zero skipped**). Both source files were restored in finally; the restored focused compatibility run passed **212 executed, 212 passed, zero skipped**. The 40-case response-content class is intentionally separate and still has 34 expected failures.
- Final response-content baseline is **40 cases: 34 red, six valid controls green**. Valid controls include resource, legacy, empty, slash/plus identities, unsupported resource-scope partial projection, and the exact combined row boundary (1023 participants plus one finding). Refusals cover closed fields/types, identity/provider/selection/scope enums, selection equality/order/case/disabled consistency, truthful evidence, nullable legacy targets and the 1025 combined-row overflow. The order-only negative now starts from a valid partial projection. These are executed missing-validation failures, not parser readiness.
- Independent review found the current producer can emit 1024 participant rows plus provenance finding(s), exceeding the contract's combined 1024-row bound. Correcting and proving that producer boundary remains part of T007/T012/T013, alongside the consumer. No broader readiness claim follows from framing validation.

Independent review cleared the route fixture extraction and corrected adverse cases. The worker supporting model was `gpt-6-luna` at xhigh; root retains integration/QA. T005 and T007–T020 remain unchecked and the specification remains In progress. Actual command/actor, complete runtime-preparer parity, strict producer/consumer contents, production process ownership, maps/architecture/full affected suites and delivery gates remain open.

## Current map checkpoint

After the route/content baseline commit, root refreshed all generated maps and ran the byte-for-byte map check successfully. Seven map files changed: the candidate fixture's existing diagnostics project references and new Nuplane package references, owned test-file links, spec In progress/task state, and the corresponding manifest/dependency counts. No linked test file was removed. Both generated findings reports were reviewed and remain byte-identical: no new runtime-to-design signal or direct package-version cluster. This is current navigation evidence, not the final architecture/CI/delivery gate; later source changes require a fresh check.

## Still required

No acceptance matrix row is closed by these foundations. Complete host branch/parity and decoded-file boundary coverage, actual host removal preservation, installed-closure worker dispatch, full response contents, CLI command/formatting, real actor/runtime parity and child lifecycle/adverse proofs remain incomplete. T005 and T007–T020, plus the full affected suites, architecture/maps, exact-head review/CI and exact-main delivery gates remain open. The specification stays **In progress**.
