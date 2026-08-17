# Wave 4 Agent API Research

## Decision: use explicit Minimal API mappings

The existing Agent API has eleven concrete FastEndpoints registrations. A real before host was
captured before deleting the adapters. The migration uses ordinary ASP.NET Core endpoint builders
and request delegates, preserving route names and metadata. A FastEndpoints canary remains in the
same test host to prove coexistence.

## Decision: keep one Foundation Identity evaluator

Agent routes carry standard policy metadata for three owner-contributed actions. The evaluator
continues to provide exact, implication, and wildcard grants. Resource and tenant checks remain
fail-closed in the existing Agent authorization services. Endpoint metadata names only the
catalog-owned action; wildcard is not copied into route metadata.

## Decision: preserve the consumed SSE contract

The before fixture establishes `text/event-stream`, no-cache/anti-buffering headers, two newline-
terminated data frames, and cancellation behavior. It contains no heartbeat, resume token, or
separate backpressure protocol. This migration therefore preserves framing and cleanup and records
heartbeat/resume as a separate future contract decision.

## Decision: source-generated JSON is owner-local

The Agent API response and SSE contexts are module-owned. Request delegates pass generated type
metadata to JSON results so reflection metadata does not become a process-global retention path for
collectible owner assemblies.

## Alternatives rejected

- Repository-wide FastEndpoints removal: outside the exact eleven-registration scope.
- A new Elsa endpoint abstraction: would recreate framework coupling and obscure standard endpoint
  metadata.
- New SSE heartbeat/resume behavior in this migration: would change a public wire contract without
  a reviewed baseline or client compatibility decision.
