# Tasks: Independent Weaver Package Witness

Input: [plan.md](plan.md), [spec.md](spec.md), research/data model and contracts. Implementation and independent distribution qualification are complete at code baseline `76ec58d0061135cfa028e1bc47ec7639fd139e7f`; [curated evidence](evidence/qualification-76ec58d00.json) records all 38 cases. #3437 is verified Done. Final-source and integration tasks T026–T028 remain open.

One implementation leaf; parallel work within it may own distinct fixture files. Greenfield cadence remains unspecified by Elsa (D9); use risk-based tests alongside code. A demonstrated common correction additionally needs causal failing-before/passing-after evidence. No task below is completion evidence until checked with an owning receipt.

## Phase 1: Setup

- [x] T001 Recheck #3438 claim/open PRs and completed #3437 dependency; maintain scoped records in specs/1174-weaver-package-witness/spec.md and tasks.md.
- [x] T002 Establish the qualifier's immutable identities, driver configuration and private/public evidence boundary in tools/studio/Weaver.Conformance.Probe/qualify.py and README.md.

## Phase 2: Foundation

- [x] T003 Implement retained committed-source product NuGet/SDK producer closure and external PackageReference/SDK-tgz audits in tools/studio/Weaver.Conformance.Probe/artifacts.py, reusing existing qualification mechanics without copying the engine.
- [x] T004 Implement real backend/Studio consumers with normal cookie-to-token auth, native stores/features, exact contract sharing/version admission, owned enrollment/feeds and serving observations in tools/studio/Weaver.Conformance.Probe/fixtures/BackendConsumer/ and fixtures/StudioConsumer/, following contracts/hosts.md. Correct the demonstrated pre-start background readability race in Essentials/src/Cluster/Readability/, synchronous saved-feature refresh in Essentials/src/Modularity/Api/ and protected RCL assembly-to-package asset ownership in Studio/src/essentials/Elsa.Studio.Hosting.Nuplane/, with causal regressions in their existing projects; retain normal schema validation and native lock policy.
- [x] T005 Implement the separate resource-agnostic provider factory package and one-feature profiles in tools/studio/Weaver.Conformance.Probe/fixtures/ProviderProtocolFixtures/, using actual adapters and unchanged baseline artifacts across module extension.

Foundation checkpoint: 76ec qualifies the actual producers, external authors and native consumers. Source-only design or helper tests are not substituted for that execution evidence.

## Phase 3: US1 — Independent resource journey (P1)

Independent test: build external authors/consumers from distributions, then observe real widget and workflow reads/review/application against actual owners.

- [x] T006 [P] [US1] Implement scoped personal-widget context/guidance, read and reviewed-visibility tools, native authorization/CAS and owner readback in tools/studio/Weaver.Conformance.Probe/fixtures/Resource.Backend/.
- [x] T007 [US1] Cover logic-bearing resource implementations and conditional feature wiring with external fixture tests in tools/studio/Weaver.Conformance.Probe/fixtures/Resource.Backend.Tests/, including no side effects before application and preservation of unrelated JSON.
- [x] T008 [P] [US1] Implement the public SDK widget, settings route, context/prompts/tool declarations and human-readable module preview/result in tools/studio/Weaver.Conformance.Probe/fixtures/Resource.Studio/Client/, plus its prebuilt RCL template.
- [x] T009 [US1] Execute actual producer, independent module typecheck/build/pack and clean host restore/build/publish in tools/studio/Weaver.Conformance.Probe/artifacts.py; retain locks/closure/bytes and audit denied consumer frontend tools.
- [x] T010 [US1] Execute real widget context/read/review/application/native Home reread and workflow position/validation/whole Undo/no implicit persistence/explicit Save through both protocol profiles in tools/studio/Weaver.Conformance.Probe/browser.mjs and journeys.py.

## Phase 4: US2 — Availability lifecycle (P1)

Independent test: genuine native package membership/generation transitions, exact incompatible SDK failure and restored positives; common bytes remain identical.

