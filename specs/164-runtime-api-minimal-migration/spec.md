# Feature Specification: Runtime API Minimal API Migration

**Feature Branch**: `codex/1375-wave9-runtime-minimal-apis`

**Created**: 2026-08-16

**Status**: Draft

**Input**: Issue #1375 — migrate the complete 24-registration `Elsa.Workflows.Runtime.Api` owner slice from FastEndpoints to owner-local ASP.NET Core Minimal API mappings.

## User scenarios

### Runtime clients retain the existing REST contract (P1)

Authorized clients can inspect executables, workflow instances, activity evidence, dispatches, diagnostics, and alteration plans through the same 24 method/path registrations, response contracts, errors, and status dispositions.

**Independent test**: an immutable historical FastEndpoints host captures all 24 anonymous challenges, authenticated successes, route/body binding cases, malformed/empty/null/content-type errors, not-found responses, alteration statuses, and OpenAPI operations; the Minimal API host consumes that evidence.

### Permissions remain framework-neutral (P1)

Anonymous callers receive 401, authenticated callers without the catalog action receive 403, and exact, implied, wildcard, normalized identity, tenant/resource, and evaluator behavior remain owned by Foundation Identity rather than endpoint authoring.

**Independent test**: Runtime Minimal API and a retained FastEndpoints canary use the same policy provider/evaluator and pass the shared permission matrix.

### Hosts compose and unload Runtime safely (P2)

The public feature can be configured and mapped by a shell without FastEndpoints production references, stable owner/name/tag/security metadata is published, and repeated endpoint/DI/serialization cycles do not retain collectible owner state.

**Independent test**: three collectible-context cycles execute mapped delegates, bind/serialize typed values, invoke configured authorization/provider seams, dispose the host, and assert weak references are collected.

## Requirements

- **FR-001**: Replace exactly the 24 reviewed Runtime FastEndpoints registrations with one owner-local Minimal API mapper.
- **FR-002**: Preserve route templates, methods, successful response shapes, status/error/media-type/header behavior, query binding, route-over-body precedence, and cancellation behavior; differences require explicit evidence and approval.
- **FR-003**: Use endpoint metadata for exactly one catalog-owned permission action per route; wildcard remains evaluator-level compatibility.
- **FR-004**: Keep authentication middleware and Foundation Identity policy evaluation outside the endpoint framework and prove anonymous 401 versus authenticated 403.
- **FR-005**: Publish stable operation names, Runtime owner/tag/authoring metadata, public/security dispositions, typed request/response metadata, and 401/403 metadata.
- **FR-006**: Use owner-local source-generated JSON serialization for all mapped Runtime request/response types.
- **FR-007**: Remove only the 24 Runtime production registrations and obsolete owner FastEndpoints reference; preserve the retained FE oracle/coexistence test reference until all migration waves finish.
- **FR-008**: Commit immutable before HTTP/OpenAPI evidence with source/runner provenance, route manifest, comparison tests, report, Spec Kit artifacts, and generated maps.

## Out of scope

- Migrating other Elsa API owners.
- Changing public route contracts or replacing HTTP/JSON.
- Removing the shared FastEndpoints package before all approved migration waves complete.
- Replacing Runtime persistence, mediator, authorization, or alteration domain behavior.

## Success criteria

- **SC-001**: The baseline runner is executable from a historical FE source commit and records 24 registrations, 24 OpenAPI operations, and complete HTTP evidence before production deletion.
- **SC-002**: Runtime owner tests, route/OpenAPI composition tests, shared authorization matrix, and retained FE canary pass.
- **SC-003**: Three collectible-context cycles pass with real mapped delegate, binder, serializer, auth/provider, DI, and disposal execution.
- **SC-004**: Transition ratchet, maps, formatter, full Architecture suite, affected E2E, and solution build pass.
