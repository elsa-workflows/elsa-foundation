# Implementation Plan: Workflow releases and environment promotion

**Branch**: `agent/codex/3590-release-contracts` | **Date**: 2026-11-12 | **Spec**: [spec.md](spec.md)

**Status**: Draft — reviewed product scope; owner adapter, binding and recovery contracts require design review before implementation.

## Summary

Deliver #3590 as one scoped program, preserving exact tested content while coordinating existing owners. The first complete
delivery includes native workflows, ordinary and direct BPMN publication, standalone ETL pipelines and their use as dependencies.
ETL catalog availability is not a new automatic start. Release history never becomes serving authority.

Use existing workflow/template/model identities and hashing; introduce a frozen multi-root release bundle, target-local mapping
and model-locator captures, preview and durable application journal. Extend owning Runtime/Publishing/BPMN/ETL/resource seams
where current APIs cannot preserve exact identity, map physical target locators, pin resources or recover owner commands.
Studio and packaged CLI call the same target APIs. Implementation is staged; every model and agreed criterion remains required.

## Technical Context

**Language/Version**: C#/.NET 10 (`global.json` SDK 10.0.300); TypeScript/React in the existing Studio workspace.
**Primary Dependencies**: Existing Publishing/Runtime/Design Core seams, System.Text.Json canonical/owner codecs, current
Connections/Secrets, capability discovery, Studio Backends/SDK/shared controls, System.CommandLine. Existing BPMN packages
and ETL providers are target prerequisites; promotion installs/upgrades none.
**Storage**: Existing immutable runtime and owner catalog stores, plus provider-neutral release/attempt contracts and durable
EF implementation. Target command guard/effect/receipt must share the owner's transaction. No cross-owner transaction promise.
**Testing**: Risk-based focused domain/EF tests, mutation controls, real child-process hosts, backend e2e, packaged consumer/CLI,
and feature-specific composed Studio/Playwright journeys. Model tests use actual admitted work and persistence.
**Target Platform**: Design-only release creation/export where material is available, Runtime-only target execution/import,
and combined Workbench/Studio. Target features must advertise the supported import/catalog/execution shape honestly.
**Project Type**: Optional publishing/release capability, owner bridges, target API, CLI command family and Studio module surface.
**Performance Goals**: Bounded streaming import/discovery; deterministic set traversal and per-owner staging. No wall-clock CI gate.
**Constraints**: Exact bytes/identities, no runtime-to-Design dependency, no secret transfer, per-run mapping choice, current
resource rotation/revocation, explicit mixed interval and uncertainty, preserved model semantics and existing program priorities.
**Scale/Scope**: Five delivery stages under #3590, one implementation slice at a time; no new issue hierarchy while readiness cap is exceeded.

## Constitution Check

Both constitutions loaded: framework **6.1.0**, Elsa **6.5.0**, plus [review checklist](../../docs/contributing/review-checklist.md).
The stale version in AGENTS does not override the owning documents. This plan ratifies no provisional section.

| Gate | Design application / required evidence |
|---|---|
| Framework §2.1/2.6/2.18; Elsa §E2.2/2.6 | Publishing coordinates through Core ports. Runtime binding/material/start contracts contain no release/Design catalog dependency. Owner bridges stay outside runtime consumers. Verify project references and Runtime-only package witness. |
| §2.5/2.5.1/2.23.1 | Optional feature composes through existing registration patterns; scoped logic services; composition test plus dedicated tests only for meaningful conditional wiring. |
| §2.9/2.10/2.20; Elsa §E2.5 | Provider-neutral persistence contracts; current provider-family adapters; consumer-owned DbContext remains supported. No mandatory Elsa base context or mixed atomic owner backend. |
| §2.22/2.23.3; Elsa §E6 | Internal implementations by default; public owner/import/operation extension ports only where integrators need them; update public API tracking and owning extension catalogs with code. |
| §2.21/2.23.2/2.23.4 | Risk tests for closure/identity, authorization, stale/retry/races, retention, physical-locator mapping and actual process recovery. Existing valid behavior preserved; no source/Markdown assertion tests. |
| Elsa §E2.8/2.9 | Imported catalog content remains source-owned/read-only; no deployment state on authored workflow state and no competing picker catalog. Provisional versioning/triplet text is design context, not newly ratified. |
| Elsa §E5/E7 | Existing computed package versions; compare public API changes, update lock/filter/tier maps when necessary. No package publication; real host/container suites belong to integration tier. |
| Accepted ADR 0043 | Existing Runtime owners select future starts. Direct BPMN receives an owner-mediated serving-selection bridge; no release-owned route/start selector. Proposed October amendment and ADR 0034 stay proposed. |

Initial review finds no authorized need to violate these gates. Post-design review must verify concrete package placement,
model serving bridge, owner-transaction guards and package consumption before contracts are frozen. Required checks and UX
acceptance remain merge gates; document status is not permission to bypass them.

## Project Structure

Documentation is [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts](contracts/),
[quickstart.md](quickstart.md) and the subsequent dependency-ordered task list. The program ledger stays on #3590.

Proposed code ownership separates source/catalog bridges from a Runtime-only target installation. Existing Publishing
engine/API projects transitively bring Design implementations and cannot host the target path. The release subtree remains
inside the pinned Workflows.Publishing domain; new target packages are justified by this actual consumption boundary.

