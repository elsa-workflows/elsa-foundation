# Research: Wave 1 Minimal API Migration

## Decisions

| Question | Decision | Rationale | Alternatives rejected |
|---|---|---|---|
| Feature composition seam | `IWebShellFeature` plus a public static `Map*Api` method | Existing Secrets, Studio Preferences, and Structured Logs can be composed by CShells or a plain ASP.NET host | Retaining FastEndpoints discovery would preserve process-global state and block unloadability |
| Handler shape | Static `RequestDelegate` methods resolving services from `HttpContext` | Keeps endpoint metadata and request delegates framework-owned while preserving module-local service logic | Typed route lambdas can retain module types in framework caches |
| Permission replacement | Keep action permissions; add `attention.read`, `expressions.javascript.render`, and `workflows.runtime.javascript.execute` as module-owned catalog entries | Wildcard-only routes need explicit least-privilege semantics while wildcard compatibility remains a grant | Making these routes public would violate the migration security gate |
| OpenAPI description | Standard response metadata and `RequestDelegate.Invoke`; no custom endpoint DSL | Matches ADR 0068 and existing canary patterns | Recreating FastEndpoints schema/discovery would expand the shared layer |
| Unloadability | Reuse explicit mapping and weak-reference lifecycle fixtures; do not weaken failures | The wave issue requires evidence for every owner | Process-memory observations cannot prove collectible contexts |

## Baseline inventory

The Wave 0 registry identifies exactly eight concrete registrations:

- `Elsa.Api.Capabilities`: `GET /capabilities`
- `Elsa.Attention.Api`: `GET /_elsa/attention/items`
- `Elsa.Expressions.Api`: `GET /expressions/descriptors`, `GET /expressions/variable-types`
- `Elsa.Expressions.JavaScript.Rendering`: `GET /javascript/documents/render`
- `Elsa.Workflows.Runtime.JavaScript`: `POST /javascript/execute`
- `Elsa.Workflows.Dashboard`: `GET /_elsa/workflows/dashboard/definitions`, `GET /_elsa/workflows/dashboard/runs`

The existing endpoint tests and service handlers define the binding and status behavior; migration tests freeze these observations before deleting the legacy endpoint types.
