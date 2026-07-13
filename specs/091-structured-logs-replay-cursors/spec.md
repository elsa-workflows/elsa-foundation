# Feature Specification: Durable Structured Logs Replay Cursors

**Tracking**: [Elsa issue #635](https://github.com/elsa-workflows/elsa-foundation/issues/635)
**Parent**: [Zero-EF Persistence #629](https://github.com/elsa-workflows/elsa-foundation/issues/629)
**Status**: Ratified and verified (2026-07-12).

## Goal

Make Structured Logs SSE replay safe when multiple host processes append to the same tenant, host
storage scope, and logical stream. Process-local `Sequence` remains display metadata; committed store
cursors are the only replay, order, and handoff identity.

## Functional Requirements

- **FR-001**: `StructuredLogReplayCursor` is a bounded, single-line, versioned opaque value that can
  round-trip unchanged through SSE `id` and `Last-Event-ID`.
- **FR-002**: A cursor is source-qualified and bound to tenant, host storage scope, and stream without
  exposing those binding values in cursor errors.
- **FR-003**: `IStructuredLogStore.AppendAsync` returns the committed entry carrying its authoritative
  cursor. Live publication happens only after that operation completes.
- **FR-004**: `GetTailCursorAsync` captures the newest retained committed boundary and bounded
  `ReadAfterAsync` pages validate an optional anchor, scan oldest-first from one provider snapshot, and
  return a next cursor. Provider continuation and cursor values remain inside the adapter.
- **FR-005**: Malformed, unsupported-version, tampered, expired, trimmed, wrong-scope, wrong-stream, and
  wrong-source cursors produce the same `StructuredLogReplayCursorUnavailableException`; HTTP maps it to
  one `409 Conflict` message without infrastructure or binding details.
- **FR-006**: The stream endpoint sends only entries returned by durable read-after pages. The process-local
  feed is a wake hint and never an authoritative payload source; bounded polling discovers remote commits.
  No logic may order or de-duplicate by `Sequence`.
- **FR-007**: `GetHighWaterMarkAsync` returns the lifetime maximum committed logical sequence, zero only for
  a never-written stream. `TrimAsync(0)` and restart preserve it.
- **FR-008**: Concurrent writers may commit equal timestamps and equal display sequences. Provider cursor
  order remains total and authoritative.
- **FR-009**: The Groundwork adapter uses `IDiagnosticRecordStore`, durable idempotent batch operation ids,
  bounded provider queries, snapshot continuation, exact trim, and inspection metadata. Core references no
  Groundwork assembly.
- **FR-010**: The temporary EF adapter compiles against the new seam without new migrations, columns, or
  registrations. No new EF functionality is introduced.

## Observable Scenarios

1. Two writers commit equal timestamp/sequence entries; reconnect from the first cursor returns the second.
2. A commit acknowledgement is lost; retry uses the same operation id/cursor and live publication occurs once.
3. A read-after page is in flight while local and remote writers commit; subsequent pages deliver each
   committed cursor once in provider order, including when wake hints are missing or duplicated.
4. A store restarts; an old retained cursor resumes and lifetime logical high-water remains monotonic.
5. Trim-to-zero followed by restart yields no records, preserves high-water, and rejects the trimmed cursor.
6. A filtered tail advances its internal cursor across excluded records and later delivers matching records.
7. Wrong-scope and wrong-stream cursors receive the same non-disclosing response.

## Out of Scope

- Subscriber queue sizing/fan-out policy and SSE writer cleanup tracked by #420.
- Groundwork provider cursor allocation or provider implementation.
- Removing the temporary EF implementation family; #629 owns the later deletion gate.
