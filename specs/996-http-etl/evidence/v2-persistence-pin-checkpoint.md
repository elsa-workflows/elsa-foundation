# V2 definition persistence and native pin checkpoint

This is partial T004 evidence for #3151, not HTTP execution acceptance. The same full-scope branch continues.
T003 still owes the complete frozen old-worker/new-consumer native resume journey. T004 still owes runtime
validation/dispatch and the later connector execution path; API/Studio authoring and HTTP delivery are not established here.

## Implemented behavior

The additive `IEtlDefinitionStoreV2` capability shares scoped definition identity, optimistic revision and
publication numbering with the original store. Explicit v2 updates can upgrade v1 drafts; v1 updates cannot
downgrade v2. Old publications remain unchanged and replayable after an upgrade. Both capabilities resolve to
one scoped EF store under normal host registration. Publishing uses the matching compiler capability and the
existing immutable artifact envelope port. Missing v2 compiler support cannot advance publication state.

Draft and publication records have an explicit format discriminator. The additive SQLite migration defaults
retained rows to format 1 and does not rewrite their JSON. This intentionally retains an upgrade path for the
authentic pre-change fixture instead of regenerating the initial migration under the usual prerelease convention.
Automated downgrade refuses removal of the discriminator: rollback must retain readers for already accepted v2
data or restore a verified v1 backup. No provider beyond the existing SQLite composition is claimed.

Catalog summaries expose format metadata without loading documents. Reads validate the row/envelope format,
scoped identity and canonical content. V2 retained drafts enforce both character and UTF-8 byte bounds before
JSON parsing; v1 serialization and content identities remain unchanged.

Native publication can pin either artifact format, and the runtime boundary verifies the corresponding canonical
representation and provenance. The metadata key remains `etl.pipeline.v1`; activity state and completion formats
remain unchanged. The existing file worker still refuses v2. These pin tests establish publication/reading only,
not connector execution or completion back into a composite.

## Review and repair

Root and bounded worker review covered persistence transitions, migration retention and native format selection.
The first focused run exposed a same-context duplicate-create failure: the inserted draft remained tracked and
prevented the next create from reaching conflict handling. The store now detaches only its successfully inserted
row. Tests also corrected two fixture mistakes: outer JSON serialization removed injected whitespace, so the
noncanonical pin case now reverses property order and verifies that it survives serialization; the retained design
fixture is published under `partition-test` and contains two publications, distinct from runtime admission scope.

The review requested normal-host resolution and retained-v1 catalog-format assertions; both are included.
Two additional oversized draft tests (ASCII and multibyte UTF-8) failed at JSON parsing before the byte preflight
was added. They pass after the repair. No failing test was removed or converted to a skip.

## Executed evidence

- Focused persistence/catalog/native-publication/retained-fixture run after initial repairs: **43 passed**, zero failures/skips.
- Full ETL suite after host/catalog/size-bound additions: **434 passed**, zero failures/skips.
  Command: `dotnet test Extensions/Etl/tests/Tests/Elsa.Etl.Tests.csproj --logger 'console;verbosity=minimal'`.
- Byte-bound test-first run: **2 failed** before repair with JSON parsing exceptions instead of the required size diagnostic.
- Removing the update format predicate made `Explicit_v1_to_v2_upgrade_preserves_old_publication_and_refuses_implicit_downgrade`
  fail precisely: expected `Conflict`, actual `Updated`. The source was restored byte-for-byte
  (SHA-256 `e34ef9a0af4db9544b65087a835dc902e49774bcbf088435716212399dd6a7e2`).
- Restored affected persistence/catalog/host/native-pin/retained-fixture tests: **47 passed**, zero failures/skips.
- Full architecture suite: **584 passed**, zero failures/skips.
- Dependency-map, solution-filter and diff checks passed. No dependency-map or filter changes were required.

Logs are retained under `/tmp/elsa-program-20261020-reprioritization/3151-*`; they are local diagnostics, not a substitute
for the committed acceptance descriptions or final exact-head CI. Final feature gates remain T020–T022.
