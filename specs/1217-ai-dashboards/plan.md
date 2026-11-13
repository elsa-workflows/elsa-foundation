# Implementation Plan: AI-authored personal and shared dashboards

**13 November delivery:** Source slices and production follow-ups have landed; [delivery slices](delivery-slices.md) and the [evidence ledger](evidence/2026-11-13/README.md) identify exact commits and scoped passes. Final keyboard/editor/Home and resulting-main obligations remain open.

**Branch**: `agent/codex/3604-ai-dashboards` | **Date**: 2026-11-06 | **Spec**: [spec.md](spec.md)

**Status**: Owner resumed implementation: one SDK-free dashboard author over Weaver and the existing Copilot SDK adapter, configured through backend options/appsettings/environment. Native and provider qualification remain explicit gates.

## Summary

Deliver saved questions and named personal/shared dashboards alongside unchanged Home. A validated module-owned query model powers both native controls and Weaver discovery/preview. Saved resources use native APIs and persistence, so editing and refresh require no model. Module satellites own authorized source selection, metric meanings, coverage and drill-down. Propose no server result cache initially.

Full delivery includes Workflow data, an independently packaged business-dimension witness, actual provider behavior, composed Studio journeys, security/recovery evidence, review/checks, merge and resulting-main qualification. The stages below describe order, not separate claims that #3604 is delivered.

## Technical Context

- **Language/Version:** repository-pinned .NET SDK 10.0.300; existing C# conventions; TypeScript, React 19 and pnpm 11.9.0.
- **Dependencies:** CShells, named Elsa Events, Foundation Identity authorization/IAM, domain read contracts, NativeEndpoints, Studio SDK/public UI controls and delivered resource-agnostic Weaver contracts. Existing Copilot SDK adapter; no new provider/model loop or OpenAI-specific implementation.
- **Storage:** Dashboard-owned provider-neutral definition/revision/audience contracts and EF Core persistence. Home stays in Studio Preferences. Qualification uses disposable owned stores, preserving existing hosts/stores.
- **Testing:** whole affected .NET projects, Vitest, composed Playwright journeys, independently packaged module probe, numerical oracle, actual selected-provider trace and independent visual review.
- **Platform:** Foundation hosts/composed Studio and independently packaged/custom-host composition; preserve Design-only/Runtime-only deployments.
- **Resource goals:** deterministic shape/traversal/row/group/concurrency bounds, cancellation and operational timeouts. Introduce no performance benchmark or wall-clock CI threshold.
- **Scope:** one dataset per first-profile query with declared module-owned enrichments; chart/table/list/metric; selected same-tenant user/role audiences. No arbitrary joins/SQL/scripts, external databases, generated widget code, runtime mutations, alerts, deploy or release.

## Constitution Check

Read both [framework v6.1.0](../../.specify/memory/constitution-framework.md) and [Elsa v6.4.0](../../.specify/memory/constitution.md), plus the [review checklist](../../docs/contributing/review-checklist.md). This proposed design check does not claim executed architecture tests.

| Gate | Application and required proof |
|---|---|
| Framework §2.1–2.2 | Studio Dashboard `.Core` owns public models/contracts; web/EF/provider implementations remain separate. Verify graph, names and public API. |
| §2.5/2.5.1 | Public inheritable feature classes, virtual registration, internal scoped logic services; immutable declarations alone may be singleton. Verify composition. |
| §2.6.1/2.6.4 | Separate authorized declaration/discovery from execution; named Sequential contribution events and one fan-in aggregator per purpose. Verify independent module without central switches. |
| §2.6.2/§2.11 | Replacement storage/identity seams diagnose missing/duplicate registrations. |
| §2.7/§2.14 | Workflow/Weaver are satellites; selected provider SDK remains in its adapter. |
| §2.9/§2.10/§2.20 | Provider-neutral command/query storage, consumer-owned DbContext support and tenant/CAS/history invariants. Verify supported providers. |
| §2.16/§2.18 | Existing Studio domain, no new analytics root; each package earns a contract/provider/cross-domain boundary. |
| §2.22/§2.23.3 | Track public API, ship extension catalogs and contribution README notes; keep implementations internal. |
| §2.23.1–2.23.5 | Test risky logic/authorization/concurrency/persistence; infrastructure errors become domain errors. Bite-proof claimed behavior. |
| Elsa §E2.2/§E2.6 | Runtime gains no Design/Dashboard dependency. Verify deployment shapes and architecture guards. |
| Elsa §E7 | Classify new test projects; host/container projects belong to integration tier. |
| Draft/provisional sections | No reliance on framework §2.24 or Elsa §E2.8/§E2.9 as newly ratified authority; no constitutional amendment proposed. |

A necessary seam is being extracted: runtime inspection authorization currently lives in the Runtime API implementation assembly. The new contracts-only `Elsa.Workflows.Runtime.Inspection.Core` contains the unchanged interface, referencing Runtime Core and Identity Core while keeping those dependencies out of baseline Runtime Core. The HTTP implementation remains in Runtime API. Dashboard consumes this seam, with no copied permission rules or Dashboard→Runtime.Api implementation reference. Design's existing `IWorkflowDefinitionAuthorizer` protects current-folder enrichment. Raise any discovered ADR conflict before production changes.

Pre-design result: selected scope fits existing ownership. Post-design result: independent data/authorization, standards and UX-design retests found no remaining concrete native-design blocker after corrections. Native work may proceed; provider configuration is selected; usable live access and final architecture/runtime/UX proof remain open. No constitutional exception or unrecorded owner decision is assumed.

## Project Structure

