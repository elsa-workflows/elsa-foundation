# Expression Code Intelligence Foundation — Verification

Last reconciled: 2026-10-03 for Program #2310 partial implementation evidence; normal-host acceptance pending

## Program #2310 status

The passing evidence below is historical baseline evidence. It does not prove the current normal host, effective runtime Liquid metadata, runtime-compatible JavaScript help, or the real persisted producer-consumer lifecycle requested by Program #2310. T026-T031 and the corresponding live Studio journey remain open.

## Passing evidence

### Program #2310 implementation checkpoint, 2026-10-03

On `claude/2351-expression-normal-host`, with draft PR #2373 stacked on the Program #2310 planning branch:

- The filtered real-Workbench run built successfully and executed two cases: the missing-Liquid-provider case passed; the persisted-draft JavaScript/Liquid case timed out at the existing ten-minute readiness limit before any test-body assertions.
- An isolated diagnostic rerun of the persisted-draft case also timed out before its test body. A direct readiness request returned HTTP 503 with `status: starting` and `code: shell_activation_pending`; shared-machine load remained above 700. This proves an unfinished warmup, not a passing expression journey or a diagnosed expression regression.
- The fixture resolves the WriteLine input reference from the live activity contract as well as the ReadLine output metadata. Review refinements assert the exact EF design feature and `elsa.db`, plus consistent nonempty document/context revisions across all four tooling responses.
- The scoped `DomainApiCapabilityRegistrationTests` Architecture suite passed all 11 tests, including independent JavaScript/Liquid composition and zero-provider capability omission.
- Manual CI [37095950659](https://github.com/elsa-workflows/elsa-foundation/actions/runs/37095950659) at `bdc2de3bbd1cb51fb69a9fd7dc583557afe07284` passed full locked restore, the full solution build, and all provider jobs. Build & test failed on a stale assertion in this fixture (`Result` instead of the live ReadLine output name `Line`) and an Identity schema-skew SQLite disposed-handle failure in unchanged files. Architecture guards and Core-only were skipped, not passed. The output-name assertion is corrected; full request-envelope acceptance still requires a passing rerun.
- The manual Maps run identified five stale generated files. Deliberate regeneration changed six map files, including the manifest; the generated findings reports were reviewed and unchanged. The final local freshness check passed: `Generated maps still describe the tree.` Maps [37095952008](https://github.com/elsa-workflows/elsa-foundation/actions/runs/37095952008) also passed at the exact `bdc2de3` revision. `git diff --check` passed.
- Copilot round 2 reported no findings at `bdc2de3`; all three round-1 findings had direct replies and resolved threads. This is review evidence, not normal-host acceptance.
- After the output-name fix, CI [37099119656](https://github.com/elsa-workflows/elsa-foundation/actions/runs/37099119656) at `e780a86a151e816a04136ae0ac8fd4eee99c0ce0` passed restore/build/provider jobs but failed solely on this fixture expecting HTTP 201 for the workflow create endpoint's HTTP 200 response. Architecture/Core-only remained skipped. Maps [37099120137](https://github.com/elsa-workflows/elsa-foundation/actions/runs/37099120137) passed at that exact head. The earlier Identity failure did not reproduce; no repair or cause is inferred.
- Studio's paired-host prerequisite [37099572230](https://github.com/elsa-workflows/elsa-foundation-studio/actions/runs/37099572230) confirmed the same create-status mismatch and passed the missing-Liquid control. The browser cases did not execute. The fixture now asserts the source-backed HTTP 200 response; Copilot round 3's hover finding is also corrected to the dynamically discovered leaf name and `Activity output` documentation, retaining all revision checks and adding output-type assertions. Root review and an independent static contract audit found no further mismatch; execution of this correction remains pending.
- CI [37102959661](https://github.com/elsa-workflows/elsa-foundation/actions/runs/37102959661) at `b08679a4c71ce994a89f266235e7c385de9ed2f0` passed Build & test, including the corrected real-host fixture, all provider jobs, and Architecture guards. Core-only failed in unchanged `EfSchemaBackfillTests.A_run_killed_mid_way_is_finished_by_another_host_with_the_table_an_uninterrupted_run_leaves` (`between batches`): a disposed `SQLitePCL.sqlite3` handle replaced the expected simulated host-kill exception. This is not a green full gate; no repair or causal connection to this work is inferred. Maps [37102959634](https://github.com/elsa-workflows/elsa-foundation/actions/runs/37102959634) passed at that exact head.
- The next coverage refinement explicitly composes an empty provider collection and asserts all four canonical tooling links, using their advertised URLs for the live requests instead of hard-coded routes. This addresses Copilot round 4's two body-only coverage findings. The affected-tree locked Release runs pass both real-host cases (2/2) and all scoped composition cases (11/11); exact-head hosted execution/review remains pending.

T026-T031 remain open. The coordinated Studio browser suite collects its four normal-host scenarios but has not yet produced live passing evidence.

### Historical baseline

| Suite | Result |
|---|---:|
| `Elsa.Expressions.Tests` | 108 passed |
| `Elsa.Workflows.Design.Tests` | 344 passed |
| `Elsa.Workflows.Design.Api.Tests` | 89 passed |
| `Elsa.Workflows.Publishing.Api.Tests` | 459 passed |
| Expression-tooling and custom-host architecture filters | 4 passed |
| `dotnet build Elsa.Server.slnx --no-restore --verbosity minimal` | Passed with 0 errors |

The focused evidence covers exact per-expression-type provider routing, JavaScript runtime globals, Liquid symbols, dotted/nested value shapes, authoritative persisted context, policy filtering before paging, host-replaceable authorization and revision fingerprints, descriptor/capability composition, semantic validation state mapping (including validator faults and caller cancellation), publication fail-closed behavior, and Test Run acknowledgement/metadata.

## Full architecture suite

The complete `Elsa.Architecture.Tests` run passes all 320 tests.

The three custom-host composition failures initially exposed by this run were corrected by making the persisted authoring-context source safely optional when a host omits draft persistence. All affected composition tests now pass.

## Contract audit

- Context and symbols are server-authoritative, permission/host-policy filtered, metadata-only, bounded, and `no-store`; hosts can replace `IExpressionAuthoringAuthorizationPolicy`, and its opaque revision participates in stale-result protection.
- Catalog paging occurs after filtering.
- Value-shape members are inlined to depth four; lazy retrieval is explicitly unsupported by v1 descriptors.
- JavaScript, Liquid, and future providers own their own globals/functions/variables; there is no generic “Elsa globals” catalog.
- Known validation errors gate Test Run/publication; unavailable Test Run validation supports explicit acknowledgement only when the result contains no error diagnostics; publication remains fail-closed.