- [x] T011 [US2] Implement native add/remove/restore and pending-operation observations, preserving unrelated state and provider availability, in tools/studio/Weaver.Conformance.Probe/journeys.py.
- [x] T012 [US2] Build/run a valid unsupported-SDK variant; require precise incompatibility, absent contributions and remaining Studio usability, then restore original archive bytes in tools/studio/Weaver.Conformance.Probe/artifacts.py and journeys.py.
- [x] T013 [US2] Retain the reproduced Close/reopen consumed-intent replay, add a causal Hosting regression and fix generic intent ownership in Studio/src/essentials/Elsa.Studio.Hosting/Client/src/app/App.tsx and app/weaver/WeaverSurface.tsx (and their existing test locations); cover registry remount without resource-specific branches.
- [x] T014 [US2] Rebuild any corrected common artifacts and repeat all native positive/control/restored transitions from a fresh final immutable baseline in tools/studio/Weaver.Conformance.Probe/qualify.py.

## Phase 5: US3 — Authority and effect boundaries (P1)

Independent test: each required refusal/uncertainty/duplicate case retains real owning before/after state and never fabricates success or replays an accepted effect.

- [x] T015 [US3] Exercise current read/write denial after preparation, normalized actor/tenant/host isolation, foreign/malformed scope, missing/default/no-op resources and invalid schema in tools/studio/Weaver.Conformance.Probe/fixtures/Resource.Backend.Tests/ and journeys.py.
- [x] T016 [US3] Exercise stale reviewed revision and actual simultaneous native CAS, preserving the winning full document, in tools/studio/Weaver.Conformance.Probe/journeys.py.
- [x] T017 [US3] Exercise malformed client completion evidence and a genuine module result without a matching renderer, requiring uncertainty/safe generic presentation and owner observation, in tools/studio/Weaver.Conformance.Probe/browser.mjs and journeys.py.
- [x] T018 [US3] Remove the original native workflow client/editor binding and module contribution while a proposal is outstanding; verify no retarget or false application in tools/studio/Weaver.Conformance.Probe/browser.mjs.
- [x] T019 [US3] Exercise denial/cancellation before effect, identical accepted execute/ACK delivery, lost confirmation and supported post-dispatch uncertainty without replay in tools/studio/Weaver.Conformance.Probe/browser.mjs and journeys.py.
- [x] T020 [US3] Assert unrelated/secret canary omission from admitted context, provider protocol traces and results while preserving owner JSON; prove common payload handling is opaque in tools/studio/Weaver.Conformance.Probe/journeys.py and fixtures/ProviderProtocolFixtures/.

## Phase 6: US4 — Reproducible contract decision (P2)

Independent test: the reusable driver reproduces the final distribution evidence; wrong-cause/unrun/missing-artifact cases fail; migration/catalog claims match demonstrated support.

- [x] T021 [US4] Implement strict evidence index/aggregate validation for exact source/archive/cache/publish/serving identities and preserved failed controls in tools/studio/Weaver.Conformance.Probe/qualify.py.
- [x] T022 [US4] Test rejection of wrong-cause controls, mismatched identities and incomplete evidence in tools/studio/Weaver.Conformance.Probe/test_qualification.py; include these meaningful driver checks in the entry point.
- [x] T023 [US4] Add explicit runnable integration CI routing for the reusable complete package witness and retained reports in .github/workflows/weaver-conformance.yml; keep provider-protocol/live labels and no-publication permissions.
- [x] T024 [US4] Incorporate preview package/namespace/removed API migration and actual workflow subset in specs/1018-weaver-module-contributions/contracts/contributions.md, Extensions/Agent/src/Elsa.Agent/EXTENSION_POINTS.md, Extensions/Agent/src/Workflows/EXTENSION_POINTS.md and EXTENSION_POINTS.md; list external implementations and document author reproduction plus Cross-domain contributions in tools/studio/Weaver.Conformance.Probe/README.md.
- [x] T025 [US4] Repeat complete final-artifact proof, record demonstrated minimal contract acceptance and incorporate final evidence in specs/1174-weaver-package-witness/quickstart.md and the canonical contribution contract only after all witness controls pass.

## Phase 7: Review and integration

- [ ] T026 Run relevant affected Agent/Hosting/Workflows suites, registration/architecture/API checks and frontend budgets; retain exact-head evidence linked from specs/1174-weaver-package-witness/tasks.md.
- [ ] T027 Open the scoped draft PR for #3438, attach it, run independent review rounds to convergence (maximum six), satisfy exact reviewed-head CI and merge through authorized repository rules; update owning issue/Program/Project transitions and receipt links in specs/1174-weaver-package-witness/tasks.md.
- [ ] T028 Verify resulting main through required CI and the real composed/distribution witness; reconcile #3438/M1 acceptance, native dependencies and Project statuses only after evidence, recording the final source/receipts in specs/1174-weaver-package-witness/tasks.md.

