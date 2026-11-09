# HTTP connector live acceptance checkpoint

Issue [#3151](https://github.com/elsa-workflows/elsa-foundation/issues/3151), draft PR
[#3156](https://github.com/elsa-workflows/elsa-foundation/pull/3156). T019 is complete; **18/22 tasks** are complete.
The HTTP leaf is not ready to merge.

## Real hosts and ownership

The root rebuilt Workbench, all Studio client assets and the Studio host. The acceptance runners
start owned processes on free loopback ports with private content roots, SQLite databases,
input/output paths and shell settings. Studio authentication remains enabled. HTTP input and output
use an owned Node fixture with explicit host-approved paths and aliases. No external endpoints,
shared host configuration, workflow permissions or release settings are changed.

The HTTP fixture has two shards, two one-record token pages per shard and a finite request/body
observation cap. The first shard-B request returns `429 Retry-After: 3`; a separate cancellation
source always returns `429 Retry-After: 30`. The receiver accepts JSON arrays and records whether
the idempotency header is nonempty. Receiver observations do not claim exactly-once effects.

## Acceptance coverage

`Test-FiniteHttpEtl.ps1` creates, validates and publishes HTTP-to-HTTP, HTTP-to-JSONL and CSV-to-HTTP
v2 definitions through the actual Workbench APIs, then invokes each immutable selection from both
Sequence and Flowchart. Each parent must complete `RunEtl` and its following activity. The HTTP
source must exhaust both continuation pages; output checks preserve exact IDs and mapped names.
CSV row rejection takes the explicit business-error continuation and produces the safe error record.

The retry proof observes persisted waiting state while the sibling shard progresses, then checks
that monotonic fixture timestamps put the next attempt after the requested delay. Cancellation
must persist an ETL terminal result, cancel the surrounding Flowchart without executing its successor,
and leave exactly one physical request after the former retry deadline plus two seconds while
Workbench remains running. Recorded claim metadata may remain after terminal settlement; it is
not treated as outstanding work.

`Test-FiniteFileEtlStudio.ps1 -Http` selects exactly two actual-browser tests with no retries or
skips: connector authoring/publication/selection and successful native Sequence continuation;
then visible HTTP 429 retry state, Studio cancellation and native cancellation without its successor.
The inspector checks unknown HTTP totals, accepted route-body counts and both exhausted page positions.
The existing file REST and Studio journeys remain separate regression checks.

## Defects discovered by live acceptance

The first REST request exposed a valid v2 property-order mismatch: the canonical codec accepts
`kind` after other fields, while the API's independent generated binding returned HTTP 500.
The first real Studio run exposed omitted optional budgets being read as zero by that generated
binding. A newly created default draft therefore failed compiler validation.

The design API now delegates only `EtlPipelineV2` values to the existing bounded canonical codec.
This aligns input ordering, optional defaults, strict structure and output bytes without adding a
second set of parsing rules. The v1 pipeline type is unchanged. Tests independently cover normal
and late discriminator positions in create/update, omitted defaults, canonical round-trip and
validate/publish, explicit zero validation diagnostics and unknown-member refusal.

Removing the converter reproduced both original defects in the two positive regression cases;
the source was restored byte-for-byte. Harness corrections also covered PowerShell timestamp
conversion, empty initial output lists, the distinction between retained claims and owed work,
exact mapped-name assertions and nonempty idempotency headers. Failed attempts are not counted as
acceptance passes.

## Verification

The repaired Workbench passed all six REST combinations and delayed cancellation. The observed retry
was 3,170 ms between the fixture observations of its 429 and the next attempt; each HTTP output journey observed no duplicates in this run,
which does not strengthen the at-least-once contract. The final Studio run passed both HTTP journeys
in 39.9 seconds, followed by the file Studio journey in 18.4 seconds. The runners verify exact test
counts and zero skipped, flaky or unexpected results. Root inspected successful ETL/native completion,
native cancellation and the actual exhausted HTTP partition table screenshots.

| Gate | Result |
|---|---|
| Workbench, Studio assets and Studio host builds | Passed; Workbench rebuilt again after the API repair |
| Full ETL suite on repaired source | 996 passed, 0 failed, 0 skipped |
| Focused design/publication API suite | 25 passed before regression control |
| Codec-removal regression control | Both order/default cases failed; source restored, full ETL passed |
| Actual HTTP REST journey | Six connector/composite cases and delayed cancellation passed |
| Existing file REST journey after helper extraction | Sequence and Flowchart passed |
| Actual HTTP Studio journeys on final test source | 2 passed, 0 failed/skipped/flaky |
| Existing file Studio journey against repaired Workbench | 1 passed, 0 failed/skipped/flaky |
| Node and PowerShell syntax | Passed |
| Architecture | 584 passed, 0 failed, 0 skipped |
| Generated maps, solution filters, relative links and diff | Passed; no map refresh required |

Commands: `dotnet build Essentials/apps/Elsa.Workbench/Elsa.Workbench.csproj -p:RestoreLockedMode=true`,
`pnpm --dir Studio build`, `dotnet build Studio/src/apps/Elsa.Studio.Web/Elsa.Studio.Web.csproj -p:RestoreLockedMode=true`,
`dotnet test Extensions/Etl/tests/Tests/Elsa.Etl.Tests.csproj --no-restore`,
`pwsh -NoProfile -File e2e-tests/etl/Test-FiniteHttpEtl.ps1`,
`pwsh -NoProfile -File e2e-tests/etl/Test-FiniteFileEtl.ps1`, and
`pwsh -NoProfile -File e2e-tests/etl/Test-FiniteFileEtlStudio.ps1` with and without `-Http`.

The API fix and final acceptance harness received bounded independent review and root integration review.
No remaining actionable finding was identified. The only post-suite test change adds a screenshot of the
partition table; both browser journeys passed again afterward. The final HTTP browser fixture root is
`elsa-finite-file-etl-9472dfa57b8b4716ab6bddc6ffa9bcad`; the final file browser root is
`elsa-finite-file-etl-ee13eaf340024a4998884c1b1e890f6c`. Both remain under the local temporary directory
with screenshots, reports and private host state. Those private databases/authentication traces are not
published to the repository. The full program, frozen predecessor resume (T003), integration with newer
main and T020–T022 remain open. Historical timeout causes remain unproved; external effects remain
at least once.
