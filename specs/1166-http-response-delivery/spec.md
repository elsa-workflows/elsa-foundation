# Feature Specification: HTTP response delivery observations

**Feature Branch**: `codex/http-response-delivery-warnings`
**Created**: 2026-10-30
**Status**: Implemented
**Input**: Retain delivery evidence and show a nonblocking warning when a completed response instruction could not be sent to its originating HTTP request.

## User Scenarios & Testing

### User Story 1 - Understand an undelivered response (Priority: P1)
An operator inspects HTTP Endpoint → Delay → Write HTTP Response. The response activity remains Completed but visibly warns when the originating exchange ended before the response was prepared. The explanation survives refresh and server restart and belongs to that exact invocation.

Acceptance: a real synchronous HTTP request returns 202 at the durable wait; after timer resumption its response activity shows a retained nonblocking warning. Execution outcomes, retries and workflow completion remain unchanged.

### User Story 2 - Distinguish delivery from uncertainty (Priority: P1)
An inline response reports Written to HTTP response without a warning or a claim that the remote client received it. Missing/legacy evidence, failed observation persistence and ambiguous request attribution remain Unknown.

Acceptance: inline, async, wait/resume, multiple requests, missing providers and duplicate/concurrent observation writes retain truthful state.

## Requirements
- FR-001: Retain invocation-specific observations independently of runtime continuation state; completion and incident policy must not change.
- FR-002: Preserve observations across later checkpoint projection writes, refresh, restart and normal retention/deletion.
- FR-003: Record an immutable originating request identity in new HTTP request payloads; never store a live request object or expose response body/header data through observations.
- FR-004: Record transport closure and successful writes as facts. Warn only when attribution to a closed originating exchange is proven. Ambiguous multiple endpoint invocations remain Unknown unless the transport recorded an exact write.
- FR-005: Cover both production and Test Run HTTP ingress, and late worker resumption. Observation capture failures must not fault or retry the activity or turn an otherwise successful HTTP response into an error.
- FR-006: Show retained warnings next to Completed on the canvas and in exact-invocation details; show Unknown for absent HTTP delivery evidence.
- FR-007: Duplicate writes are idempotent; conflicting evidence identities and concurrent projection updates cannot silently discard evidence.

## Key Entities
- Activity observation: immutable identified fact, severity, code, message, timestamp and bounded non-secret metadata.
- HTTP exchange: opaque request identity and a recorded transport outcome associated with an endpoint invocation.

## Assumptions and Scope
The original exchange is only inferred when exactly one HTTP endpoint invocation is present. Multiple/unclear origins deliberately remain Unknown. An exact transport write is authoritative for its response invocation. Written means the server wrote the response, not client receipt. No new runtime lifecycle status, activity faults, automatic retries, or warning authoring endpoint is introduced.

## Success Criteria
All normal inline responses have accurate write evidence; the reproduced wait/resume case has a warning without a fault; legacy and ambiguous runs never acquire a guessed failure. SQLite restart and concurrent projection tests retain the same invocation facts. Studio preserves Completed and visibly distinguishes warning and Unknown.
