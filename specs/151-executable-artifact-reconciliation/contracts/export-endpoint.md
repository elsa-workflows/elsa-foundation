# Contract: Executable artifact export endpoint (FR-B-010a v1 target)

**Project**: `Elsa.Workflows.Publishing.Api` · **Pinned for**: elsa-foundation-studio#493

## Capability advertisement

Added to `PublishingApiCapabilities.StaticDeclaration` (capability id **`elsa.api.publishing`**):

```json
{ "rel": "workflow-executable-export",
  "href": "publishing/workflows/{versionId}/executable-export",
  "templated": true }
```

Rel is kebab-case, no dots (sibling of `workflow-executable-provenance`); href is shell-relative and mirrors the route exactly; `contractVersion` reviewed on add (additive link → no major bump expected).

## Route

```
GET publishing/workflows/{versionId}/executable-export[?target={targetId}]
```

- `{versionId}` uses the existing `RouteConstants.VersionIdConstraint` (`regex(^(?!drafts$).+$)`); route constant added to publishing `RouteConstants`.
- `target` optional, default `"download"`. Unknown target id → 400 with the known target ids in the problem detail.
- Permission: new `PermissionNames` entry (export is read-shaped; distinct from `WorkflowPublishingManage` — final constant name resolved against `PermissionNames.cs` at task time). Configured via `ConfigurePermissions(...)` per endpoint convention.

## Behavior

1. Resolve the **Published-scope** source reference for `{versionId}`; `TestRun`/draft or missing → 404/409 problem detail (FR-B-011: non-portable references are never exported).
2. `IWorkflowArtifactClosureFactory.CreateAsync(versionId)` → `WorkflowArtifactClosure` (root + transitive dependency closure + references + bindings). A dependency missing from the store → 409 problem detail naming the missing `ArtifactId` (export never emits an incomplete closure).
3. Resolve `IWorkflowArtifactExportTarget` by `target` id; `DeliverAsync(closure)`.

## Responses

| Case | Status | Body |
|---|---|---|
| `download` target (InlinePayload) | 200 | The closure JSON (`application/json`) with `Content-Disposition: attachment; filename="{definitionId}-{artifactVersion}-closure.json"` (safe-name rules; filename shape shared with studio#493). |
| Receipt-kind target (future: blob/folder) | 200 | `{ "targetId": "...", "location": "..." }` |
| Unknown version / no Published reference | 404 | problem detail |
| Non-Published-only version (test-run) | 409 | problem detail: export restricted to published scope |
| Incomplete closure in store | 409 | problem detail naming missing dependency artifact id(s) |
| Unknown `target` | 400 | problem detail listing registered target ids |

No FastEndpoints byte-download precedent exists in the repo; implementation uses `Send.StringAsync(json, 200, "application/json")` + a manual `Content-Disposition` header via a small response helper placed beside `ServerSentEventResponseExtensions` (`src/Elsa/Api/FastEndpoints/`).

## OpenAPI

The management-api OpenAPI contract fragment (spec-owned, mirroring `specs/148-authoring-schema-endpoints/contracts/management-api.openapi.yaml` practice) is produced in the tasks phase alongside the endpoint; `elsa.api.publishing`'s enumerated capability ids in the OpenAPI schema are unchanged (rel additions are data, not schema).
