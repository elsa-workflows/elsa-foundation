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

## Still required

No acceptance matrix row is closed by these foundations. Complete host branch/parity and decoded-file boundary coverage, actual host removal preservation, installed-closure worker dispatch, full response contents, CLI command/formatting, real actor/runtime parity and child lifecycle/adverse proofs remain incomplete. T005 and T007–T020, plus the full affected suites, architecture/maps, exact-head review/CI and exact-main delivery gates remain open. The specification stays **In progress**.
