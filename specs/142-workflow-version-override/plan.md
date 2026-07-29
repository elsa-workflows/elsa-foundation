# Implementation Plan: Workflow Version Override

**Branch**: `codex/workflow-version-override` | **Date**: 2026-07-28 | **Spec**: [spec.md](spec.md)

## Summary

Extend the normal Workflow Design draft-promotion operation with an optional, author-requested SemVer label, plus a non-mutating authoritative preflight for Studio. When omitted, promotion retains the current server-assigned next-major policy. When present, the server trims the request, parses it with the shared `SemVer` model, and accepts it only when its precedence is greater than the definition's current latest immutable version. Preflight reports the current resolved candidate and readiness but never reserves a version; promotion repeats every check under the existing definition-level lock and durable atomic-write ledger. Tenant scope, the draft-validation gate, and the unique `(definitionId, semVerSortKey)` persistence constraint remain authoritative. Stable additive capability relations let clients offer preflight and exact selection only on supporting hosts.

The detailed decisions, data shape, HTTP contract, and runnable verification scenarios are in [research.md](research.md), [data-model.md](data-model.md), [contracts/workflow-version-override.openapi.yaml](contracts/workflow-version-override.openapi.yaml), and [quickstart.md](quickstart.md).

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`)

**Primary Dependencies**: Elsa Workflows Design API, Elsa Workflows Design Persistence Core and Groundwork, `Elsa.Primitives.Versioning.SemVer`, FastEndpoints, Mediator, Groundwork document store and design atomic writer

**Storage**: Groundwork workflow-design documents and projections; existing tenant-scoped unique index on `(definitionId, semVerSortKey)`; shared `designOperation` idempotency ledger

**Testing**: xUnit unit/contract tests under `tests/Elsa/Workflows/Design`; Groundwork design conformance tests; management API endpoint/capability tests; relevant REST e2e suite after a rebuilt `Elsa.Server`

**Target Platform**: ASP.NET Core management hosts composed from Elsa domain features

**Project Type**: Modular domain library plus management API

**Performance Goals**: One promotion takes the existing definition-level lock and performs no additional cross-definition scan; version parsing, comparison, and duplicate lookup are bounded by one definition.

**Constraints**:

- Keep `POST design/workflows/drafts/{draftId}/promote` as the sole normal-authoring mutation path and retain `workflow-design.manage` authorization.
- Expose a read-only `POST design/workflows/drafts/{draftId}/promotion-preflight` assessment for version selection; it must never reserve an identity or weaken the atomic recheck during promotion.
- Preserve automatic next-major assignment exactly when `requestedVersion` is absent.
- Treat build metadata as semantically equal, because the shared `SemVer` sort key ignores it.
- Bind assignment mode and normalized requested version into the existing operation-key material; replays must never mint or relabel a version.
- Validate candidate parsing, forward precedence, and duplicate identity after acquiring the existing definition lock; persist the existing unique identity constraint as the final race defence.
- Do not add a new anonymous endpoint, a separate custom-version promotion endpoint, a migration of published version rows, Git reconciliation changes, or publication-slot behavior.

**Scale/Scope**: One existing Workflow Design command and mutation endpoint, one new preflight endpoint/assessment contract, capability declarations, a shared version-assignment policy seam, Groundwork persistence implementation, and focused tests.

## Constitution Check

**Pre-design result: PASS with draft-constitution notice.** Both constitution documents are draft, so their gates guide the implementation but require ratification-aware review. This work remains in the `Elsa.Workflows.Design` bounded context: it does not create a Runtime-to-Design reference, does not add a new infrastructure dependency to a `.Core` library, and leaves publication and runtime execution ownership untouched.

| Gate | Evidence and planned treatment |
|---|---|
| Design/Runtime bounded-context split | The request changes Design promotion and its Design API only. Publishing remains a separate downstream operation; Runtime receives no new reference or contract. |
| Domain-owned API | Workflow Design owns both mutation and preflight. Capability discovery is added through the existing API Capabilities declaration mechanism, while endpoint authorization remains action-scoped. |
| Core/implementation separation | Optional request data and version-selection contract belong in `Elsa.Workflows.Design.Persistence.Core`; Groundwork locking and durable mutation remain in `...Persistence.Groundwork`; no provider implementation leaks into Core. |
| Persistence invariants | Immutability is retained. Server-side normalized sort-key identity and the provider's unique index, not client input, enforce uniqueness and ordering. |
| Extension/seam discipline | Reuse `IPromoteDraftToVersionCommand`, `WorkflowVersionNumbering`, `IWorkflowDefinitionVersionStore`, `IDesignAtomicWriter`, and `IApiCapabilitySource`; do not add an ad-hoc database or endpoint bypass. |
| Test discipline | Add focused parser/assignment, replay, lock/race, store uniqueness, endpoint status mapping, and capability relation coverage; run the existing automatic-promotion tests unchanged. |

**Post-design result: PASS.** The data model and contract preserve all listed boundaries. The implementation must update the command contract and the existing Groundwork implementation in lockstep; adding a second custom-version write path would fail this check.

## Project Structure

### Documentation (this feature)

```text
specs/142-workflow-version-override/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
└── contracts/
    └── workflow-version-override.openapi.yaml
