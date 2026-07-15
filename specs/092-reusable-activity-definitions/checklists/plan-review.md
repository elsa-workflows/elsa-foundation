# Feature Specification and Initial Plan Review

**Feature**: [Reusable Activity Definitions](../spec.md)

**Reviewed**: 2026-07-15

**Scope**: Feature specification, initial plan, data model, quickstart, and backend contract shapes. A formal Speckit cross-artifact analysis remains a post-`tasks.md` gate because that workflow requires generated tasks.

## Requirement-to-design coverage

- [x] FR-001–FR-007 — catalog identity, multiple drafts, optimistic revisions, authority, and fork behavior are shaped in the authoring API and data model.
- [x] FR-008–FR-016 — public contract, provider manifest, Runtime descriptor, stable provider/consumer keys, defaults, and provider migration are shaped in the authoring and provider/runtime contracts.
- [x] FR-017–FR-027 — deterministic publication, exact dependencies, content addressing, SemVer diffing, version lifecycle, test runs, and Runtime preflight are shaped in the publication plan and focused contracts.
- [x] FR-028–FR-039 — exact template placement, collision-resistant identity, ordinary composite execution, scope isolation, input capture, output propagation, and no child-workflow execution are shaped in the provider/runtime seam.
- [x] FR-040–FR-048 — checkpoints, bookmarks, restart recovery, faults, cancellation, retries, artifact-only Runtime, deployment incidents, and admission policy are covered by the Runtime seam, validation strategy, and quickstart.
- [x] FR-049–FR-055 — hierarchy inspection, separate aggregate state, pinned layout, authorization/redaction, lifecycle policy, dependency reads, upgrades, and test-run evidence are covered by the inspection, dependency, authoring, and error contracts.
- [x] FR-056–FR-062 — RFC 7807 errors, Elsa 3 conversion, clean break, architecture guards, test preservation, reusable terminology, and source-owned forking are explicit in the spec and plan.
- [x] SC-001–SC-012 — measurable publication, execution, restart, inspection, compatibility, migration, architecture, and legacy-removal outcomes are represented in the validation strategy and quickstart.

## Shape review

- [x] Authoring API separates definition identity, draft state, immutable versions, content authority, and version lifecycle.
- [x] Source-owned customization has one explicit fork route, creates a new Design-owned identity, and exposes exact immutable fork provenance without creating shared lineage.
- [x] Validation errors extend Elsa's shared RFC 7807 envelope with stable operation codes, ordered diagnostics, safe locations, status mappings, and disclosure rules.
- [x] Version diff exposes stable change kinds, area/impact/minimum bump, safe before/after projections, provider extensions, and deterministic ordering.
- [x] Dependency reads distinguish authoritative direct edges from rebuildable reverse/transitive projections and bind cursors to their watermark and visibility scope.
- [x] Upgrade plans use exact replacements, pinned draft revisions/heads, dependency-closed selection, atomic apply, and explicit bottom-up multi-stage handoff when a child must publish first.
- [x] Runtime inspection extends the existing execution detail, keeps outer lifecycle separate from subtree aggregate, pages a stable committed hierarchy, supports nested click-through, and reads executed-reference layout lazily.
- [x] Provider and Runtime contracts are separated by stable persisted keys/schemas; Publishing is the only bridge and Runtime remains artifact-only.

## Consistency and risk review

- [x] Definition-level lifecycle ambiguity was removed; lifecycle belongs to immutable versions while definitions expose version lifecycle summaries.
- [x] Fork provenance promised by the API is represented in both the data model and definition read view.
- [x] Existing solution/test paths in the validation commands were checked against the repository (`Elsa.Server.slnx`).
- [x] Runtime -> Design and Runtime -> Publishing dependencies are explicitly forbidden and covered by an architecture release gate.
- [x] New persistence targets provider-neutral Core contracts plus Groundwork/in-memory implementations; no EF schema or migration is planned.
- [x] Existing tests are retained; no deletion is authorized by this plan.
- [x] The plan records the accepted ADR constraints for behavior-only hashes, Source Reference layout, artifact lifetime, and Groundwork-only new durability.
- [x] No arbitrary Foundation depth/node/artifact-size limit is introduced; iterative algorithms expose measurements to replaceable admission policy.
- [x] The provisional constitution wording that still implies CLR descriptor identity is recorded as a follow-up; it does not weaken Activity Catalog authority or permit a boundary violation.
- [x] Studio/frontend work is deliberately deferred, with a handoff window before Slice A API models and Slice E inspection endpoints freeze.

## Review outcome

**PASS** — The backend feature specification and initial plan are internally consistent and implementation-ready at plan granularity. No clarification remains. Task decomposition and the formal Speckit analysis are the next backend planning gates; frontend grilling can begin now and may amend wire/read-model usability before implementation freezes those contracts.
