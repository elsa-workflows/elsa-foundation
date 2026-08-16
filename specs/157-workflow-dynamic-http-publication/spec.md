# Feature Specification: Workflow-authored Dynamic HTTP Publication

**Feature Branch**: `codex/1366-dynamic-http-metadata`
**Created**: 2026-08-16
**Status**: Draft
**Input**: Issue #1366 and ADR 0068

## User Scenarios & Testing

### User Story 1 - Publish an inspectable dynamic route set (Priority: P1)

As a shell host, I can publish workflow-authored HTTP routes as one validated immutable generation so that every route has an owner and explicit security disposition before it can receive requests.

**Independent Test**: Resolve a workflow route set, inspect ownership/security metadata, and prove a successful refresh exposes one complete generation while a rejected candidate leaves the previous generation unchanged.

### User Story 2 - Reject ambiguous dynamic candidates (Priority: P1)

As a host maintainer, I receive deterministic diagnostics when dynamic routes collide with one another or with a supplied host/module manifest, including both route owners and the overlapping method.

**Independent Test**: Validate exact routes, equivalent parameter templates, overlapping method sets, and owner combinations, then verify every conflict identifies both owners and the prior route snapshot remains available.

### User Story 3 - Drain requests against their matched generation (Priority: P1)

As a workflow request, I remain bound to the route generation that matched me even when a newer generation is published, and the old generation is released only after the request completes.

**Independent Test**: Acquire a route snapshot lease, publish a replacement, verify new readers see only the replacement, and verify the old snapshot's drain task completes only after the held lease is released.

### User Story 4 - Preserve authorization and unloadability evidence (Priority: P2)

As a security and modularity maintainer, I can distinguish public, permission, host-credential, and named-policy routes and repeatedly verify that route metadata does not retain a collectible generation.

**Independent Test**: Resolve representative authorization options into exactly one typed disposition, preserve existing request authorization behavior, and run repeated metadata-only collectible-generation probes with weak-reference evidence.

## Requirements

- **FR-001**: Every published workflow route MUST carry immutable dynamic-shell ownership containing OwnerKind, OwnerId, ShellId, and non-negative Generation.
- **FR-002**: Every published workflow route MUST carry exactly one explicit security disposition; legacy route inputs MUST receive an explicit compatibility disposition before publication.
- **FR-003**: Route publication MUST build and validate a complete candidate before one atomic snapshot swap.
- **FR-004**: Validation MUST detect exact and semantically equivalent route templates, overlapping method sets, and supplied host/module/dynamic owner conflicts.
- **FR-005**: A rejected candidate MUST leave the previous generation and its route metadata available.
- **FR-006**: New requests MUST observe either the previous complete generation or the new complete generation, never an empty or partial publication.
- **FR-007**: A request that matched a generation MUST retain a lease on that exact generation through request completion and release it before the generation is disposed.
- **FR-008**: Existing `IRouteTable` implementations and authored route inputs without new metadata MUST continue to compile and retain their prior behavior through an explicit compatibility path.
- **FR-009**: Provider-specific authentication and claim mapping MUST remain outside route publication; existing policy authorization remains the enforcement path.
- **FR-010**: Repeated lifecycle evidence MUST use weak references and distinguish route metadata retention from ordinary process-memory observations.

## Key Entities

- **Dynamic route candidate**: An authored route template, method set, metadata, and diagnostic source before publication.
- **Route manifest**: A complete owner-aware set of candidate routes used for collision validation.
- **Route generation**: An immutable ordered route snapshot with a monotonically increasing generation number.
- **Generation lease**: A request-owned reference that keeps a matched generation alive until request completion.
- **Security disposition**: One public, permission, host-credential, or named-policy classification attached to a route.

## Success Criteria

- **SC-001**: All dynamic routes emitted by the workflow resolver contain ownership and security metadata before the route-table swap.
- **SC-002**: Every conflict fixture fails deterministically and names both owners, while the previous snapshot remains enumerable.
- **SC-003**: Repeated concurrent reads during replacement observe only complete snapshots and no transient empty route set.
- **SC-004**: A held old-generation lease drains successfully after replacement and before old-generation disposal is reported complete.
- **SC-005**: Existing workflow HTTP integration and unit suites remain green, including legacy route-table test doubles.
- **SC-006**: Collectible-generation evidence is repeatable and contains no strong reference to the generated route metadata after release.

## Out of Scope

- Replacing the custom workflow HTTP middleware with static Minimal API mappings.
- Re-implementing Foundation Identity policy evaluation or provider-specific authentication.
- Replacing the completed CShells host endpoint publication implementation.
- Changing public workflow HTTP route contracts or the HTTP/JSON protocol.
