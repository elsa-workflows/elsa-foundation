# V2 runtime validation and terminal-envelope checkpoint

Partial #3151 implementation on the same full-scope branch. This completes T004 and advances T007/T008; it is not
HTTP execution acceptance. T005 connector positions/effects and T009 transport execution remain required.
T001/T002/T004 are checked (3/22). The complete retained old-worker/native resume proof remains owed under T003.

## Implemented behavior

The runtime has a separate v2 executable reader. It checks bounded canonical bytes, artifact provenance,
connector declarations, independent host ceilings and live-attempt capacity. It compiles the same typed
mapping/filter/lookup plan used for actual row transformation without resolving design services. Compiler
validity and admission to a particular host remain separate decisions, as with v1. HTTP policy ceilings do
not allocate memory or establish a whole-process memory bound.

Typed HTTP rows and parsed CSV rows share mapping, filtering and JSONL serialization. Optional lookup absence
does not require a fake CSV header, row or lookup capacity. V2 checks exact CLR scalar types, required fields,
lookup key types and requiredness, and compatible filter literals. Filtered rows return no output bytes.
V1 retains its CSV parser, lookup constructor rules and serialized artifact format. The small runtime JSON
Pointer reader is exercised for root, escaped property names, array indices and missing versus null values;
page decoding, pagination, duplicate-property handling and transport remain later work.

The actual admission service uses the v2 reader before any file binding resolution. Until connector storage
and workers are composed, valid v2 work receives `etl.connector-runtime-unavailable`; invalid host policy
receives the specific bounded validation diagnostic. Neither creates preparation, partition, reservation or
file-manifest work. This temporary refusal is an explicit unfinished integration point, not the feature's
target behavior.

The existing EF run and terminal outbox can now retain known canonical v2 invocation envelopes for cancellation
before admission and admission refusal. It verifies format-specific identity and static envelope/invocation
byte bounds without applying the executing host's policy to terminal retention. This permits delivery of a
host-policy refusal. Unknown formats and invalid canonical provenance are still refused; direct file-store
admission of v2 is invalid. Replay, conflicting requests, reopen and actual worker-driven result delivery are
covered. No new schema or provider capability is claimed by this change.

## Review and validation

Root review found and repaired two v2 compiler/runtime semantic mismatches: null filters on required fields
and optional lookup keys. V1 constructor behavior remains unchanged. A proposed missing output-cap check
was retracted after root inspection confirmed it was already present. The test host's page/continuation
ceilings and a missing EF namespace import were corrected before the successful run.

- Focused runtime reader, JSON Pointer, typed transformation, terminal persistence and admission tests:
  **54 passed**, zero failures/skips.
- Removing the HTTP attempt host ceiling made the real admission test fail: expected
  `etl.executable.limits-exceeded`, actual `etl.connector-runtime-unavailable`. The permissive-host case
  remained green (1 failed, 1 passed). The reader was restored byte-for-byte, SHA-256
  `dc1628b0b8f377493a9e7d32c26ac9658b6e8f0d3d0eee3db6edf0b78b6e4778`.
- Full restored ETL suite: **466 passed**, zero failures/skips, including the retained v1 fixtures.
  Command: `dotnet test Extensions/Etl/tests/Tests/Elsa.Etl.Tests.csproj --no-restore --logger 'console;verbosity=minimal'`.
- Final bounded root/worker review found no remaining concrete in-scope defect.
- Full architecture suite on the staged candidate: **584 passed**, zero failures/skips. The initial run
  passed 583 and failed the catalog link guard because this new evidence file was not yet tracked. After
  staging it, both catalog checks passed; the complete architecture rerun above also passed.
- Dependency-map, solution-filter, local-link and diff checks passed. No map/filter refresh was needed.
- Public API baselines include the additive host settings; no new runtime-to-design reference, schema
  migration, package dependency or source-breaking store interface was introduced.

The existing draft PR remains incomplete. Rebuilt Workbench/Studio journeys, HTTP execution/recovery and
final exact-head CI/resulting-main acceptance remain T005–T022; this local checkpoint does not claim them.

Logs are retained under `/tmp/elsa-program-20261020-reprioritization/3151-runtime-reader-*` and
`3151-host-ceiling-negative.log`. Final exact-head CI and full product gates remain T019–T022.
