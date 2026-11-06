# Implementation Plan: catalog admission and disclosure

**Branch:** agent/codex/3908-entity-integration-design | **Date:** 2026-11-06
**Spec:** [spec.md](spec.md) | **Owning task:** [#3908](https://github.com/elsa-workflows/elsa-foundation/issues/3908)
**Status:** Scoped technical plan reviewed at 6d8a45fe13c8db82b1767f6a53839ab2e03e26d8; implementation and qualification remain outstanding. The owner approved this catalog boundary at 1bf3d5955a42222a5b62e4b0d463e17c43eedd16; [receipt](https://github.com/elsa-workflows/elsa-foundation/pull/3923#issuecomment-6675758688).
**Inspected source:** 01bfb391886aeb60080d9afe186bcd29a0851a09, including main fd0fdb21eacabe690183ff761432746e9c7e680c.

## Summary and scope

Retain an explicit public/protected requirement with each catalog version/publication and its server-owned lineage. Validate trusted producer admission before writes, preserve requirements through forks and exact retries, and apply one current disclosure decision before every user-facing projection. A missing requirement or policy never becomes a public grant.

This plan covers the catalog unit of the larger entity-integration specification, chiefly FR-003/013–018/028–032 and the catalog portions of A04/A11–A13. It does not complete those full-feature rows. The entity-provider SPI, live record/field policy, source operation authorization, workload grants, Runtime ingress/resumption and consumer projections retain their separate agreements and implementation. Production source-private profiles stay disabled until their complete deployed disclosure/inspection paths qualify. No business-record backend is added.

[Research](research.md) records the scoped decisions. [Data model](data-model.md), [catalog contract](contracts/catalog-disclosure.md) and [qualification guide](quickstart.md) define the reviewable details. The earlier [boundary proposal](design/catalog-disclosure.md) is the owner-agreed direction, not a claim that these contracts or tests already exist.

## Technical Context

| Area | Selected approach |
|---|---|
| Language/runtime | C#/.NET 10, existing CShells, Events, Tasks and MinimalEndpoints. |
| Ownership | Generic models/ports/events in Activities Design Core; persistence commands/queries and canonical store authorization in Persistence.Core; one independently composable Activities Design Disclosure feature; HTTP identity adapters in the existing APIs. Core gains no Identity/ASP.NET dependency. |
| Storage | ActivitiesDesign family 2.0.0, using the existing four relational providers and normal pre-GA Initial regeneration. Current-format markers/projections/receipts retain security material separately from executable behavior. |
| Supported deployment | Fresh format-2 catalog stores; current same-format restart/reopen. Existing preview stores are preserved and unsupported by this format. No in-place/rolling upgrade, legacy receipt import, finalization override or database reset is provided. |
| Test tools | Existing xUnit suites, real SQLite, provider conformance where affected, owned HTTP hosts, deterministic Agent provider fixtures, architecture/composition/maps and required CI. |
| Bounds | Complete inventory of at most 256 non-public or undecidable resources per read; canonical valid-Public rows do not consume that limit. Unknown/malformed states belong in the inventory rather than silently disappearing. Exceeding the bound is unavailable, never a truncated successful count. Canonical IDs remain lossless within the store's 450-character bound. |
| Authorization lifetime | No new reusable positive authorization cache. One read/admission pass ends before a write, model/provider call or tool execution; each subsequent pass rechecks. |
| Performance | Bounded policy selection and provider-side authorized filtering before count/page. No benchmark promise or unbounded remote per-row scan. |
| Compatibility | Breaking preview Core changes are explicit; update all current callers and track their public surface. No default-Public overload or compatibility shim. Computed package versions remain unchanged in source. |

The fresh-store choice follows [ADR 0078's pre-GA policy](../../docs/adr/0078-workflow-executions-are-virtual-actors-and-cluster-membership-is-a-foundation-contract.md#consequences). Family labels are opaque: choosing 2.0.0 documents this mandatory security format, rather than claiming minor labels are readable. The old schema-1 activation/materialization path must refuse new format-2 data. Supported-format unclassified material remains withheld until explicit trusted inventory classification; that is not an old-database upgrade/import claim.

## Constitution Check

Loaded framework v6.1.0, Elsa v6.4.0, both Gate indexes and the [review checklist](../../docs/contributing/review-checklist.md). The following is a plan assessment; no implementation checks are claimed. Recheck after concrete contract review and every implementation batch.

Elsa declares no test cadence gate (the current constitution records the owner's D9 decision). This unit uses risk-based tests alongside implementation, with red/green regression witnesses where existing behavior changes. The absence of a prescribed greenfield cadence does not waive framework §2.23's required behavior/composition evidence and creates no inferred TDD or permission gate.

| Gate | Assessment and required evidence |
|---|---|
| Framework §§2.1–2.4, 2.14, 2.18; Elsa §E2.2 | Activities owns the catalog seam; optional source policies remain separate. No new root domain, heavy Core dependency, Runtime-to-Design edge or cross-domain friend access. |
| Framework §§2.5–2.7, 2.11 | One scoped evaluator/admitter replacement, keyed policy strategies, one named Sequential registration event and aggregator. Registry retains metadata/factories; producer origins bind the exact current-scope registration. Missing, duplicate and uninitialized composition fail explicitly. |
| Framework §§2.9–2.10, 2.20; Elsa §E2.5 | Existing provider-neutral persistence ports and optional DbContext base remain. Admission/equality/inheritance hold for every provider. Reads never classify or write as a side effect. |
| Elsa §§E2.6, E2.8–2.9 | One persisted catalog. Security metadata remains outside authored state and executable behavior/template hashes. Runtime needs no Design lookup. Draft/provisional versioning/reconciliation sections are not treated as ratified approval. |
| Framework §§2.13, 2.16, 2.23.3, 4; Elsa §§E5–E6 | New Disclosure feature earns its identity because APIs and endpoint-free provisioning compose it independently. Public implementer contracts are tracked; implementations are internal sealed, feature public/unsealed, logic scoped. Breaking preview callers are updated together without manually authored package versions. |
| Framework §§2.21–2.23; Elsa §E7 | Risk-based behavior, failure, real persistence and cross-seam tests; no tests on documentation text. Existing suites/tier registrations are reused where possible. Child-host tests follow the integration tier rule. |
| Framework §2.22 | Update owning extension-point catalogs and cross-domain contribution notes in the same source change. Refresh maps/solution filters when projects change. |

No constitution amendment, ADR 0045 binding change or exception is selected. The current accepted pre-GA storage policy is used; parked baseline-freeze #1976 and unrelated paused programs remain untouched.

## Project Structure

    specs/1219-entity-aware-integrations/
      spec.md, plan.md, research.md, data-model.md, quickstart.md
      contracts/catalog-disclosure.md
      design/catalog-disclosure.md, design/runtime-ingress.md
      checklists/requirements.md
      tasks.md                         # only after this scoped plan review

    Essentials/src/Activities/Design/
      Core/                            # requirement/resource/decision, ports and registration event
      Disclosure/                      # evaluator/admission/registry/classification orchestration
      Persistence/Core/                # retained invariants, exact authorizer and selection ports
      Persistence/EntityFrameworkCore/  # columns, guards, projections and format-2 Initials
      Reconciliation/                   # actual producer binding and admitted candidates
      Api/                             # current HTTP context and guarded projections
    Essentials/src/Workflows/
      Publishing/                      # admission/equality/lineage and transport entry-point guards
      Design/Api/                      # exact-version guard before options/provider execution
    Extensions/Agent/src/Workflows/Elsa.Agent.Workflows/
                                       # lossless catalog identity capture and required history admission
    Essentials/apps/Elsa.Workbench/     # explicit feature/package composition
    Essentials/tests/, Extensions/Agent/tests/, e2e-tests/
    tools/ef/                          # existing module-filtered baseline generator

Consumers reference Core ports and declare the feature dependency. Hosts install/compose Disclosure explicitly. Keep human authorization outside the authorization-free Publishing engine; trusted write admission is a separate invariant. Design-only, endpoint-free provisioning/Publishing and combined hosts need concrete composition coverage. Runtime-only hosts gain no Design dependency.

## Implementation order

1. **Core and retained admission.** Implement the reviewed models, fail-closed statuses, registration/producer scope, factory/clone paths and format-2 persistence. Enforce admission and exact requirement equality on direct and source-publisher paths before any Design/Runtime writes, including the already-published shortcut. Consume #3917's qualified ownership fix.
2. **Lineage and replay.** Preserve server-owned conjunctions through drafts, definition headers, forks, migrations, upgrades, conflict copies and all transitive structured catalog dependencies. A Public wrapper cannot launder a private child, and removing that child cannot silently declassify retained material. Every header-bearing projection requires the definition union plus its exact version/draft. ActivitiesDesign atomic/fork format-2 material binds canonical requirements independently of behavior hashes; unsupported catalog material refuses. PublishingLedger activity-publication receipt members/stamp 1 and flat PublishingSnapshotReview stay unchanged. Domain-separate existing request/candidate fingerprint preimages, update every replay/commit recomputation, and reauthorize exact applied outcome or original non-applied draft material before projection. Old-token/hash mismatches conflict without writes/fallback; changed/deleted draft diagnostics are unavailable.
3. **Interactive disclosure and query closure.** Guard catalog, management/picker, availability diagnostics, direct detail/construct, dependency/diff/fork/review/receipt, options and user-facing publish/test paths before projection/material copying. Query providers apply the complete authorized set before search/count/page. Cursor binding includes the current authorization/selection fingerprint.
4. **Historical assistant closure.** Capture a versioned lossless machine-identity snapshot for every count/display contributor in existing owner References, with a 1 MiB escaping-aware attachment cap and 10,000 identities, separately from the existing bounded display prefix. Core snapshot values belong beside the listing result, not in Persistence.Core. Recheck all canonical references and their definition unions before historical evidence/session admission, including snapshots with zero rendered rows. Retained API copies remain under authenticated session access/retention; provenance is not secret storage or a source grant. Uncertified legacy or clipped identities require fresh-session recovery even when a prefix resolves to a public row. Required refusal precedes model/tool use.
5. **Qualify and enable narrowly.** Complete C01–C12 below, required review/CI, merged resulting-main checks and current-commit experience evidence where behavior is visible. Source-private production profile enablement remains a separate, explicit gate including Runtime-only copied-schema inspection and real source/workload policy.

Each batch is reviewable, but no marker-only batch enables a private profile or closes the catalog unit. Maintain one active implementation leaf and the existing integration lane. The full #3908/#3608 outcome remains open after this unit.

## Finite qualification inventory

These are required outcomes, not passed results. [Quickstart](quickstart.md) gives the execution/evidence shape.

| ID | Required outcome |
|---|---|
| C01 | Public/Protected canonicalization, malformed/missing/unknown input and default status fail closed; no mutable JSON/collection alias changes retained material. |
| C02 | Exact current-scope registered producer accepted; same-type fake, wrong scope, unattributed candidate, weakened protected requirement and missing owner refuse before writes on both reconciliation routes. |
| C03 | Real format-2 SQLite publish/reopen preserves requirement/tenant across every participant; intentionally public CLR/intrinsic catalogs work. A missing policy stays recognizably protected after reopen. |
| C04 | Same operation/version with changed requirement conflicts despite unchanged executable hash; exact catalog format-2 duplicate/recovery converges. Unknown/catalog-format-1 atomic receipts refuse. Unchanged foreign publication receipts use the new fingerprint domain and current exact-material authorization, including early replay/GetReceipt and changed/deleted non-applied drafts, with zero writes on mismatch. |
| C05 | Fork/clone/provider migration/upgrade/conflict-copy/retry and newly authored wrappers preserve inherited/dependency conjunctions; removal cannot declassify. A public old version cannot expose a private newer definition header/count/head. |
| C06 | Two tenants, two differently permitted sources under one provider, allowed/denied users: no protected IDs, names, fields/defaults/facets/provenance/diagnostics or option-provider calls escape. |
| C07 | Every list, detail, construct, options, dependency/diff/fork, availability, publish/test and receipt path produces the same decision; guessed and foreign IDs have safe non-disclosing responses. |
| C08 | Provider-side totals/pages/cursors use the complete authorized set; 256-cap overflow is unavailable. Revocation invalidates old continuation scope; no post-page filtering claim. |
| C09 | Lossless 450-character machine IDs, all count contributors (including zero rendered rows), escaped provenance-byte/count limits and forged caller rejection; protected long-ID/public-prefix collision cannot authorize old schema/text. Legacy uncertified snapshots require refusal. |
| C10 | Revoked/outage/missing-owner history and SDK-retained derived text fail required admission before any model/tool call. Revocation between passes within one HTTP stream cannot reuse the earlier allow. |
| C11 | Four fresh ActivitiesDesign provider baselines and format-2 reopen; ActivitiesDesign old-version activation/row refusal; no existing store altered. Supported-format missing classification stays unavailable until exact trusted inventory. |
| C12 | Required composition/architecture/maps/CI and post-merge checks, ordinary owned Workbench restart, independent security/product review, and inspected downloadable UX evidence for visible states. |

C01–C12 qualify this catalog unit only. They do not substitute for full A01–A13, live source SDK, User Task, event recovery, business projections or Runtime workload authority.

## Post-design review and complexity

Independent security and architecture review of the contract/data-model changes at 6d8a45fe13c8db82b1767f6a53839ab2e03e26d8 is clean. The earlier ownership cycle and ambiguous foreign receipt-format claims were corrected in that commit. Rechecking the Constitution Check after those corrections finds no selected exception: reconciliation-visible contracts stay in Core, foreign receipt families keep their existing serialized shapes, and the accepted pre-GA fresh-store policy applies. Task generation may cover this catalog unit; implementation evidence and full-feature agreement remain outstanding. New findings that alter the approved boundary return to its owner; normal implementation choices and required follow-through do not create repeated permission gates.

The added Disclosure feature is a real independently composable catalog boundary. A generic grant store, second catalog, signed admission-token platform, generic persistence upgrade system and data reset are excluded. No violation waiver is requested.
