# Local implementation qualification

Status: stopped at the owner's request on 24 November. Qualification is incomplete; this receipt does not claim
final readiness or CI success. Start with [the other-computer handoff](../HANDOFF-2026-11-24.md).

Implementation PR: [#5460](https://github.com/elsa-workflows/elsa-foundation/pull/5460).
The initiative's S-2 implementation `855dda5dd6` was integrated in `7759707e4f`, resolving all 31 conflicts.
The subsequent initiative update `2db2d8a899` adds only the approved S-15 documents; merge `33fcf62e5e`
has no implementation-tree difference from `b6bc919e61`. S-15 acceptance is separate from this receipt.

## Completed local runs

Counts below describe separate retained runs, not one combined test invocation. Linux execution uses the owned
validation container; Windows Application Control was not disabled. Historical failed and interrupted runs remain
in the PR's chronological receipts and local evidence directories.

| Whole project or control | Source head | Result |
|---|---|---|
| Serialization | `36d1763fa0` | 218 passed, one filesystem access-control platform skip |
| Workflows Runtime | `36d1763fa0` | 5,177 passed |
| Elsa 3 Mapping | `36d1763fa0` | 1,952 passed |
| Publishing API | `36d1763fa0` | 2,115 passed |
| Runtime EF | `608402b518` | 3,042 passed |
| Activities Runtime | `c60268f111` | 1,271 passed |
| Activities Design EF | `c60268f111` | 172 passed |
| BPMN, installed current captures | `083b612cd9` | 865 passed |
| BPMN after document and loop-state raw-data repairs | `04ae1285c2` | 877 passed, no skips |
| ETL, installed current captures | `083b612cd9` | 1,376 passed |
| Constructor, null-flow and source-context controls | `36d1763fa0` | 322 passed |
| Constructor, null-flow and source-context controls after SDK-reference repair | `520f08a66a` | 327 passed |
| Independent Python fixture/verifier controls | `36d1763fa0` | 38 passed |
| TypeScript hash guard controls | `c60268f111` | 59 passed |
| TypeScript shipping hash census | `c60268f111` | 65 owners, 110 resolved references, 1,522 authenticated sources; passed |

The TypeScript source receipt changed only after proving that all 34 changed sources differed by whitespace.
Its owner, root, module, authority and golden bindings remained unchanged. Studio implementation and unit/type
evidence from earlier exact-source runs remains chronological supporting evidence.

## Causal controls and repaired failures

- The scoped polymorphism controls produced 12 failures and three passes against an authenticated old `2deda02d5c`
  Core binary. All 15 pass in the repaired whole Serialization run.
- The later shared-scope controls reproduced four Core, seven XML and four importer failures against `c60268f111`.
  The repaired whole suites include all 21 new root/member/nested/nullable/date cases. The six additional nested
  importer cases were covered by the repaired whole Mapping run, not by that four-case causal filter.
- All 14 Runtime EF failures were repaired and the complete 3,042-test project passed. Schema byte fixtures now
  distinguish absent metadata from explicit JSON null; the wait-payload helper uses its actual camelCase reader.
- All 12 Publishing failures were repaired using 14 independently reconstructed former preimages and schema-only
  replacements. The passing node-frame pins and historical refusal fixtures were preserved.
- The complete old-head constructor matrix retained 13 failures and 304 passes before the nullable-disabled and
  package-source fixes. The subsequent 322 controls include wrong-ABI, unresolved and unwitnessed dependencies.

All 19 existing VariableValueDigest vectors, canonical preimages and pins remain unchanged. Its equality/digest
semantics are preserved. A separate absent-schema vector covers the additional representation; no old verifier
or compatibility fallback was introduced.

## Current capture provenance

Clean writer `c60268f111` produced the complete current BPMN prior-cancellation and ETL families through real
executing writers. The former current families were archived before replacement; historical archives were untouched.
The BPMN producer passed its real Delay/cold-restart/claim-expiry journey. Its packager verifies all three full
SQLite cuts, authority, pins, physical custody, original debt/result identities and settlement order.
The two ETL capture tests passed; complete inventories, SQLite integrity/foreign keys and 30 canonical JSON hashes
passed independent checks, including after copying the captures to Windows. The new whole readers pass above.

## Review and remaining qualification

The owner ended implementation/review work and requested commit, push and a clear handoff. All implementation
source through `3f3f7b9cca4a77b488408b20dc8db31bbc666097` was committed and pushed before that request. Fresh
round-14 correctness and standards source reviews found no surviving findings at behavior head `04ae1285c2`;
the spec review found none at `3f3f7b9cca` after two comment-only corrections. These source reviews do not qualify
the unfinished evidence/registers or replace the affected whole suites. Portable review receipts and current
public test results are retained in [the handoff evidence directory](handoff-2026-11-24/).

The latest real failure is the HTTP parser matrix at `3f3f7b9cca`: 15 passed and one failed. A concrete `JsonValue`
request correctly refuses an object, but System.Text.Json throws `InvalidOperationException`; the new test expects
the `JsonException` family. This assertion remains unresolved. No product or test fix was made after the stop request.
Agent's 14 new controls, trigger configuration's six new controls, their causal runs and the remaining whole-suite
queue were not reached. A prepared trigger filter used a filename fragment instead of method names; it must be
corrected before that queue is reused, and no zero-test run may be credited.

Native container controls retain old 3-fail versus repaired 3-pass evidence; Switch retains 1-fail versus 1-pass;
Graph retains 7-fail versus 7-pass. BPMN document controls retain old 5-fail/1-pass versus repaired six-pass evidence,
and loop codec controls retain old six-fail versus repaired six-pass. The subsequent whole BPMN suite passes all
877 cases after both old binaries were restored. Placement retains old one-fail versus repaired one-pass at
`3f3f7b9cca`. Its earlier failed positive run was verified byte-for-byte to have used the retained `5a1fdeb558`
Publishing binary; that stale-binary attempt is preserved, rather than treated as a current-source defect.

The latest architecture shipping build was interrupted deliberately after the 877-test BPMN pass. Its current
335-case guard rerun, real-source quickstart mutation and full source scan were not started. Both Linux clones
were checked clean with temporary probes absent after stopping the owned process tree. CI was not polled or
claimed passed. The handoff records the stale C# signatures and empty golden signature explicitly.

Earlier independent correctness and spec reviews reported no surviving verified source findings at `6248553084`;
the independent standards review reported none at `36d1763fa0`. Subsequent real-corpus execution and a fresh
independent spec review found additional defects. Those earlier clean source rounds are supporting history,
not final convergence. Round 10 at `520f08a66a` was clean in the correctness and standards lenses but found
HTTP projection casing, container raw-data rewriting, graph number narrowing and placement rewriting in the spec
lens. Repairs and bounded causal controls are in progress; a further Switch child-replacement path is included.

The actual private 36-definition run at `b6bc919e61` preserved conversion at 16 but regressed publication from
11 to three and completion from eight to three. The corpus and measurement probe remained unchanged. The
empty result-type alias was traced to optionless portable-expression producers and repaired, with public synthetic
producer/Publishing controls. The first four-case Mapping run at `520f08a66a` passed one and failed three on the
test's incorrect numeric-enum assertion; the agreed existing string enum was retained and the assertion repaired.
The repaired corpus rerun at `5a1fdeb558` restored 16 converted, 11 published and eight completed, with zero
regressions and unchanged corpus/probe; a later-head rerun and affected whole suites remain pending. A separate harness preflight attempt used an
incorrect full-head argument and stopped before reading/measuring the corpus; it is not a corpus result.

Backend quickstart attempts retain an initial probe compile failure, a fixture root-header composition failure,
and a default diagnostic-snapshot/full-payload mismatch. With the production root header registration and explicit
full-payload capture for the public non-sensitive output, the real submit/save/publish/native-run/Runtime API
journey passes at `520f08a66a`, including exact output/default tokens, authored key casing and both headers.
The Git continuation initially used a payload reader for the API's authored string enums and failed; its harness
now uses the API enum vocabulary before passing the state to the real Git writer. Its rerun is pending.

The complete source-guard scan at `b6bc919e61` failed on runtime rather than SDK compiler reference selection.
The SDK-reference repair and its positive/negative controls pass above; the complete source scan rerun is active.
Historical failed receipts remain retained until the corresponding repaired rerun is recorded.

The final compiled consumer/authority/golden receipts, complete production deviation scan, private 36-definition
regression measurement, backend quickstart, remaining host/Cluster/Design/import checks and final artifact review
are still pending or running. No pending state is credited as passed.

Studio composed/visual acceptance was removed from this stream by the owner on 24 November. Its historical
failed/interrupted/not-run states remain in [the acceptance ledger](../ux-acceptance.md); no pass is claimed.
CI waits are explicitly deferred to the initiative. CI and external D-18 review/coordinator merge are not claimed here.

## Earlier checkpoint (retained history)

The following prior record keeps its original source heads and then-unresolved states. Later repairs are recorded above; the owner removed Studio acceptance from this stream.

<details>
<summary>Earlier checkpoint text</summary>

# S-9 local validation checkpoint, 24 November 2026

Current source checkpoint: `f5b3362cb2a322ea03b82025dccd8369c94e9fba`. This record remains chronological and incomplete;
it does not claim final review convergence. S-2 initiative `855dda5dd6` is integrated by `7759707e4f`, with all31 content
conflicts resolved. The owner removed composed Studio acceptance, visual inspection and visual artifact delivery from
this stream on24November; [the retained acceptance record](../ux-acceptance.md) preserves the earlier failed and not-run
states. Those historical Studio gates below are no longer delivery requirements.

The first independent review round's source repairs cover temporal nullable/container host metadata, stable numeric
refusals, extension catalogs, canonical wait-schema comparison and effective scoped typed-JSON conversion with owning
protected diagnostics. An additional audit closes raw JSON writes beyond the paired reader's existing depth bound.
Focused before/after controls retain9fail/5pass→14pass for temporal metadata and11fail/4pass plus4 numeric failures→19pass
for depth/numeric diagnostics. Typed-profile originals fail all5 new controls; their repaired run is still pending.

At integrated `6e0f57b45b`, whole Runtime5129, ActivitiesRuntime1246, RuntimeAPI742, ExecutionEvidence87,
DashboardQuery58 and BPMNInterchange293 pass; Serialization131pass/1Linux access-control skip. These supporting results
precede the latest source repairs and are not one final-head aggregate. Other whole-suite compile/fixture failures,
the interrupted import run and Docker engine500 interruptions remain retained until repaired whole reruns complete.
The all679 actual-owner hash vectors, fresh compiled register/GoldenSignature and final fresh review remain pending.

This is an incomplete implementation checkpoint, not final acceptance or review convergence. CI waits are deferred by
the coordinator. No CI pass is claimed. S-2 owns workflow null/missing/undefined policies and ValueEnvelope admission
and writes; these repairs preserve JSON tokens at the serializer boundary only.

Clean published source `87ae0a2dd58e0c4ca4b898e6671d2e88bbb0d187` passed the forced canonical locked restore and shipping
Architecture rebuild. Whole results: Serialization 128 passed/1 skipped; Runtime 5007; Publishing API 2089;
BPMN Interchange 292; Dashboard Resource 88. Architecture had 862 passed, 2 failed and 1 Windows-only skip. Its failures
were a typed golden-fixture construction error and a reviewed wire-graph difference. Both failed states are retained.

Independent source review of that graph found two real defects: nullable defaults bypassed raw writes, and form-field
configuration erased present null before the owner's existing object-only validation. The prepared repairs use raw
writes and the shared nullable-element converter, retaining the existing read/presence rules.

Recorded candidate patches based on that head produced:

| Control or affected whole suite | Result |
|---|---|
| Activity and executable default raw spelling/indentation, original writer | 4 expected assertion failures |
| Same default controls, repaired writer | 4 passed |
| Actual form-draft route, original nullable member | 1 expected assertion failure |
| Same route, repaired member; raw object and missing configuration | 1 passed |
| Runtime whole | 5009 passed |
| Activities Design whole | 815 passed |
| Runtime API whole | 739 passed |
| BPMN publication owner controls | 87 passed |
| BPMN Interchange whole | 293 passed |

The first combined default test did not compile because Runtime tests do not reference Activities Design. That failed
attempt is retained. Tests were split into their owning projects before the assertion-level negative and repaired runs.

The five authored BPMN publication vectors retain independent Python preimages and SHA-256 results for operation
identity and supplied request intent. The actor-fixture construction repair changes none of the 37 existing expected
owner digests. Its full shipping-owner rerun remains pending at this checkpoint.

Private local logs, TRX files, source patches and failed attempts are retained under `.agent-prefs/test-results/` and
the owned Linux validation helper. They do not substitute for final exact-head downloadable PR evidence. The exhaustive
hash register/guard, graph pin convergence, retained provider/fixture proof, both composed Studio hosts and independent
visual acceptance remain outstanding, followed by final conflict resolution and fresh independent local review.

</details>
