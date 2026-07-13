# Implementation Plan: Durable Structured Logs Replay Cursors

## Architecture

- Keep `IStructuredLogStore` and cursor/error models in `Elsa.Diagnostics.StructuredLogs.Core`.
- Keep capture sequencing and process-local wake publication in `Elsa.Diagnostics.StructuredLogs`; the sink
  publishes a hint only after append commitment.
- Let each store adapter privately encode and validate its cursor envelope and expose bounded
  tail/read-after pages with next-cursor state; Core exposes only opaque cursor values.
- Use a replay-token field to prove that a Groundwork anchor is retained and that its opaque provider cursor,
  source, scope, and stream binding match before continuation begins.
- Adapt EF without schema changes; it is a temporary compatibility lane, not the conformance target.

## Validation

- Public in-memory store and SSE endpoint tests for cursor parsing, equal sequences, trim, two-host durable
  ordering, remote-only polling, duplicate wake hints, filtered advancement, commit-during-read, reconnect,
  and errors.
- SQLite-backed Groundwork adapter tests for two writers, equal timestamps, restart, trim-to-zero, wrong
  bindings, and acknowledgement loss.
- Structured Logs Core, EF persistence, architecture ratchets, Release solution build, and format checks.
- Up to five self-review/fix passes followed by independent fresh-context review.

## Constitution Gates

- Core remains persistence-provider neutral.
- The change adds no EF migrations or schema surface.
- Scale-bearing Groundwork reads use declared provider predicates and snapshot continuation.
- Errors crossing the HTTP seam are domain-scoped and non-disclosing.
