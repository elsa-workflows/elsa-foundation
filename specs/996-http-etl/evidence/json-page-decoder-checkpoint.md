# Bounded JSON page decoder checkpoint

This is partial T007/T008 evidence for [#3151](https://github.com/elsa-workflows/elsa-foundation/issues/3151),
within the full-scope draft [#3156](https://github.com/elsa-workflows/elsa-foundation/pull/3156).
It does not establish HTTP execution, durable pagination, retry, native continuation or Studio acceptance.
T007 and T008 remain unchecked.

## Behavior

`EtlJsonPageReader` reads one bounded UTF-8 page through the existing RFC 6901 selector. It checks page bytes
before parsing, enforces depth and record-count limits, rejects duplicate properties throughout the document,
and validates Unicode even in unselected metadata. Each selected record retains its raw byte count and item
index, with either a typed row or a safe row diagnostic. Record bytes, declared selectors and continuation
bytes are bounded. Missing/null optional fields become typed null; required values fail the row. Scalars are
not coerced from strings or structures. Decimal conversion must preserve the numeric value exactly.

Single-page, opaque-token and next-link declarations select a continuation without resolving or sending it.
An empty page with a continuation remains unfinished. Item indices are not standalone durable identities:
the eventual store must bind them to the retained shard/request/page. Page count and visited-request checks
remain part of T005/T007/T009 integration.

The decoder-to-transform tests exercise mapping of strings containing commas, quotes and newlines; real CSV
lookup parsing joined to JSON typed rows; and a nonempty filtered page producing no output while retaining
its continuation. This proves local composition only, not atomic zero-effect batch acceptance.

## Review and verification

Root review reproduced six Unicode failures in the initial implementation: lazy string decoding either
escaped as a raw decoder exception or failed to reject malformed unselected metadata. Validation now visits
all JSON strings and reports only a stable page code, without preserving the raw decoder exception.
Additional coverage preserves valid surrogate pairs, decoded property-name duplicate detection, UTF-8 byte
limits and the final representable item index. Decimal exactness was identified during bounded worker/root
review and added before integration.

A final contract pass also found an unsupported object-only record assumption. The v2 compiler and JSON
Pointer contract permit root-scalar and array-index selection. New tests reproduced **2 failures, 1 pass**
before removing that shortcut. Field pointers, requiredness and scalar types now decide validity for every
record shape. This later correction intentionally changes the restored reader hash recorded below; the full
ETL suite is rerun afterward.

- Focused decoder and transform composition: **51 passed**, zero failures/skips.
  `dotnet test Extensions/Etl/tests/Tests/Elsa.Etl.Tests.csproj --no-restore --filter 'FullyQualifiedName~EtlJsonPageReaderTests|FullyQualifiedName~EtlJsonTransformTests' --logger 'console;verbosity=minimal'`.
- Full ETL project after restoring exact conversion and correcting record-shape selection: **520 passed**, zero failures/skips.
  `dotnet test Extensions/Etl/tests/Tests/Elsa.Etl.Tests.csproj --no-restore --logger 'console;verbosity=minimal'`.
- Negative control: replacing exact decimal conversion with `JsonElement.TryGetDecimal` made the five
  underflow/rounding/range tests fail **4**, pass **1**. The reader was restored byte-for-byte before the full
  suite; SHA-256 `c07bf7ec1245682d699569f1254dbec7549c3d97f8711374abd1514f724000f5`.
- Root and bounded worker review found no remaining concrete decoder defect. The root fixed one build error
  in the delegated change (`JsonMarshal.GetRawUtf8Value` already returns a span), then reran validation.
  The final review assessed the actual `UInt128` implementation and evidence boundaries.
- Architecture **584 passed**, zero failures/skips, before the final object-shape shortcut removal. That later
  correction changed no project references, public API or registration; the final ETL run above covers it.
  Current map/filter/link/diff results are recorded in the PR checkpoint receipt after running those checks.
  No rebuilt-server, browser or CI pass is claimed here: this decoder is not yet wired into HTTP execution.

## Remaining scope

The optional connector-store contract remains a design draft, outside compiled source until its transitions
and provider tests are implemented together. T005 must retain v1 provider behavior, type file/page positions
separately, and share run accounting/terminal delivery without letting a legacy preparation or settlement
path handle connector work. T003 retained old-worker resume and all HTTP/recovery/native/API/Studio work
remain owed. No task-runner, Orleans or Proto.Actor implementation is included.
