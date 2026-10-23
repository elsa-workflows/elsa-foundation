# OpenIddict Store Contract

## Contract Scope

The concrete package implements OpenIddict 7.5 application, authorization, scope, and token store interfaces. Its public behavior is the external OpenIddict contract plus the guarantees below; it does not introduce Groundwork types into Elsa identity abstractions.

## Required Capability Groups

| Store | Required groups |
|---|---|
| Application | instantiate/CRUD/CAS; plain and bounded count/list; id/client-id/redirect/post-logout lookup; all scalar, localized, JSON, and collection descriptor round trips. |
| Authorization | instantiate/CRUD/CAS; id/application/subject/compound lookup; bounded count/list; creation/status/subject/type/scope/property round trips; prune and revoke families. |
| Scope | instantiate/CRUD/CAS; id/name/name-set/resource lookup; bounded count/list; name, localized description/display, resource, property round trips. |
| Token | instantiate/CRUD/CAS; id/reference/application/authorization/subject/compound lookup; bounded count/list; lifecycle/payload/property/reference round trips; prune and revoke families. |

## Deterministic Behavior

- Unique client id, scope name, and obfuscated reference id use create-only semantics.
- Offset pages have explicit finite count/offset validation and stable id ordering.
- Stale update/delete maps to the OpenIddict concurrency failure; successful update rotates the opaque concurrency value.
- Named operations use declared bounded routes. Unsupported shapes fail before provider I/O.
- Generic query/projection delegate overloads admit only a declared restricted translation. The current manifest declares no generic translation route, so all twelve generic overloads fail immediately with capability code `ELSA-OIDC-GW-001`, before the delegate is invoked or provider work starts; they never receive a general queryable collection or materialized full result.
- Refresh redemption/revocation, dependent deletion, prune, and bulk revoke preserve their declared atomic/failure/recovery outcomes and exact affected count.

## External Registration Contract

One feature registration replaces all four OpenIddict Core store implementations with scoped Groundwork stores. It preserves the existing server, validation, selector, and public token-service registrations. Resolving all four OpenIddict managers and stores is a mandatory feature registration test.

## Error Contract

- Expected stale external writes surface OpenIddict's concurrency outcome.
- Unsupported generic delegates surface `ELSA-OIDC-GW-001` with the adapter operation identity.
- Missing schema/capability/topology blocks readiness before token-serving traffic.
- Provider/serialization failures are translated to documented feature-scoped failures with context and preserved inner cause.
- Cancellation propagates unchanged.
