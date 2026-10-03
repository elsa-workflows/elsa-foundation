# Expression Code Intelligence Foundation — Verification

Last reconciled: 2026-10-03 for Program #2310 M2 local conformance verification; committed-head CI/review, delivery and final human acceptance pending

## Program #2310 status

T026-T027 pass at the exact M1 pair recorded below. T028 is implemented with the local M2 evidence below; its committed-head CI/review gate is pending. Effective Liquid metadata and the final coordinated language-depth gate (T029-T031) remain open. Historical baseline evidence remains separate. All program PRs remain draft and unmerged; final human acceptance is outstanding.

## Passing evidence

### M2 reviewed local candidate, 2026-10-03

Branch `claude/2352-javascript-runtime-conformance` is based on reviewed M1 `f81be4be`. A shared immutable JavaScript Core profile now owns curated deterministic Math/JSON signatures, the always-present frozen `args` surface, conditional visible-variable helpers and generated-getter naming, and known unavailable ambient capability metadata. Jint consumes the same names/getter/strip metadata without changing sandbox grants or exposing runtime services to authoring. The metadata is deliberately not an exhaustive ECMAScript catalogue. Provider diagnostics use parsed reference positions and lexical scope, ignore keys/comments/local shadows and safe `typeof` absence probes, and leave unknown computed paths undiagnosed.

- Root locked Release runs pass Expressions 149/149, Jint 64/64, Design 507/507, Design API 142/142 and Publishing API 714/714. The Jint rerun after extracting shared request setup also passes 64/64.
- Scoped Architecture passes 16/16 domain-capability/value-flow tests and the separate Core implementation-shape guard 1/1. This is scoped local evidence, not a claim that the full local Architecture suite passed.
- Rebuilt real Workbench persisted-host tests pass 3/3, including cookie and rotating-bearer JavaScript completion/strict-expression/ambient diagnostics, recovery to a valid Math/JSON function expression, and the independent missing-Liquid control. An earlier attempt collided with the browser command building the same Workbench output; the sequential rerun passes without a code or gate relaxation.
- The coordinated rebuilt Studio branch `codex/552-javascript-expression-conformance` passes the canonical normal-host Chromium command 4/4 in 3.2 minutes, with fresh SQLite, real authentication, exact-source saved readback, post-save revision correlation, zero console errors and owned teardown. It proves TypeScript/JSX/statements and ambient-capability diagnostics plus valid-expression recovery through the actual producer/consumer path.
- Temporarily removing ambient diagnostics causes 20 provider regression failures; production code is restored and the complete Expressions rerun passes 149/149.
- The existing JavaScript REST e2e suite passes 10/10 against a newly rebuilt Debug Workbench, on a fresh isolated SQLite content root and free loopback port. The helper stops only its owned process. Deliberate map regeneration changes only `docs/maps/spec-status-map.md` for completed task counts; both generated findings reports are reviewed and unchanged, and the final freshness check passes.
- Final independent source audit identifies a pre-existing runtime error-attribution defect: regex scanning the whole source blames unrelated failures on a withheld capability mentioned only in a comment, key, literal, safe absence probe or local shadow. Five new controls fail before correction. Jint now parses only for failure attribution, maps the actual failing syntax location across its expression wrapper, and requires a static capability path; dynamic/ambiguous paths and any same-root binding anywhere retain the original native error. This deliberately conservative runtime guard is not a second authoring scope solver and adds no dependency/project edge. Root reviews the correction and the final complete Jint suite passes 80/80, including 20 attribution cases and the original #921 positive controls. After this correction, the paired command rebuilds both hosts/clients and passes browser 4/4 in 2.4 minutes, the real persisted-host fixture passes 3/3, the freshly rebuilt Debug Workbench JavaScript REST suite passes 10/10, and map freshness passes again. No prior failing attempt is counted as green.

These are local candidate-tree checks. Exact committed-head CI, Maps and Copilot review remain pending. M2 is not accepted or shipped, and M3/M4 are not complete.

