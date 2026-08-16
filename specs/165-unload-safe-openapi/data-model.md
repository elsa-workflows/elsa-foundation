# Data Model: Unload-Safe OpenAPI Boundary

The work unit introduces no durable storage. These are endpoint-build and diagnostic concepts with explicit lifetime ownership.

## OpenAPI lifetime boundary

Represents the accepted rule for one endpoint's documentation metadata.

| Field | Meaning | Validation |
|---|---|---|
| Owner | Stable module/host owner identifier | Required; obtained from existing endpoint ownership metadata |
| Classification | `HostStatic` or `SharedContract` for accepted first-party endpoints | Required; a collectible implementation cannot claim its own artifacts are shared |
| Endpoint | Route display identity used for diagnostics | Required after route construction |
| Checked categories | Request, response, metadata object, member/method, delegate/transformer, serializer artifact | Fixed closed set in the validator |

The accepted metadata record is immutable and contains only primitive values/enums from the shared assembly.

## OpenAPI metadata violation

Represents one unsafe reference found while the candidate endpoint is being built.

| Field | Meaning | Validation |
|---|---|---|
| Owner | Endpoint owner | Required |
| Shell | Dynamic shell identifier when available | Optional for static-host mapping |
| Generation | Candidate generation when available | Optional for static-host mapping; non-negative when present |
| Endpoint | Display name and/or route pattern | Required |
| Category | Closed validation category | Required |
| Artifact identity | Assembly-qualified type/member identity or delegate target identity | Required and deterministic |
| Load context | Diagnostic name and collectible flag | Required when an assembly is involved |

Violations are ordered deterministically by category and artifact identity before the exception message is produced.

## Stable API contract assembly

A package/assembly whose lifetime intentionally outlives replaceable API implementations.

| Property | Rule |
|---|---|
| Name | Owner-scoped `*.Api.Core` for API-only wire contracts, or an existing domain Core when the model genuinely belongs there |
| Contents | Wire models, enums, value objects, and contract exceptions only |
| Dependencies | No ASP.NET Core/OpenAPI implementation dependency and no heavy external package |
| Versioning | Contract SemVer; incompatible changes require the shared-contract restart/version boundary |
| Compatibility | Existing public namespaces remain; moved public types are forwarded from the former assembly where necessary |
| Runtime | Loaded in a non-collectible/shared contract context for the lifetime of dependent module generations |

## State transitions

```text
Endpoint mapping
  -> Completed metadata assembled
  -> Lifetime validation
     -> Accepted: boundary metadata attached -> candidate may publish
     -> Rejected: deterministic exception -> candidate discarded -> prior generation remains

Published generation
  -> API Explorer/OpenAPI may retain stable contract artifacts
  -> Generation replaced or removed
  -> routing/provider/handler artifacts drain and release
  -> implementation load context becomes collectible
```
