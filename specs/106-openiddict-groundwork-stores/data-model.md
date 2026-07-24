# Data Model: OpenIddict Groundwork Stores

## Boundary

The following records belong only to `Elsa.Foundation.Identity.OpenIddict.Groundwork`. Existing identity and token-service contracts remain free of Groundwork types. All four units are **global**: no public OpenIddict store operation contains a tenant selector. A future tenant partition is a new feature, not an ambient filter.

## 1. Application Record

| Field group | Canonical content | Physical/query obligation |
|---|---|---|
| Identity/concurrency | id, opaque concurrency value | Primary identity; CAS update/delete. |
| Client configuration | application/client/consent types, client id/secret | Unique client-id route. |
| Presentation/keys | display name(s), JSON Web Key Set | Round-trip only unless a named query proves a projection. |
| Collections | permissions, requirements, redirect URIs, post-logout redirect URIs | Preview.81 linked projections are scalar and cannot maintain/query collection elements. The record unit remains logical until Groundwork#128 supplies bounded element storage and membership routes. |
| Metadata | properties, settings | Canonical JSON round-trip. |

## 2. Authorization Record

| Field group | Canonical content | Physical/query obligation |
|---|---|---|
| Identity/concurrency | id, opaque concurrency value | Primary identity; CAS update/delete. |
| Relationship/state | application id, subject, status, type, creation date | Compound named find/revoke/prune routes; date range where required. |
| Collections/metadata | scopes, properties | Scope containment projection only after public capability proof; properties round-trip. |

## 3. Scope Record

| Field group | Canonical content | Physical/query obligation |
|---|---|---|
| Identity/concurrency | id, opaque concurrency value | Primary identity; CAS update/delete. |
| Naming/presentation | unique name, description(s), display name(s) | Unique name route; presentation round-trip. |
| Resources/metadata | resources, properties | Resource membership route only after public multivalue proof; properties round-trip. |

## 4. Token Record

| Field group | Canonical content | Physical/query obligation |
|---|---|---|
| Identity/concurrency | id, opaque concurrency value | Primary identity; CAS update/delete/redeem/revoke. |
| Relationships | application id, authorization id | Named relationship routes and atomic dependent policy. |
| Lifecycle | subject, status, type, creation, expiration, redemption dates | Compound find/revoke and date-range prune routes. |
| Payload/reference | payload, properties, obfuscated reference id | Unique reference-id lookup; payload/properties round-trip. |

## 5. Relationship and Transition Rules

```text
Application -> Authorization -> Token
Application -----------------> Token
Scope -----------------------> referenced by authorization scope values
```

- Application deletion/revocation performs its declared dependent authorization/token decision atomically; no dangling relation remains.
- Token refresh redemption compares the observed version/status and changes status/redemption value exactly once. A subsequent concurrent redemption sees the authoritative outcome and cannot issue a second successor.
- Prune and bulk revoke execute a finite declared mutation route, return exact changed count, preserve cancellation, and have named allowed durable outcomes for before-call, in-UoW, after-commit/before-acknowledgement, and recovery interruption points.
- A provider failure translates at the feature boundary; cancellation is never translated or swallowed.

## 6. Route Catalog

| Route family | Required examples | Bound/ordering |
|---|---|---|
| Point/unique | application by client id; scope by name; token by id/reference id | Single result. |
| Multivalue | application by redirect/post-logout URI; scope by resource; authorization by scopes | Explicit finite result bound and stable id tie-break; gated on public multivalue proof. |
| Compound filters | authorization/token subject, client/application, status, type | Finite page or bounded bulk mutation; stable id tie-break. |
| Relationship | authorizations/tokens by application; tokens by authorization | Finite page/batch; stable id tie-break. |
| Lifecycle range | token/authorization prune by date | Server-side range and finite batch/mutation bound. |
| Administrative list/count | all four stores' offset/count operations | Validated maximum page/count; deterministic id order. |

## 7. Schema and Evidence Records

- **Storage declaration**: logical unit id, version, entity form, canonical kind, fields/indexes, provider capabilities, naming-policy result, and fingerprint.
- **Capability admission**: exact Groundwork family/tool version, provider topology, route/mutation evidence, and readiness diagnostic.
- **Provider evidence**: provider/version/topology, manifest fingerprint, scenario, independent-client count, failure window, result digest, native plan/mutation-plan artifact, restart outcome.
- **Performance submission**: #646 workload id, fixed dataset/payload/concurrency, correctness digest, physical form, machine/provider identity, and pass/redesign/blocked verdict.

## 8. Preview.81 Collection-Element Blocker

Applications, authorizations, scopes, and tokens remain four distinct logical record
units. They are not yet declared as four physical entity tables:

- `PhysicalTableDefinition.PhysicalEntityTable` admits projected scalar columns and
  physical indexes, but has no linked projection/key parameter.
- The shared/dedicated document forms expose only scalar linked projections and
  maintain one linked row per canonical document.
- OpenIddict requires searchable collection membership for redirect URIs,
  post-logout redirect URIs, authorization scopes, and scope resources, including
  an owner-contains-all route for minimal authorization scopes.

Neither choosing shared/dedicated storage nor adding adapter-owned membership
documents satisfies the contract: the former cannot expand collection elements,
and the latter would require forbidden client-side intersection for contains-all.
Groundwork#128 owns portable element maintenance, grouping/membership queries,
readiness, and four-provider evidence. The manifest/store scaffolding gate remains
blocked until that executable capability is published; #646 then selects among
the admitted physical forms.
