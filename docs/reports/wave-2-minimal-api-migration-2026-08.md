# Wave 2 Minimal API migration evidence (2026-08)

Issue #1368 Wave 2 replaces exactly 13 first-party FastEndpoints registrations with explicit module mappers. The work is limited to these owners:

| Owner | Routes | Contract families |
| --- | ---: | --- |
| `Elsa.Activities.Bpmn.Interchange` | 3 | BPMN analyze/import/export |
| `Elsa.Modularity.Api` | 2 | module catalog/apply |
| `Elsa.Workflows.ExecutionEvidence` | 3 | correlation/workflow read and delete |
| `Elsa3.Activities.Design.Import` | 5 | upload, analysis, selection, apply, status |

## Immutable compatibility evidence

The before capture was run against the real FastEndpoints host before endpoint deletion and frozen in commit `2a4daa0aa`. It contains 22 deterministic HTTP observations and all 13 OpenAPI operations, including representative authenticated success, anonymous 401, malformed/validation/conflict errors, real BPMN XML, Elsa3 multipart upload, JSON bodies, diagnostics, pagination, polling, delete, status, location, content types, headers, and schema references.

`Wave2MinimalApiCompatibilityTests` now starts only the migrated Minimal API host. It loads the committed HTTP and full-schema OpenAPI fixtures and compares after evidence through `Elsa.Api.Compatibility.Testing`. The comparer has no approval file or blanket waiver. The affected compatibility gate is green with zero deltas.

## Security and ownership

- Each route has one module ownership record, Minimal API authoring metadata, and one Foundation Identity permission disposition.
- Modularity list names `module-management.read`; apply names `module-management.manage`, whose catalog contribution implies read.
- Execution Evidence names catalog-owned `execution-evidence.read` or `execution-evidence.delete`; `execution-evidence.manage` explicitly implies both delete and read.
- Wildcard is tested only as an evaluator-level grant. It is not represented in endpoint policies or module ownership evidence.
- The authorization matrix proves anonymous 401, unrelated 403, exact action grants, manage implications, wildcard, normalized `v1` identity, invalid normalization, and Elsa3 tenant isolation.

## Unloadability

`Wave2MinimalApiUnloadabilityTests` loads each owner assembly in a collectible context five times. Every cycle configures DI, maps the real production feature, materializes and serializes route metadata, disposes the service provider, clears route data sources, unloads the context, and verifies weak references for the load context, assembly, feature type, and representative endpoint. All four owners pass.

## Transition registry

The exact 13 owner entries were removed from `fastendpoints-transition-exceptions.json`; no unrelated owner entries were changed. On this pre-Wave-1 branch the scanner ratchets from 164 to 151 and the architecture test is green. After the Wave 1 rebase, the integration target is the requested 156 to 143 ratchet.

## Verification status

Green locally:

- Wave 2 compatibility and authorization tests (5 tests)
- Wave 2 collectible owner test (5 cycles × 4 owners)
- FastEndpoints transition architecture tests
- Endpoint security architecture tests
- BPMN Interchange tests (107), Execution Evidence endpoint/unit tests (75), and Elsa3 import contract/apply tests (9)
- Studio Preferences coexistence/write/OpenAPI contract tests (16)
- Owner project restores/builds and `git diff --check`

Integration handoff notes:

- This pre-Wave-1 branch reports the temporary 164→151 transition count; reconcile to 156→143 after rebasing Wave 1.
- The generated maps check currently reports nine stale snapshots (including the manifest); no refresh was performed without the required narrow authorization.
- A no-restore full-solution build cannot start 208 unrelated projects whose assets files are absent in this worktree; the affected projects build and test successfully after narrow restores.
- Backend E2E still requires the rebuilt Workbench/fresh database runner. Nightly Integration issue #1323 is an open main-health report and is not a Wave 2 owner regression.