| Existing owner | Proposed responsibility |
|---|---|
| `Essentials/src/Workflows/Publishing/Releases/Core` (new) | Runtime-safe release/preview/attempt and owner codec/import ports; no reference to existing Design-bearing Publishing.Core. |
| `Essentials/src/Workflows/Publishing/Releases/Elsa.Workflows.Publishing.Releases` (new) | Frozen bundle validation/storage coordination, target preview, attempt runner and evidence association; runtime/resource Core seams only. |
| `Essentials/src/Workflows/Publishing/Releases/Api` (new) | Runtime-safe target import/preview/apply/resume/rollback/history, existing permission/capability/tenant conventions; no existing Publishing.Api implementation reference. |
| `Essentials/src/Workflows/Publishing/Releases/Persistence` (new provider family) | Durable release blobs, attempts and target identity; independent target store context and provider-neutral ports. Owner effect/receipt remains in each owner's transaction. |
| `Essentials/src/Workflows/Publishing/Elsa.Workflows.Publishing` and existing Publishing API | Optional source builder/export and Design/catalog projection adapters referencing the lightweight release Core. Never a target-engine dependency. |
| `Essentials/src/Workflows/Runtime` | Runtime-owned immutable deployment context/start capture, command receipt/fence and typed material selection; no Design/release lookup during execution. |
| `Essentials/src/Activities/Bpmn` | Exact mixed native/model export/import and physical-locator resolution, direct-root serving selection and native/model propagation. |
| `Extensions/Etl` | Exact integer-version artifact/catalog import/export, read-only discovery, scoped alias mapping and retained admission/continuation semantics. |
| `Essentials/src/Connections`, `Secrets` and activity bridges | Metadata identity observations, same-read incarnation checks and logical reference resolution preserving ordinary rotation. |
| `Essentials/src/Cli/Elsa.Cli` | Release command family over target HTTP API, independent of migration worker; JSON results and documented exit statuses. |
| `Studio/src/essentials/Elsa.Studio.Workflows`, `Backends`, `Hosting`; model Studio modules | Shared release pages/journey, prepared target connection lifetime and model-owned selection labels; shared UI controls. |
| Existing domain tests, `e2e-tests`, `tools/ux-acceptance` | Risk/consumer/process/Studio proof for the exact delivered commit. |

Follow existing project naming/layout within these proposed locations; exact package/public API review precedes the split.
An M0 packed Runtime-only consumer must exclude all Design implementation packages before target contracts are approved.
Runtime-only targets may retain portable authored blobs for inspection without instantiating a Design catalog; advertised
catalog import availability must distinguish that shape. Combined targets project immutable records through the owning catalog.

## Resubmission after #3911

The [9 November closure](https://github.com/elsa-workflows/elsa-foundation/pull/3911)
requires stacked PRs of roughly 3,000 lines, one spec slice per PR in task order. The exact closure receipt is linked
from [delivery slices](delivery-slices.md); the active program authorization is preserved on current main.
The old branch and uncommitted work remain separate. All previous test evidence is historical and tied to its named
source, not qualification of this documentation head or later resubmissions. Each production slice must re-establish
its affected tests, consumer boundary and current-head gates. The new Runtime core proposal does not authorize
replacing the existing runtime in this feature.

## Critical Path and Review Points

1. **M0: agreement and bounded technical witnesses.** Review spec/model/bundle/promotion/API/Studio contracts together. Prove the
Runtime-only target package closure, existing
selected-version versus artifact-producer behavior, direct-BPMN Runtime slot/typed-material path, source physical-locator
indirection, ETL exact import/read-only catalog and owner guard/effect/receipt transaction design. Update designs from those witnesses.
2. **M1: frozen exact content.** Implement bundle/canonical vectors and complete native/reusable/BPMN/ETL owner discovery. Hold source
retention through durable bundle capture. Add exact target import adapters; no rebuild. Prove empty-target/import/retry/conflict/cycle
and source-GC/download behavior, including arbitrary retained ETL version. Dependencies remain independently inert.
3. **M2: target context and preview.** Add stable authority discovery and scoped requirements, current resource metadata/incarnation checks,
immutable runtime-owned resource/model locator capture, target preview and stale-apply validation. Build the Studio mapping/preview
journey and packaged CLI preview against these APIs. Both different test environments use the same frozen bundle.
4. **M3: durable effect and recovery.** Add owner-side atomic command guard/receipt/revocation, CAS/read-after-unknown and attempt
coordination. Propagate selected capture through every actual native/BPMN/ETL start, saved input, worker, outbox and checkpoint
boundary. Activate explicit roots only; retained dependencies remain callable. Qualify binding-only change, overlapping/external
writers, delayed workers, unknown commits, explicit rollback and long-running exact pin/mapping continuity.
5. **M4: integrated acceptance.** Complete creation/import/evidence/history/rollback Studio/CLI, operator/integrator docs and runnable
examples. Run the full agreed failure matrix through real composed hosts and packaged consumer boundaries, independent visual
inspection and exact-head review/checks. Merge only the agreed fully qualified scope and qualify resulting main.

Every stage records passed/failed/skipped/cancelled/unavailable/incomplete separately. No stage alone completes #3590.
Background repair observes completed effects; new post-interruption transitions require an authenticated authorized resume,
which a pipeline can automate with its normal target identity. No new durable delegation/secret store is introduced.

## Complexity Tracking

No constitutional exception is proposed. The additional runtime context is necessary to preserve tested content while mapping
source-local BPMN locators and per-run resources; a mutable alias table or rewritten artifact would violate selected behavior.
Owner receipts/fences are necessary to account for late effects; a coordinator-only lease cannot fence other publication writers.
The ETL import bridge preserves owner identity without replaying draft publication or fabricating version history.
