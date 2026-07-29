---
status: proposed
date: 2026-07-28
decision_context: Workflow version override specification 142
---

# Author-requested forward workflow versions preserve automatic assignment

Normal authored promotion may accept an optional exact semantic-version label. The service remains the authority for parsing, normalization, precedence, uniqueness, authorization, draft validation, and the immutable version it commits. When no label is requested, the existing automatic next-major policy continues unchanged. An exact request is accepted only after surrounding whitespace is trimmed, the resulting label parses under the shared SemVer model, and its precedence is strictly greater than the current latest immutable version for that workflow definition.

The Workflow Design API also exposes a non-mutating, capability-discovered version preflight. It evaluates the current draft, latest immutable version, automatic or requested exact candidate, and semantic identity availability, returning a structured readiness assessment. Preflight does not reserve a version, write an operation marker, or create a durable version. It is therefore authoritative for its observed state but advisory for a later mutation. Promotion repeats all validation and identity checks under its definition-level lock and atomic-write boundary.

The accepted, trimmed label is stored on the immutable `WorkflowDefinitionVersion`; the existing normalized SemVer sort key provides ordering and identity. Build metadata does not create a distinct identity, so labels differing only in build metadata conflict. The current definition-level promotion lock serializes the comparison and write intent, the persistence unique constraint on `(definitionId, semVerSortKey)` remains the final race defence, and existing draft validation remains an in-lock promotion gate. A malformed or non-forward request creates no version and is invalid input; an occupied or persistence-racing semantic identity creates no version and is a conflict.

Exact selection is part of the durable operation material. Repeating an operation key with the same draft, assignment mode, and normalized requested label returns the original authoritative result; reusing that key with automatic versus exact assignment, or with a different exact label, conflicts. This prevents retry ambiguity even when the response to a committed promotion was lost.

The Workflow Design API continues to expose one normal promotion operation. Supporting hosts add stable, templated `workflow-draft-promote-version-preflight` and `workflow-draft-promote-exact-version` relations to the existing `elsa.api.workflow-design` capability declaration, pointing to preflight and promotion respectively. Management clients use the relations to offer preflight and exact assignment only when supported; their absence leaves automatic promotion fully compatible. Capability discovery is permission-neutral, while both endpoints retain existing action-scoped authorization.

## Supersession of ADR 0034 D2

This ADR supersedes **only the version-assignment premise** in [ADR 0034](0034-workflow-definitions-reconcile-from-and-export-to-git.md), D2: versions are no longer necessarily system-assigned. A qualified authorized author may request a server-validated forward version, satisfying the author-assigned version plus uniqueness/monotonicity prerequisite that D2 identified.

It does **not** supersede ADR 0034's GitOps v1 topology. The v1 Git reconciliation design remains a single promote-and-export catalog with read-only import consumers. This decision neither enables multiple promoting catalogs nor makes Git a first-class authoring store; those require a separate decision about distributed contention, content authority, and conflict handling.

## Considered options

- Always deriving semantic bumps from workflow diffs was rejected. Elsa has no authoritative classifier for whether an authored change is major, minor, or patch, and a policy guess should not become immutable identity.
- Requiring exact versions for every promotion was rejected. It would break the established automatic path and add unnecessary release-process detail to routine authoring.
- A separate exact-version mutation endpoint was rejected. It would duplicate promotion authorization, validation, idempotency, and concurrency boundaries.
- Client-only validation and version reservation during preflight were rejected. The former cannot observe the authoritative catalog; the latter adds a lease lifecycle and abandoned-reservation failure mode without replacing promotion's atomic recheck.
- Trusting a client-side comparison or check-before-lock was rejected. Immutable identity is a server-side persistence invariant and must be checked inside the definition-level boundary.
- Treating build metadata as a distinct version identity was rejected because the shared SemVer model intentionally ignores it for equality and precedence.

## Consequences

Foundation must expose the optional request field, non-mutating preflight, stable capability relations, exact-version domain validation, typed invalid/conflict outcomes, and replay-safe operation material as one coherent change. Studio can retain automatic publication by default and capability-gate its advanced exact-version control, using preflight for responsive feedback but treating a promotion conflict as a normal concurrent outcome. Existing clients continue to omit the field and receive automatic next-major assignment.

The decision makes forward author-requested labels admissible; it does not assign semantic meaning to the label, relax version immutability, change publication-slot authority, or solve multi-writer Git reconciliation.

## Linked work

- [Workflow Version Override specification](../../specs/142-workflow-version-override/spec.md)
- [Implementation plan](../../specs/142-workflow-version-override/plan.md)
- [Workflow definitions reconcile from and export to Git](0034-workflow-definitions-reconcile-from-and-export-to-git.md)
