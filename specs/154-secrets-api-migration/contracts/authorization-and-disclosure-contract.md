# Secrets Authorization and Disclosure Contract

## Catalog ownership

- Owner: `Elsa.Secrets.Api`.
- Contributed permissions: `secrets:read`, `secrets:write`, `secrets:update-value`, `secrets:delete`, `secrets:test`, `secrets:use`, `secrets:import`, and `secrets:export`.
- `secrets:write` implies `secrets:read`.
- No other implication is declared.
- `*` remains an explicit administrative grant and is never cataloged.

## Endpoint declarations

Each operation declares one canonical Foundation `Any(*, action)` policy and standard endpoint permission metadata. Endpoint handlers do not read permission claims.

## Required authorization outcomes

- Anonymous caller: challenge (`401`).
- Authenticated normalized caller without the action permission: forbid (`403`).
- Adjacent action permission only: forbid, except reviewed write-to-read implication.
- Exact action grant: allow.
- Write grant on read endpoints: allow through catalog implication.
- Explicit wildcard grant: allow.
- Untrusted or ambiguously normalized principal: deny.
- Resource-handler denial: remains authoritative.

## Tenant outcomes

- Data operations require one normalized tenant claim and pass only that value to domain services.
- Missing tenant returns the captured forbidden response without invoking a domain service.
- Same-name cross-tenant reads and mutations remain isolated and use the captured invisible/not-found behavior.
- Descriptor discovery is read-authorized but intentionally tenant-independent.

## Disclosure outcomes

A unique sensitive marker submitted as value, configuration key, provider metadata, or provider exception detail must not appear in:

- success or failure response bodies;
- response headers;
- list, get, picker, descriptor, or lifecycle projections;
- ProblemDetails fields/extensions;
- consumed OpenAPI response schemas/examples;
- audit record fields or rendered log messages.

Rejected authorization, tenant, binding, validation, and conflict cases must not mutate storage.
