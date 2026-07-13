# Completion Audit: Domain-Owned Management APIs

## Disposition

spec 092 is implemented across Foundation and Studio. The normative design remains in `spec.md`; the
OpenAPI document is the wire contract, `migration-matrix.md` records the facade disposition, the ADRs
record durable architectural decisions, and this audit records implementation and validation evidence.

Coordinated implementation revisions:

- Foundation implementation baseline: `753b8987`
- Foundation current-main integration revision: `40c2307b`
- Studio implementation revision: `fbf49fc0`
- Studio coordinated spec-renumbering revision: `aa01db32`

The work unit moved from spec 091 to spec 092 when current `main` assigned spec 091 to structured-log
replay cursors. The Foundation completion-audit commits follow the implementation baseline and record
completion evidence, current-main integration, and final generated-map state.

## Functional requirement evidence

| Requirements | Result | Primary evidence |
|---|---|---|
| FR-001–FR-010 | Pass | Domain API feature projects, `DomainManagementApiCompositionTests`, `ManagementApiOperationInventoryTests`, and `ElsaServerReferenceCompositionTests` prove one supported domain-owned API, custom-host composition, explicit ownership, and no server facade. |
| FR-011–FR-021 | Pass | `Elsa.Api.Capabilities`, capability catalog/endpoint tests, multi-shell discovery tests, duplicate-equivalence diagnostics, typed Sources, authentication tests, and domain feature dependency declarations prove one permission-neutral shell discovery document. |
| FR-022–FR-034 | Pass | Workflow Design lifecycle endpoints/tests, first-class `WorkflowDraftView`, bounded list projection tests, soft-delete/restore/permanent-delete tests, persisted-version guards, and Studio editor/instance tests prove the Design lifecycle and Runtime-pinned inspection split. |
| FR-035–FR-040 | Pass | Activity authoring catalog, availability endpoints/tests, Workflow Design contextual input-option analysis, Expressions API tests, and Studio direct-domain client tests prove the authoring/descriptor ownership boundaries. |
| FR-041–FR-057 | Pass | Publication slot, policy, preflight, activation, projection reconciliation, HTTP trigger integration, source-reference retirement, normalized public DTOs, and Studio publication UX tests prove replacement-by-default and explicit named coexistence. |
| FR-058–FR-066 | Pass | Runtime executable/provenance APIs, immutable artifact stores, retained-execution projection, root-write lease coordination, and garbage-collector tests across all retained statuses prove the union-of-roots retention model without loading every execution. |
| FR-067–FR-074 | Pass | Endpoint security sweep, contract-major capability versions, coordinated Studio client migration, facade inventory/migration matrix, ordered task history, amended retention ADR, and publication lifecycle ADR prove authorization, compatibility, migration, and decision-record requirements. |

Every identifier from FR-001 through FR-074 is covered exactly once by the contiguous ranges above.

## Success-criteria evidence

| Criterion | Result | Evidence |
|---|---|---|
| SC-001 | Pass | Custom TestHost completes representative Design, Activity, Expressions, Publishing, and Runtime journeys without referencing `Elsa.Server`. |
| SC-002 | Pass | Server architecture guard and zero-legacy search find no management endpoint implementation in `src/Apps/Elsa.Server`. |
| SC-003 | Pass | Management API inventory and `migration-matrix.md` map every former operation to one owner or an explicit removal rationale. |
| SC-004 | Pass | Studio Workflows/Weaver suites and legacy-literal sweep make zero requests to the removed facade. |
| SC-005 | Pass | Capability-cache tests prove one coalesced `/capabilities` request per shell with no domain probing. |
| SC-006 | Pass | Multi-shell capability tests prove advertised links resolve in-shell and omitted domains remain absent. |
| SC-007 | Pass | HTTP publication integration proves `/foo` to `/bar` default-slot replacement and failure-safe authority. |
| SC-008 | Pass | Concurrent slot transition tests prove at most one authoritative publication per definition and slot. |
| SC-009 | Pass | Retention tests protect executables pinned by running, suspended, completed, canceled, and faulted records. |
| SC-010 | Pass | Garbage-collection tests collect only after live references and retained executions are both absent, subject to policy and leases. |
| SC-011 | Pass | Definition projection regression test enforces the bounded read budget and returns Studio summary facts without per-item requests. |
| SC-012 | Pass | Coordinated Foundation and Studio revisions were validated together after facade removal. |

## Validation evidence

Foundation:

- Full solution tests pass after adding the new domain test projects to `Elsa.Server.slnx`.
- Full solution build passes with zero errors; current Groundwork deprecation warnings and the existing
  `NU1510` package-pruning warning remain non-blocking.
- Publishing API: 140 tests pass after public status/action/source/trigger normalization.
- Workflow Design API: 52 tests pass; Runtime: 830 tests pass; Architecture: 87 tests pass.
- Zero-legacy facade search returns no matches.

Studio:

- Full typecheck passes.
- Full tests pass, including Workflows 468/468, Weaver 8/8, and Web 320/320. On the local Node
  25.8.0 runtime the documented `--no-experimental-webstorage` compatibility flag is required; without
  it Node's experimental global storage shadows jsdom's `localStorage`.
- Full build passes.
- Lint passes with zero errors and 42 non-blocking warnings.
- Legacy facade/demo/descriptor fallback search returns no matches.

## Residual notes

- API routes remain unversioned; capability documents carry contract major versions as required.
- Physical executable deletion remains a retention/privileged concern. Studio exposes publication
  unpublish/restore operations and treats Runtime executables as read-only artifacts.
- `Elsa.Server` now demonstrates composition only; integrators install the domain features they need.
