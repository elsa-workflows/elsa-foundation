# Wave 2 Contract Model

## Endpoint Contract

An endpoint contract is the tuple `(method, route, request binding, response/status, media type, headers, errors, OpenAPI operation, owner, authoring model, permission)`. Before values are immutable files under `tests/Elsa/Architecture/Baselines`; after values are captured from the migrated TestServer.

## Permissions

| Owner | Permission | Implies | Routes |
| --- | --- | --- | --- |
| `Elsa.Modularity.Api` | `module-management.read` | none | GET `/modularity/features` |
| `Elsa.Modularity.Api` | `module-management.manage` | `module-management.read` | POST `/modularity/features/apply` |
| `Elsa.Workflows.ExecutionEvidence` | `execution-evidence.read` | none | both GET routes |
| `Elsa.Workflows.ExecutionEvidence` | `execution-evidence.delete` | none | DELETE route |
| `Elsa.Workflows.ExecutionEvidence` | `execution-evidence.manage` | delete, read | administrative delete/manage callers |

The wildcard grant is evaluator behavior, not endpoint metadata or catalog ownership.

## Scoped Resources

Elsa 3 import collections and receipts are keyed by normalized `(tenantId, userId)` plus their handle/key. The HTTP scope resolver accepts the established NameIdentifier/sub/name fallback and Elsa/conventional tenant claims. A different scope yields the existing not-found/error response without disclosing the resource.

## Evidence Pages

Execution Evidence returns `ExecutionEvidencePage(records, firstSequence, lastSequence, terminal, matchedWorkflows)`. `after`, `waitMs`, `correlationId`, and `workflowExecutionId` retain their old semantics, including bounded wait clamping and plain-text 400 validation.

## Collectibility Evidence

One cycle consists of loading one owner into an isolated context, mapping its routes, exercising representative binding/serialization, generating the OpenAPI document, releasing route/DI/serializer/disposal references, and observing a weak-reference collection. Four owners must pass repeated cycles.