```

### Source Code

```text
src/Elsa/
├── Primitives/Primitives/Versioning/
│   └── SemVer.cs
├── Workflows/Design/Api/
│   ├── Capabilities/WorkflowDesignApiCapabilities.cs
│   ├── Commands/WorkflowLifecycleCommands.cs
│   ├── Endpoints/Drafts/Promote.cs
│   └── Handlers/WorkflowLifecycleHandlers.cs
├── Workflows/Design/Persistence/Core/
│   ├── Contracts/IPromoteDraftToVersionCommand.cs
│   ├── Exceptions/
│   ├── Services/WorkflowVersionNumbering.cs
│   └── Stores/IWorkflowDefinitionVersionStore.cs
└── Workflows/Design/Persistence/Groundwork/
    └── Services/GroundworkPromoteDraftToVersionCommand.cs

tests/Elsa/Workflows/Design/
├── Api/Tests/
└── Persistence/
    ├── Core/Tests/
    └── Groundwork/Tests/
```

**Structure Decision**: Keep the capability and HTTP changes in the existing Workflow Design API feature. Keep version-selection abstractions and domain exceptions in Design Persistence Core, and implement the lock/atomic-write behavior only in its Groundwork provider. Reuse the existing `SemVer` and operation-ledger types rather than creating a versioning or idempotency module.

## Implementation Outline

1. Add a value object or explicit request component representing automatic versus exact assignment. It must normalize an explicit wire label by trimming it once, parse it with `SemVer.TryParse`, retain the accepted trimmed label for persistence, and expose the sort key/precedence used for comparison. Keep absent and explicit values distinct in canonical operation material.
2. Add a non-mutating version-preflight service and `POST .../promotion-preflight` endpoint. Under the current definition consistency boundary, it must evaluate draft validity, latest version, automatic/exact candidate, precedence, and current identity availability, returning a structured assessment with no operation marker or reservation.
3. Extend `PromoteDraft` and `IPromoteDraftToVersionCommand` with the optional requested version, and flow it through the existing handler without creating a parallel mutation endpoint or command.
4. In `GroundworkPromoteDraftToVersionCommand`, include assignment mode plus normalized requested label in `PromoteDraftRequestMaterial`. Under the established draft then definition locks, repeat the preflight's validation, load the latest version, resolve automatic or exact assignment, reject malformed/non-forward input, check semantic identity, and atomically persist the version, layout, and operation marker. Map a persistence uniqueness collision to the domain conflict.
5. Add explicit invalid-version and version-conflict outcomes and map them, plus operation-key material conflicts, to the documented HTTP 400/409 results. Preserve current validation-error and not-found behavior.
6. Add static, templated `workflow-draft-promote-version-preflight` and `workflow-draft-promote-exact-version` relations to the existing `elsa.api.workflow-design` capability declaration. Their absence means clients retain automatic promotion without probing an unsupported host.
7. Cover automatic compatibility, valid release/prerelease requests, no-prior-version behavior, whitespace/leading-zero/malformed input, lower/equal/build-metadata-equivalent identity, preflight ready/not-ready assessments, a promotion that changes after preflight, duplicate/race outcomes, identical replay, replay mismatch, authorization, endpoint status mapping, and capability discovery. Then run the focused test projects and the relevant rebuilt REST e2e journey.

## Complexity Tracking

No constitution violation or new project is required.