## Dependencies and parallel work

T001–T005 precede US1 distribution execution. T006 backend and T008 client may be authored independently after T002 fixes identities, then converge at T009/T010. US2 and US3 depend on US1's real composition and run in the single integration lane. T013 requires retained actual replay and a failing regression before its correction. Any common change invalidates the earlier final baseline: T014/T025 repeat the complete proof. US4 final acceptance depends on US1–US3, then review/merge/main gates follow.

Independent investigation/author work may cover backend, client and protocol templates in distinct files; reviewers for the final PR must not have contributed to it. No second implementation issue is activated.

## Coverage and increments

US1 maps FR001–006/011 and SC001/004; US2 maps FR004/007/010/012–013 and SC002/003; US3 maps FR003/008–009/015–016 and SC003; US4 maps FR005–006/012–016 and SC004–005. T026–T028 preserve all acceptance/merge/main gates.

US1–US4 are implemented and exercised by the 76ec witness. Review/integration tasks remain mandatory before #3438/M1 completion; this packet does not close those gates.

## Qualified implementation receipt

T003–T025 are checked against the completed [76ec code-baseline witness](evidence/qualification-76ec58d00.json): 135 stages exit 0, all 38 cases complete (16 positives and 22 expected Failed controls), both original/restored resource and workflow journeys through both actual adapters, unchanged common baselines, and zero consumer source references/frontend invocations. [Quickstart](quickstart.md#qualified-code-baseline) records artifact/report/export identities. The [M1 contribution contract](../1018-weaver-module-contributions/contracts/contributions.md) records the demonstrated minimum and its process-local retention/review bounds.

Authoritative receipts: [owning leaf #3438](https://github.com/elsa-workflows/elsa-foundation/issues/3438#issuecomment-6538498830), [implementation PR #3497](https://github.com/elsa-workflows/elsa-foundation/pull/3497#issuecomment-6538499164), [M1 #3430](https://github.com/elsa-workflows/elsa-foundation/issues/3430#issuecomment-6538499535), and [Program #3428](https://github.com/elsa-workflows/elsa-foundation/issues/3428#issuecomment-6538499883). The prerequisite [#3437 acceptance](https://github.com/elsa-workflows/elsa-foundation/issues/3437#issuecomment-6516702315) is complete.

T026–T028 remain unchecked. The 98 driver tests and 135 stages belong to the 76ec baseline. Fresh final-source proof, affected-suite/API/architecture/budget checks, required exact-head CI, independent PR review, authorized merge and resulting-main qualification must be recorded on the owning PR/issue before those tasks complete. Authors of this implementation are ineligible to supply its final independent review. The spec's `Implemented` status is staged in the implementation PR under the lifecycle rule and becomes effective on merge; it does not assert that the PR is already merged or that M1/Program statuses are Done.

## Causal corrections retained for review

Earlier failures remain Failed in the private outputs and owning issue; later runs do not relabel them. The completed witness uses freshly rebuilt common and module artifacts incorporating the demonstrated corrections.

| Correction | Commit | Preserved boundary |
|---|---|---|
| Defer detached readability until host startup | `d1cf419bb` | Native startup reconciliation retains normal lock/privacy policy |
| Refresh saved feature configuration before shell activation | `d2edae52f` | Standard host configuration and native feature lifecycle |
| Resolve protected RCL assets through package ownership | `97ded6a45` | Shared assembly identity remains distinct from package/archive ownership |
| Consume shell intent across dock/registry lifetimes | `35f5c71ab` | A remount cannot submit an already consumed request again |
| Preserve native workflow position-edit metadata | `42aaced7a` | Atomic working state, validation, whole Undo and explicit persistence |
| Surface Copilot semantic tool refusals | `0ddba726a` | SDK transport completion cannot stand in for owning application |
| Stabilize the external widget checkbox during pointer activation | `b88f62d6d` | Ordinary trusted pointer activation; module-owned layout fix |

Later evidence-driver corrections are included in 76ec and documented on #3438: protected control paths, exact provider/native-result correlation, finite/aborted SSE capture, route-specific readiness, typed refusal versus proposal status, full manifest versus discovery summary, and serialized restore-path hash checks. They preserve strict identities, opaque payload comparison, expected Failed controls and retained originals. They introduce no new product contribution contract.

All 16 functional requirements and five success criteria retain task coverage. M2–M5, durable recovery, broader current-module/workflow support and live-provider qualification remain open.
