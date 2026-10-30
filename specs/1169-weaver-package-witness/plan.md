# Implementation Plan: Independent Weaver Package Witness

**Branch**: `agent/codex/3438-weaver-package-proof` | **Date**: 2026-10-30 | **Spec**: [spec.md](spec.md)

**Input**: #3438, Program #3428 / M1 #3430. Source baseline `c986f9ddfdec2c641b2f1c1f5d8f82f8dd19e529`.
**Status**: Reviewed for implementation; no runtime qualification or contract acceptance claimed.

## Summary

Prove the candidate with an independently built module owning a Home widget and its reviewed visibility operation. Use the real personal Home layout through `IStudioPreferenceService`, native authorization, revision compare-and-swap and independent native Home/API readback. Preserve unrelated layout entries/options. The owning resource is the module's widget configuration within the existing personal document.

Produce NuGet archives and an npm SDK tarball; instantiate module-author and host consumers outside the repository. Keep a separate resource-agnostic protocol fixture available during resource-module removal. Compose native Workflows, authentication and package administration before the baseline. Publish the two consumers once, then add/remove/restore the resource packages through supported native paths. Common binaries/assets must remain identical.

A demonstrated generic defect may be corrected, with a causal regression test, before establishing the final immutable baseline and repeating the whole sequence. No common implementation anticipates the witness payload. Workflow validation, atomic working-state application, whole-change undo and explicit save remain required.

## Technical Context

**Language/Version**: .NET 10/C#, TypeScript/React, Python 3 orchestration, Playwright.
**Dependencies**: Public Agent.Core, Identity.Core, Preferences.Core and Studio.Core/SDK; existing CShells/Nuplane. Host-selected Agent, Identity, Preferences API/EF and native Workflows implementations. Public provider factories in a separate protocol fixture.
**Storage**: Explicit personal `home-layout`, schema 1, existing native SQLite Preferences store. Existing workflow working state/persistence stays separate.
**Testing**: Risk-based resource tests; package restore/build/publish audits; native reconciliation; real HTTP/browser observations; unchanged provider adapters with protocol clients; relevant existing common suites and required CI.
**Target**: Supported .NET 10/native profile; no remote publication/deployment.
**Project Type**: Reusable qualification tool and external test-module/consumer templates, excluded from default product compositions.
**Performance**: Preserve relevant current frontend budgets; retain measured final artifacts/times. No new invented latency target.
**Constraints**: No product source/workspace links or copied engine in consumers; no frontend tools during host restore/build/publish/run. Only own-widget data reaches the provider. One implementation leaf.
**Scope**: One non-workflow resource and native workflow witness; lifecycle/security controls; preview migration and catalog correction. M2–M5 retain broader lifecycle/provider/module coverage.

**Test cadence**: Elsa declares no test-cadence gate (ADR0080 D9, recorded in the derived constitution). Greenfield cadence is therefore unspecified at application level; this plan records that gap without adding a gate. Use risk-based behavior tests alongside implementation. Demonstrated common behavior corrections additionally require causal failing-before/passing-after evidence under the authorized review process.

## Constitution Check

Framework 6.1.0 and Elsa 6.3.0 gate indexes plus [review checklist](../../docs/contributing/review-checklist.md) apply. Draft/provisional material grants no new authority.

| Gate | Disposition |
|---|---|
| Framework §§2.1, 2.6, 2.7, 2.20 | Resource consumes Core contracts; host chooses persistence. No implementation shortcut or heavy Core dependency. |
| §§2.5, 2.5.1, 2.11, 2.23 | Public nonsealed feature/virtual wiring; scoped logic, immutable configuration; normal contribution registration with behavior evidence. |
| §2.6.1–2.6.3 | Reuse named Sequential candidate contribution events/single aggregators; no second engine, registry or anonymous side-effect dispatch. |
| §§2.13–2.16, 2.18 | Resource backend/Studio and protocol fixture follow separate dependencies. Templates add no production project or namespace. Only actual author seams are public. |
| §§2.21–2.23 | Tests exercise effects/refusals and causal common regressions. Update existing catalogs/docs; preserve valid tests. |
| §4.2 / Elsa §E5 | State breaking preview package/API migration. Retain exact local versions/bytes; no remote release. |
| Elsa §§E2.2, E2.5–E2.6 | No runtime/design dependency, mandatory base context or executable semantics change. Preserve normal workflow authoring boundaries. |
| Elsa §E7 | Common regressions use existing test projects. Long external-host qualification runs in integration/package routing; no unregistered new test project. |

Pre-research: no exception required. Post-design/independent plan review: public scope/auth/CAS/widget/provider seams suffice; no new contract is justified from source alone. Explicit host recipes, shared contract/version admission and one-provider feature selection resolve the review findings. Close/reopen replay is now demonstrated on the actual c986 host (issue3438 receipt6516911261); registry remount remains an execution control. No constitution exception is requested.

## Structure

`tools/studio/Weaver.Conformance.Probe/` contains the qualifier, browser observations and external BackendConsumer, StudioConsumer, Resource.Backend, Resource.Studio/Client and ProviderProtocolFixtures templates. Reuse existing Studio qualification mechanics where applicable; do not copy the generic engine. Producer may use repository projects; external authors/consumers use distributions only.

Existing Agent/Hosting/Workflows tests receive only demonstrated regression coverage. Existing contribution contract at [spec1018](../1018-weaver-module-contributions/contracts/contributions.md), Agent/Workflows and root catalogs receive compatibility/evidence updates. This spec holds plan/research/data model/validation contracts and later tasks, not a duplicate public API definition.

[Consumer recipes](contracts/hosts.md) enumerate actual packages/features, normal Identity cookie-to-token authentication, isolated native stores, explicit shared contract/version admission and one enabled provider feature. These source-derived recipes require real package builds; no successful closure is inferred from the plan.

## Delivery sequence

1. Build the scoped fixture resource and generic provider-protocol package against retained public distributions; prove owning scope, read, CAS and readback behavior.
2. Materialize/package its client using the actual SDK tarball; typecheck without workspace aliases; own context, prompts, preview/result renderer and widget/settings route.
3. Produce and publish ordinary backend/Studio package consumers, with native workflow support and package administration selected before baseline. Audit closure/cache/published bytes and deny consumer frontend tools.
4. Complete context/read/review/application and native workflow/undo/save observations. Exercise security/stale/malformed/duplicate/vanished-client controls with real before/after owners.
5. Add/remove/restore and run the genuine incompatible SDK control; require native membership and serving generation changes. Retain consumed intent across reconciliation without replay. Fix only demonstrated generic defects and rerun final artifacts.
6. Review evidence, incorporate preview migration/catalog corrections and record the smallest accepted contract only after accepted #3437 integration. Run review/CI/merge/main gates and reconcile owning issues/board.

## Complexity

No constitution exception requested. Independent fixture packages represent the real author boundaries being tested; they are not new product layers. Do not build generalized resource schemas, storage, agents, permissions or orchestration in the qualifier.
