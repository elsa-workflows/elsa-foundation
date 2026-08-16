# Wave 2 Minimal API migration evidence (2026-08)

Issue #1368 Wave 2 replaces exactly 13 first-party FastEndpoints registrations with explicit module mappers. The work is limited to these owners:

| Owner | Routes | Contract families |
| --- | ---: | --- |
| `Elsa.Activities.Bpmn.Interchange` | 3 | BPMN analyze/import/export |
| `Elsa.Modularity.Api` | 2 | module catalog/apply |
| `Elsa.Workflows.ExecutionEvidence` | 3 | correlation/workflow read and delete |
| `Elsa3.Activities.Design.Import` | 5 | upload, analysis, selection, apply, status |

## Immutable compatibility evidence

The before capture was run against the real FastEndpoints host before endpoint deletion and frozen in commit `b1604579f`. It contains 22 deterministic HTTP observations and all 13 OpenAPI operations, including representative authenticated success, anonymous 401, malformed/validation/conflict errors, real BPMN XML, Elsa3 multipart upload, JSON bodies, diagnostics, pagination, polling, delete, status, location, content types, headers, and schema references.

`Wave2MinimalApiCompatibilityTests` now starts only the migrated Minimal API host. It loads the committed HTTP and full-schema OpenAPI fixtures and compares after evidence through `Elsa.Api.Compatibility.Testing`. The comparer has no approval file or blanket waiver. The affected compatibility gate is green with zero deltas.

## Security and ownership

- Each route has one module ownership record, Minimal API authoring metadata, and one Foundation Identity permission disposition.
- Modularity list names `module-management.read`; apply names `module-management.manage`, whose catalog contribution implies read.
- Execution Evidence names catalog-owned `execution-evidence.read` or `execution-evidence.delete`; `execution-evidence.manage` explicitly implies both delete and read.
- Wildcard is tested only as an evaluator-level grant. It is not represented in endpoint policies or module ownership evidence.
- The authorization matrix proves anonymous 401, unrelated 403, exact action grants, manage implications, wildcard, normalized `v1` identity, invalid normalization, and Elsa3 tenant isolation.

## Unloadability

`Wave2MinimalApiUnloadabilityTests` loads each owner assembly in a collectible context five times. Every cycle configures DI, maps the real production feature, executes representative production request/response serialization through the mapped route, disposes the service provider, clears route data sources, unloads the context, and verifies weak references for the load context, assembly, feature type, and representative endpoint. The production mappers use owner-local source-generated JSON metadata so framework serializer caches do not retain collectible owner types.

## Transition registry

The exact 13 owner entries were removed from `fastendpoints-transition-exceptions.json`; no unrelated owner entries were changed. With merged Wave 1 as the branch base, the executable scanner ratchets from 156 to 143 registrations across eight remaining owners.

## Verification status

Green locally:

- Wave 2 compatibility, authorization, mixed-host, transition, security, and collectibility gate: 21/21
- Wave 2 collectible owner evidence: 5 cycles × 4 owners through production route serialization
- Full architecture suite: 422/422
- BPMN Interchange tests (107), Execution Evidence endpoint/unit tests (75), and Elsa3 import contract/apply tests (9)
- Studio Preferences coexistence/write/OpenAPI contract tests (16)
- `Elsa.Server.slnx` restore and full build: 0 errors (existing repository warnings remain)
- Generated maps check, changed-file formatter verification, and `git diff --check`

No existing backend E2E suite composes the migrated BPMN interchange, module-management, execution-evidence, or Elsa 3 import HTTP owners. The exact real HTTP contract is therefore exercised through the production TestServer mappings and immutable FastEndpoints-before fixtures; adjacent BPMN runtime and reusable-activity authoring scripts do not call these 13 routes and are not claimed as migration evidence.

Nightly Integration issue #1323 remains an independent open main-health report and is not a Wave 2 owner regression.