Documentation: [research](research.md), [data inventory](research-data.md), [Weaver inventory](research-weaver.md), [data model](data-model.md), [query contract](contracts/query.md), [resource contract](contracts/resources.md), [Weaver contract](contracts/weaver.md), [UX acceptance](ux-acceptance.md), [quickstart](quickstart.md) and [tasks](tasks.md).

Proposed source placement; refine exact identities during public-surface review rather than creating empty projects:

| Path | Responsibility |
|---|---|
| `Essentials/src/Studio/Dashboard/Core/` | Public declarations/query/results/resource contracts |
| `Essentials/src/Studio/Dashboard/Management/` (`Elsa.Studio.Dashboard.Management`) | Generic query orchestration and definition policy; distinct identity from the existing Studio RCL `Elsa.Studio.Dashboard` |
| `Essentials/src/Studio/Dashboard/Api/` | Authorized discovery/query/resource transport |
| `Essentials/src/Studio/Dashboard/Persistence/EntityFrameworkCore/` | Definitions, immutable revisions, audiences and concurrency |
| `Essentials/src/Workflows/Dashboard/Queries/` | Workflow satellite over Runtime/Design Core; current folder is a declared enrichment |
| `Essentials/src/Workflows/Runtime/Inspection/Core/` and `Api/` | Minimal reusable inspection seam only |
| `Extensions/Weaver/src/Dashboard/` with owning Dashboard Studio SDK contributions | Dashboard-owned context/tools/result controls through existing orchestration |
| Selected existing provider adapter | Existing backend token/environment/runtime options; preserve single active harness and SDK Empty-mode boundary |
| `Studio/src/essentials/Elsa.Studio.Dashboard/Client/src/` | Collections, native editor/layout/renderers alongside Home |
| `Essentials/tests/Studio/` | Query/resource/persistence behavior and composition |
| `Extensions/Examples/src/Acme.Processing/` + `tools/studio/Dashboard.Conformance.Probe/` | Separately packaged module and package-only qualification consumer |
| `Studio/tests/acceptance/` | Actual composed product journeys |

Use the existing module architecture and `.Core` boundaries. Domain-authoritative metric calculation and authorized traversal remain source owned; generic orchestration must not branch on dataset identity.

## Staged delivery

| Stage | Outcome | Exit evidence |
|---|---|---|
| D0 | Reviewed product/query/resource/Weaver/UX design | Clean independent review; consequential choices recorded; access gaps explicit |
| D1 | Declaration/discovery/validation/execution; Workflow and independent business contributions | Novel questions, numerical oracle, record/field/aggregate isolation, bounds/cancel/schema/coverage cases |
| D2 | Durable questions/dashboards, revisions, reuse, user/role audiences and copy/revoke | Restart persistence, owner/editor/viewer rights, conflicts/keyed create, cross-tenant/history isolation |
| D3 | Native collections/editor/preview/save/arrange/reopen/refresh/share/drill | Actual composed native journeys/screenshots and Home regressions with provider disabled |
| D4 | Weaver clarification/refinement/preview through native capabilities and selected provider integration | Actual provider turns/tools, exact saved artifact, retained-disclosure denial and zero provider calls on native refresh |
| D5 | Complete user/integrator docs and full qualification | Exact-head correctness/standards/spec/independent UX review, required CI, permitted merge and resulting-main proof |

D1–D3 can use native APIs independently of provider implementation after applicable design agreement. D4 uses the selected existing Copilot adapter and waits only for configured usable live access. Use one active implementation integration lane; independent review/module qualification may run concurrently. Do not widen/reparent Weaver #3443 or reactivate paused programs.

## Validation strategy

1. Independently verify query filtering/grouping/aggregation/comparison-result predicates/sorting and no-denominator math. Mutation controls prove hidden records/fields cannot affect visible aggregate values or membership.
2. Verify tenant and canonical actor identity, audience membership, owner/editor/viewer/copy permissions, next-operation ACL revocation, immutable history, keyed create and CAS. Distinguish live IAM permission reevaluation from token-refresh propagation.
3. Seed real Workflow data through supported runtime operations/stores. Acme.Processing owns declared records/category/duration and uses the identical extension path in a separately built package. Compute expected values outside the implementation being tested.
4. Execute every [UX acceptance](ux-acceptance.md) journey on a committed actual composed host. Retain exact head/source/profile, dirty state, downloadable traces/screenshots and independent inspection.
5. Record safe live-provider/model/connection metadata and tool/save/refresh call counts; never credentials. Deterministic factories remain protocol evidence only.
6. Respect shared build slots. Iterate with narrow whole affected projects, then required architecture/map/locked-restore gates for graph changes. A docs-only checkpoint uses existing docs guards and claims no .NET/UI qualification.
7. Keep failed/skipped/cancelled/not-run/unavailable/incomplete evidence visible. Queued baseline CI is not a pass; exact-head and resulting-main gates remain open.

## Open decisions

Confirmed: separate Dashboards with Mine/Shared with me alongside unchanged Home; the four selected #3604 directions remain authoritative. Copilot adapter and backend configuration are selected; usable live access remains to be demonstrated. Reviewable defaults include current-question propagation, owner-controlled sharing/delete, tenant users/roles, explicit shallow/deep-copy behavior, and honest current-folder/started-cohort/equal-elapsed-week semantics. Resolve routine engineering details in review; do not treat an unanswered card as approval.

Proceed through native and Weaver implementation using the selected Copilot adapter. Runtime, live access, product acceptance, review and resulting-main evidence remain separate; these documents alone do not deliver #3604.
