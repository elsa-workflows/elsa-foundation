# Implementation and qualification receipt — 6 November 2026

The product implementation for #3602 is complete on [PR #3689](https://github.com/elsa-workflows/elsa-foundation/pull/3689). Local qualification below passed; independent final acceptance, downloadable exact Actions evidence, required exact-head CI and resulting-main verification remain delivery gates on that PR. This document does not claim they have passed.

## Observed evidence

| Slice | Result | Identity and boundary |
| --- | --- | --- |
| Complete Decisions suites | Evaluation 96, Design 48, API 15, workflow adapters 32, workflow disclosure 11; 202 discovered/executed/passed, zero skipped | Review-fix product source `2c8925a2a`; discovery logs and TRX retained |
| Cold computed-version packages |46 assertions,96 evaluator tests passed | [Receipt](standalone-package/receipt.json), source `0718297e0`; Core/default source unchanged through `2c8925a2a`; nine package-only dependencies, no Workflow/EF/ASP.NET/Studio closure |
| External editor/reference evaluator |56 Elsa expectations;27 reference equivalents,29 differences/outside-profile cases | [Pinned fixture](interop/README.md), both actual edited round-trip directions; explicit profile/precision/null/result-shape limits, no full-DMN claim |
| Composed Studio |4/4 projects, zero skips/retries;68 logical PNGs plus ARIA/traces | Clean source `2c8925a2a`; desktop/narrow light/dark; seven cases including exact 34-digit and NoMatch, deliberate regression/repair, publish/evaluate/reload/clone, conflict preservation, exact import/export |
| Direct/native/BPMN/restart | Passed | [Receipt](execution-receipt.json), clean `2c8925a2a`; Sequence, Flowchart, public dynamic input and ordinary BPMN bound task return identical result/trace; suspended caller resumes original pin after process restart with Decisions API/Design/EF/workflow-design disabled |
| Real Identity authority/disclosure |37 checks passed across seven users/two tenants | [Receipt](identity-qualification/receipt.json), clean `2c8925a2a`; independent grants,401/403/404, source/output protection and persisted Evaluate revocation before resume |
| Studio module |40/40; module/acceptance TypeScript, root-configured ESLint/stylelint and real production frontend build passed | Corrected unsaved-case/new-metadata guards, focus mount/restore, exact tagged transport and readable narrow explanation headers |
| Architecture and composition |617/617 passed, zero skipped; locked normal Release and isolated Debug graphs, locked server solution restore, generated filters and tier checks passed | Repeated after merging main `abea6ae86`; 138 test projects each assigned one tier/shard |
| Shared authenticated-start paths | Prior whole Runtime 3355, Publishing API 940, BPMN 581 passed, zero skipped | Their discovery placeholders expand data rows at execution; final current-source repeat and CI results are recorded on the PR |

The normal host applied its own fresh SQLite migrations. Actual SQLite suites cover CAS, concurrent publication, immutable identity, tenant isolation, completed-receipt response loss/replay, precommit interruption, summary-only/opt-in history and the final-slot history race. Migration SQL was generated for SQLite/PostgreSQL/SQL Server/MySQL; only SQLite database behavior is qualified by these local runs. No other provider's existing migration history was reset.

The host matrix does not claim resolvable captured decision-activity result payloads, hosted-model BPMN HTTP execution or external-provider identity. Workflow output disclosure is exercised for Sequence/Flowchart/BPMN; focused disclosure/authority suites additionally cover exact model dependency traversal, payload authorization and explicit unsupported-identity refusal. Protected workflow bindings/materialized envelopes fail closed rather than claiming arbitrary downstream sensitivity propagation.

## Corrections and causal controls

Real-host qualification exposed missing EF module/schema registration, tagged Object input deserialization and cookie API redirects; these were corrected with whole-suite and wire-level evidence. The earlier unsuccessful UX attempts remain preserved: selector error, expected-outcome locator failure and the actual narrow-dark focus race. They were not converted into passing acceptance. Owner-authorized continuation fixed the race using mount-time focus and opener restoration; a queued-frame negative control failed before correction and passed after it.

Formal review round 1 at `7553f504b` found retained-history overflow, unsaved-case replacement, late replacement ambiguity, new-form metadata dirty state and narrow rule-label compression. All are corrected in `2c8925a2a`. The refreshed four-project browser run passed; independent visual retest is a separate gate.

[Retained causal receipt](review-causal-controls.json): removing the history admission bound fails its boundary test, then the whole Design 48 passes after restoration. Disabling final registration validation fails 13 rejection cases, then the whole Design 48 passes. Restoring the pre-fix case/page controls while retaining new tests produces 4 failures, then the whole Studio 36 passes after restoring product code. Composition and unsaved-edit controls ran in a private detached clone without changing the primary checkout; history and column regressions were run against pre-fix primary source and rerun after correction. Prior engine/XML/authority/disclosure controls also exercised normalization, null wildcard, canonical bytes, ANY conflicts, CAS, source privacy/hash, identity ambiguity and retention.

Round 2 at `bf415c898` found imported declaration drift, generated input-name collisions and missing owning-catalog entries. All are corrected in `559af440d039f3b7af761566fe77e36ea888a256`. Five actual browser-editor outputs now compile/evaluate through the real engine; four new unit regressions fail before correction and the whole 40-test module passes after it. Cross-domain known implementations are recorded in the Foundation Identity and Publishing catalogs.

## Evidence access and final gate

Local raw receipts/logs/TRX/package artifacts/browser traces are retained in the task qualification directory. The committed runners reproduce the host evidence from clean source; CI uploads composed artifacts for 14 days. The final PR evidence comment must name the exact head/tested source, downloadable Actions artifact, independent correctness/standards/acceptance reviewers, actual screenshots opened, findings/retest and unverified states. Machine success is not independent visual approval.

The existing broad host build reports transitive NU1902/NU1903 advisories for System.Security.Cryptography.Pkcs 9.0.13 and MySqlConnector 2.3.5, plus existing compiler/analyzer warnings; these runs are not zero-warning claims. No package or release was published. Changes to the shared execution foundation only capture trusted authenticated start subjects and apply the normal publication/export authority boundary; Decisions adds no scheduler, pending ledger or execution coordinator.
