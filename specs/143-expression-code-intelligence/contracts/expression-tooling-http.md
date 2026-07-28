# HTTP Contract: Expression Tooling

All routes are additive and shell-relative. Responses produced by an endpoint handler use
`Cache-Control: no-store`. Exact route constants are implementation-owned but must remain
canonical capability targets.

## Capability advertisement

`Elsa.Workflows.Design.Api` advertises capability `expressions.tooling.v1` only when the Design context service and at least one expression-type tooling provider are composed, with links:

| Relation | Method | Target | Purpose |
|---|---|---|---|
| `expression-tooling-descriptors` | `GET` | `design/workflows/expression-tooling/descriptors` | List composed expression types and their declared tooling capabilities. |
| `expression-tooling-context` | `POST` | `design/workflows/expression-tooling/context` | Resolve a location-scoped Design context. |
| `expression-tooling-validate` | `POST` | `design/workflows/expression-tooling/validate` | Validate one document against current/supplied context. |
| `expression-tooling-symbols` | `POST` | `design/workflows/expression-tooling/symbols` | Search/page visible symbols or member children. |
| `expression-tooling-completions` | `POST` | `design/workflows/expression-tooling/completions` | Request per-language completions. |
| `expression-tooling-hover` | `POST` | `design/workflows/expression-tooling/hover` | Request per-language hover content. |

Advertising the capability is not an authorization grant. Missing links mean the feature is not composed; a client continues generic editing.

## Request bodies

All request bodies use camelCase. The location fields are flat; clients never submit a
`document` object, symbols, expected types, permissions, or graph state.

```json
{
  "contractVersion": { "major": 1, "minor": 0 },
  "workflowDraftId": "draft-123",
  "nodeId": "write-line",
  "propertyKey": "text",
  "expressionType": "JavaScript",
  "documentRevision": "opaque-client-revision",
  "source": "'Hello ' + args.name",
  "contextRevision": "optional-opaque-revision",
  "cursor": { "line": 0, "character": 16 }
}
```

`context` and `symbols` accept only the six location fields plus optional `contextRevision`,
`search`, `skip`, and `take`. `validate` adds `source`; `completions` adds `source` and
`cursor`; `hover` adds `source` and `position`. The server validates the location against
authoritative current draft state. `source` is accepted only for context-bound ad-hoc
tooling; full-draft validation reads current persisted draft source. It is never returned to
another caller or emitted in telemetry.

## Common response envelope

```json
{
  "result": {
    "state": "success",
    "contractVersion": { "major": 1, "minor": 0 },
    "documentRevision": "evaluated-revision",
    "contextRevision": "evaluated-context-revision",
    "payload": {}
  }
}
```

The DTOs for descriptors, context, symbols/completions, hover, and validation all wrap an
`ExpressionToolingOutcome<T>` in the top-level `result` property. The serializer emits
camelCase enum values: `success`, `supportedEmpty`, `unavailable`, `unauthorized`,
`incompatible`, `stale`, or `canceled`. Non-success outcomes contain only safe
state/code/version/revision metadata; they cannot contain symbols, source, live values, or a
partial context.

## Authorization and status behavior

| Condition | HTTP result | Envelope state | Disclosure |
|---|---:|---|---|
| Unauthenticated | 401 | none | No draft/location/provider work. |
| Missing Workflow Design read permission | 403 | none | No draft/location/provider work. |
| Invalid or undisclosed location | 200 | `unavailable` | No unrelated draft data. |
| Provider absent/faulted | 200 | `unavailable` | Safe code/message only. |
| Version/capability mismatch | 200 | `incompatible` | Supported versions/capabilities only. |
| Revision superseded | 200 | `stale` | Opaque current revisions only. |
| Caller canceled | client disconnect/transport cancellation | none, or `canceled` when a response is possible | No partial response. |

## Consequential-operation gate payloads

Test-run records expose `expressionValidation.state`, `expressionValidation.unavailableAcknowledged`, and serialized sanitized diagnostics in their metadata. A test-run request may include `acknowledgeUnavailableExpressionValidation: true`; this is accepted only when the full-draft result is unavailable and there are no known error diagnostics. Publication/promotion has no equivalent override.
