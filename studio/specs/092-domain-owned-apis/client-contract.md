# Domain-Owned Management Client Contract

Elsa Studio discovers management APIs with one authenticated `GET /capabilities` request per active
shell. The response is permission-neutral: it describes installed, stable API contracts and their
relative links, while each domain endpoint continues to enforce its own action permission.

The Workflows client owns one cache and five domain-specific clients:

| Capability | Client | Responsibility |
|---|---|---|
| `elsa.api.workflow-design` | `api/workflowDesign.ts` | Definitions, drafts, versions, scoped variables, contextual input options |
| `elsa.api.activity-design` | `api/activityDesign.ts` | Normalized authoring catalog and availability management |
| `elsa.api.expressions` | `api/expressions.ts` | Expression and variable-type descriptors |
| `elsa.api.publishing` | `api/publishing.ts` | Preflight, publication slots, policy, publish/unpublish/restore, test runs |
| `elsa.api.runtime` | `api/runtime.ts` | Executables, provenance, execution dispatch, instances, diagnostics |

Clients resolve stable link relations from the capability document. They do not infer an installed
API from an Elsa composition feature name, probe alternate routes, retry a legacy route after a 404,
or use demo endpoints. If a capability or optional relation is absent, the client raises
`ApiCapabilityUnavailableError` before emitting a domain request. UI callers must render the
corresponding action or view as disabled/unavailable.

`api/workflows.ts` remains a private compatibility barrel only. It contains no paths, transport
fallbacks, or cross-domain behavior; new imports should target the owning domain client directly.

Executable lifecycle mutation is Publishing-owned. Runtime exposes immutable executable inspection,
provenance, dispatch, and workflow execution state. Definition creation sends catalog-authored
`initialState` and optional layout, never a root-kind shortcut.

The authoritative wire contract is the Foundation specification at
`specs/092-domain-owned-apis/contracts/management-api.openapi.yaml` in `elsa-foundation`.
