# Tasks: Studio Package CI

**Input**: [spec.md](spec.md), [plan.md](plan.md) and the accepted3365handoffs.
**Scope**: One implementation leaf, existing publisher holds, no new production API/project or dependency choice.

## Phase 1: Setup

- [x] T001 Validate exclusive claim, healthy main/prerequisites, numbering, mandatory branch hook, specification/checklist and existing-contract plan in `specs/1015-studio-package-ci/`.

## Phase 2: US1 - Complete Archive Guards

**Goal/test**: Genuine complete packages pass; precise dependency/resource faults fail and restored originals pass.

- [x] T002 [US1] Inspect actual nuspec/resource payloads and canonical inventory/producer ownership before refining `tools/ci/verify_studio_nuget_packages.py`; preserve frozen consolidation identities.
- [x] T003 [US1] Add required dependency/static-content verification and meaningful failure/restoration self-tests in `tools/ci/verify_studio_nuget_packages.py`, deriving resource paths from `tools/ci/verify-studio-assets.py`.
- [x] T004 [US1] Execute genuine candidate complete/dependency/missing/empty/changed-resource/restored controls and retain original hashes/diagnostics in `specs/1015-studio-package-ci/validation.md`.

## Phase 3: US2 - Consumer and Causal-Control Automation

**Goal/test**: Every required existing proof executes automatically and only its precise intended negative qualifies as a control.

- [x] T005 [US2] Add `tools/ci/qualify_studio_consumers.py` to run actual API and Hosting normal/control/restored profiles, read full reports and verify provenance, precise diagnostics, denials and immutable original inputs.
- [x] T006 [US2] Integrate `tools/studio/Composition.Probe/` and strengthen genuine-reconcile/update observation before accepting its disabled-reload failure; mutation-test rejection of a no-update/wrong control.
- [x] T007 [US2] Automate normal/causal-control/restored shared-contract and admitted-reader protection using `tools/studio/HostingReuse.Probe/` and its StorePruning profile, preserving supported-native failures and fixture assumptions.

## Phase 4: US3 - Workflow Ownership

**Goal/test**: Owned changes select complete validation without activating publishers or changing identities/version policy.

- [x] T008 [US3] Wire same-version actual candidates/reference publish, Chromium/native prerequisites, owned-input triggers and retained evidence into `.github/workflows/studio-ci.yml`; verify existing publisher guards and unchanged permissions/identity records.

## Phase 5: Integrated Verification and Delivery

- [ ] T009 Run the complete frozen actual-archive/consumer/control orchestration, retain source/tool/archive/report/cleanup observations and finalize runnable commands in tool READMEs and `specs/1015-studio-package-ci/validation.md`.
- [x] T010 Run applicable whole tooling checks, affected .NET fixtures if changed, required architecture graph/whole guard/discovery and generated maps; reconcile results in `specs/1015-studio-package-ci/validation.md` without repeating unrelated suites.
- [ ] T011 Converge independent correctness/standards/acceptance review within6rounds, terminalize spec/remove checklist and pass actual exact-head CI/Full tier/Studio on `.github/workflows/studio-ci.yml` and the final diff.
- [ ] T012 Complete reviewed delivery, verify resulting-main Linux CI/candidate proof, then reconcile issue/Project/claim with receipts in `specs/1015-studio-package-ci/validation.md`; leave3366and ancestors open until their own exit criteria pass.

## Dependencies and Execution Strategy

T001 -> T002 -> T003/T004 establishes the minimal archive guard. T005 depends on that artifact contract; T006/T007 extend its precise controls. T008 wires the complete matrix. T009/T010 precede final T011/T012 delivery. MVP is US1's actual complete/fault/restored archive proof; subsequent profiles are added incrementally rather than accepting a partial full-task pass.

Source inspection and separate profile-read audits can be independent, but shared build/server work runs serially with the build-slot wrapper. There is no implementation delegation. Only the mandatory independent review lenses run in parallel. No tests of documentation text, synthetic replacement product packages, arbitrary exit1 controls or unavailable-native skips.