### M1 exact-head automated gate, 2026-10-03

Foundation `f81be4beed6973378eb678b2327d789c19d8095e` passes [CI 37116214636](https://github.com/elsa-workflows/elsa-foundation/actions/runs/37116214636), including full hosted Architecture, and [Maps 37116216477](https://github.com/elsa-workflows/elsa-foundation/actions/runs/37116216477). Copilot review 5400291420 has no findings. Root local real-Workbench persisted assistance passes 3/3, Design API 142/142, composition 11/11, JavaScript REST e2e 10/10 and map freshness. The full local Architecture attempt was not green because unrelated full-tree/Debug restore prerequisites were absent; hosted Architecture passes instead.

Coordinated Studio `c9479fba29613b6c58335c05e1d26fb40a51514e` passes every job in [CI 37123111128](https://github.com/elsa-workflows/elsa-foundation-studio/actions/runs/37123111128), including the paired source-built normal-host Chromium suite, 4/4 against that exact Foundation head. It proves persisted workflow JavaScript/Liquid assistance in compact and expanded modes, Activity Definition canonical save/read/reload, and independently removed editor/provider controls, with real authentication, fresh SQLite, zero console errors and owned teardown. Root local rebuilt browser also passes 4/4. Exact-head Copilot 5400803545 has no actionable findings and all M1 review threads are resolved. Studio null preservation is separate adapter/API round-trip evidence, not a new live null browser case.

This closes the automated T026-T027 dependency and unblocks M2. It does not prove arbitrary main compatibility, M2-M4 completion, current-head manual assistive-technology acceptance, final human acceptance, or delivery. PRs #2373 and elsa-foundation-studio#557 remain draft and unmerged. Earlier checkpoints below retain their historical failures and pending states.

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

- Exact head `8c588ebac2e6895bdb713084d1687e21e6ec34ed` passed CI [37103917688](https://github.com/elsa-workflows/elsa-foundation/actions/runs/37103917688) and Maps [37103919244](https://github.com/elsa-workflows/elsa-foundation/actions/runs/37103919244); Copilot review 5399422093 reported no findings. This producer proof covered cookies, not Studio's per-request bearer exchange.
- The real Studio browser exposed an additional production defect: the permission fingerprint included token-instance claims, so every new bearer changed context revision with unchanged draft/permissions. A new Workbench theory reproduces `Stale` on the next completion before the correction (one failure, cookie and missing-Liquid controls passing). The narrow correction excludes only `jti`, `iat`, `nbf`, `exp`, `oi_tkn_id`, `oi_crt_dt`, and `oi_exp_dt` from revision hashing; the authenticated principal and token lifetime/revocation validation are untouched. Every other claim and host-policy fingerprint remains part of stale protection. These names were checked against the configured OpenIddict 7.5.0 / IdentityModel 8.16.0 validation path, including restored entry timestamps.
- The corrected affected-tree Release run passes all three real-Workbench cases: cookie and fresh-bearer JavaScript/Liquid persisted assistance, plus missing Liquid. Each bearer operation exchanges a different token and asserts unchanged context revision. All 142 Design API tests pass, including claim-order/renewal stability and individual subject, role, permission, issuer, audience, tenant, security-stamp, custom-policy, private-scope/audience/presenter/authorization-ID/token-type mutations. Local generated-map freshness passes without regeneration. Exact-head hosted/browser gates are still pending.
- The scoped composition rerun passes 11/11. The complete local Architecture run reports 615 passing and 22 failing tests: every failure requires restore assets outside this scoped Release build (unrestored projects, Foundation Host, or Debug evaluation). This is not a passing full architecture gate; full-restored current-head CI remains required. The Studio browser now receives successful assistance with matching revisions but still fails on editor Enter interaction and Activity Definition readback, which are being reconciled separately.

T026-T031 remain open. The coordinated Studio browser suite collects its four normal-host scenarios; its corrective live rerun has not yet produced passing evidence.

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
