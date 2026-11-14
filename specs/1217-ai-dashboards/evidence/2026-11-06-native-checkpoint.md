# Native dashboard implementation checkpoint

**State:** Stopped; incomplete and unqualified. PR #3913 remains draft. No release, deployment, provider call, merge or resulting-main qualification occurred.

## Why work stopped

The org [AGENTS.md](https://github.com/elsa-workflows/.github/blob/main/AGENTS.md) stop conditions say: “You've failed the same check 3 times in a row.” Three native build/test attempts stopped at the shared Core public API baseline while resource contracts were still changing. Implementation was stopped, and all three author subagents confirmed no owned build/test/formatter process remains active. No further implementation or test retry was performed after that stop.

The baseline was repaired by an already-started operation before the stop and compilation then reached tests. This is progress, not permission to discard the triggered stop. Resume on explicit owner instruction, retaining the existing PR and failed evidence. Stabilize the contract/baseline before any retry; do not restart the review/failure history.

## Exact observed results

| Check | Actual result | Limit |
|---|---|---|
| New `Runtime.Inspection.Core` build | Passed; 0 errors, existing dependency warnings | Contract extraction only |
| Whole Runtime API suite | 302 passed, 0 failed, 0 skipped | Protects unchanged inspection policy/extraction; not Dashboard behavior |
| Management build, session 94935 | Failed: five missing PublicAPI entries | No behavior tests |
| QueryTests, session 19948 | Compile failed: changed resource ListAsync baseline | No tests ran |
| QueryTests, session 76957 | Compile failed: ListAsync/actor interface baseline | Third same-check failure; stop rule triggered |
| QueryTests, already-started session 36842 | 22 passed, 1 failed, 0 skipped | Failing `Dst_week_overflow_requires_an_explicit_alternative`; no fix/retry after stop |
| Initial resource API build, session 56485 | Failed: 1,068 initial Core PublicAPI diagnostics | Earlier shared-baseline failure |
| ResourceTests project build, session 15831 | Passed, 7 warnings, 0 errors | Nine authored tests were not executed |
| Whole Dashboard frontend suite | 86 passed across 7 files | Predates final query-control/drill edits |
| Frontend TypeScript | Passed at an earlier source state | Final edits unverified |
| Composed host, source/math/tenant security, business module, live provider, independent UX | Not run | No product acceptance |

Local logs: `/tmp/elsa3604-inspection-build.log`, `/tmp/elsa3604-runtime-api-tests.log`, `/tmp/elsa3604-tests/inspection-extraction-api.trx`, `/tmp/elsa-dashboard-resource-build.log`. Query/frontend outputs remain in this chat's tool records. These local paths are not shared downloadable acceptance artifacts.

## Code preserved

- `Essentials/src/Studio/Dashboard/Core/`: typed query/declaration/result models, named events, resource/actor/storage contracts and public API baselines.
- `Essentials/src/Studio/Dashboard/Management/`: query validation/orchestration/periods/concurrency, event fan-in, resource service/ACL/history/copy/preservation logic and feature registration.
- `Essentials/src/Studio/Dashboard/Api/`: native resource/query/refresh/drill endpoints, expected revisions, canonical actor mapping, audience directory and capability publication.
- `Essentials/src/Studio/Dashboard/Persistence/EntityFrameworkCore/`: durable definition/keyed-write/CAS/schema code, contexts/features/factories. **No migrations exist.**
- `Essentials/src/Workflows/Runtime/Inspection/Core/`: extracted unchanged inspection interface. Existing Runtime API implementation stays in place; consumers import the new contract namespace.
- `Essentials/tests/Studio/Dashboard/`: 23 query tests and nine unexecuted resource tests.
- `Studio/src/essentials/Elsa.Studio.Dashboard/Client/src/native/`: collections, ordinary controls, previews/renderers, arrangement, sharing/copy/history, refresh/drill and access/cancellation state code. Home preference/loader components were not changed; routes/navigation were added behind the backend capability.

**Not implemented:** real Workflow query contributor, independent business contribution, Weaver backend/context/tools/result controls, selected-provider Connections/Secrets adapter, full host/package/solution/tier wiring, practical finished docs, actual composed journeys and final acceptance.

## Known open defects and risks

1. Investigate the failing DST test with detailed output. Its fake clock moves backwards from 6 November to 25 October; distinguish fixture failure from window-policy failure before changing behavior.
2. Resource tile projection currently validates base-question disclosure; effective override values also need current disclosure validation.
3. Resolve actor role IDs against current role existence, not only IDs on a user record. Preserve identity-provider propagation truth.
4. Add explicit last-modifier/history audit attribution and canonical ordering for operation fingerprints.
5. Complete user/role audience search and question-picker paging.
6. Scaffold/qualify migrations and real provider activation/restart; SQLite `EnsureCreated` test setup is not migration proof.
7. Run the nine resource tests and add API authorization/concurrent-write/revocation/schema/provider cases. No current security guarantee is inferred from a build.
8. Finish native dialog cancellation guards, multi-series/negative/missing-value chart behavior and predicate-depth controls. Rerun final-source typecheck/tests/lint/bundle build.
9. Complete source-level authorization-before-aggregation, numerical oracle, bounded traversal, exact drill membership and independent module witness. The generic query foundation has no real dataset contributor yet.
10. Weaver's external authentication subject differs from canonical Elsa user ID. Use the trusted actor context's `AuthenticationSubjectId` to bind Agent sessions and canonical `UserId` for resource authority; refuse absent/ambiguous mapping.
11. Provider/access choice remains pending. No credential was assumed authorized and no provider-dependent implementation or call began.

## Resume sequence

After owner resumption, retain #3604 and draft #3913. Re-read live comments/claims and confirm the existing branch is still the delivery branch. Verify stable final Core/Management public API baselines, diagnose the DST failure and run whole affected suites. Fix the known resource disclosure/identity/audit/idempotency gaps before qualifying security. Then continue the dependency-ordered [tasks](../tasks.md), complete the real datasets and independent module, provider selection/integration and actual Studio qualification. Finish exact-head independent reviews/checks and resulting-main verification before delivery. Do not merge this checkpoint or close #3604 as completed.
