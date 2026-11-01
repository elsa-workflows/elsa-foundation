# PR #3410 — complete-PR round 6, independent standards review

**Verdict: NOT CLEAN — one actionable P2 standards finding (S1).** No repository or GitHub changes were made. This is the sixth round under the original six-round bound; it does not restart the loop or clear any existing hold.

- Reviewed head: `d1449c0bff679212f8a64766a95b1c29cfadbcea`.
- Reviewed base: `d481d6a5e624e35bba9fbf32033ed02aff64cbef`.
- Existing branch: `agent/codex/2898-direct-bpmn-hosting`.
- Authoritative round start: <https://github.com/elsa-workflows/elsa-foundation/pull/3410#issuecomment-6536773095>.
- Scope: the entire PR, closing #2898 under #2892, rather than only the round 5 fixes. The changed-file inventory contains 7,549 paths: 117 production-domain paths, 41 migration paths, 58 test paths, 90 lockfiles, 58 e2e paths, 27 tools, 16 other paths and 7,142 evidence paths. These are inventory categories, not independent test counts.

## Actionable finding

### S1 — P2: wrap the owned native `RunBpmnState` wrapper at the checkpoint boundary

**Changed line:** `Essentials/src/Activities/Bpmn/Elsa.Activities.Bpmn/Internal/BpmnEmbeddingBoundary.cs:81`.

`Read(ActivityExecutionState)` validates the generic private-state envelope, then calls `value.Deserialize<RunBpmnState>()` directly. The contextual JSON wrappers in `BpmnEmbeddingJson`, the delivery handler and the direct model driver do not cover the native checkpoint-enricher path. `BpmnEmbeddingCheckpointEnricher.EnrichAsync` calls this reader when freezing a wait (line 23), and when cancellation reads the retained predecessor after the proposed cancellation has retired its private state (lines 57–60). `RuntimeCheckpointCommitter.CommitAsync` invokes that enricher at line 90 without a JSON boundary of its own.

**Concrete failure:** a retained native `RunBpmn` activity has an otherwise valid private-state envelope (correct invocation, `StateVersion=1`, `RunBpmnState` type/schema), but its owned inline wrapper is malformed or incompatible, such as a JSON string or a noninteger `FormatVersion`. Normal cancellation reads the committed predecessor and leaks a bare `System.Text.Json.JsonException` out of the public checkpoint committer. Its message contains a CLR JSON path but no native execution, invocation, node or checkpoint identity. This defeats the feature's contextual domain-error boundary and makes an operational cancellation failure needlessly unlocalizable. It does **not** prove lost state: the probe made zero physical persistence calls.

**Governing standard:** ratified framework constitution §2.23.5 requires infrastructure exceptions, explicitly including `JsonException`, to be wrapped in a domain exception with the original inner cause and identifiers localizing the failure. The same requirement is in the contributor review checklist. No draft/provisional provision is needed for this finding.

**Reproduction:** `embedding-json-probe/Program.cs` invokes the actual `IRuntimeCheckpointCommitEnricher` for two malformed wrapper shapes, then exercises the retained-predecessor cancellation branch through public `RuntimeCheckpointCommitter.CommitAsync`. All three observations return raw `JsonException`. The cancellation observation records `Retained predecessor reads=1; physical persistence calls=0`. See `embedding-json-probe/output.log` and `provenance.json`.

The probe runs the source9c Release assemblies; their BPMN/Runtime/Runtime.Core hashes match the frontend-adoption product receipt, and the reviewed head has the same `Essentials` tree. This is an isolated in-process boundary reproduction, **not an actual-host upgrade/recovery witness**. It constructs only inert inputs and readers, writes no stored execution/queue/receipt/authority, and launches no Workbench or Studio host.

**Correction:** catch and wrap typed wrapper decoding in `Read(ActivityExecutionState)` (or at both checkpoint feature boundaries), preserving the original `JsonException` and including the native execution and invocation/node identities. Keep the valid wrapper wire unchanged. A focused regression should exercise the retained-predecessor cancellation branch and require the contextual BPMN domain exception. No fix or mutation was applied during this read-only review.

## Discussion and authority scope

The supplied snapshot was refreshed during initial inventory. The final post-start snapshot was then frozen into `input-snapshot/`; its file hashes are recorded in `input-provenance.json`. The ID-based union of the paginated base/new comment files contains:

| Source | Issue/PR body | Unique issue comments |
|---|---:|---:|
| #2892 | 1 | 289 |
| #2898 | 1 | 97 |
| #2907 | 1 | 13 |
| #2908 | 1 | 25 |
| #3410 | 1 | 244 |
| Total | 5 | 668 |

There are zero PR inline comments, zero submitted reviews and zero review threads in the supplied snapshot. The PR metadata's older `comments=242` field predates the last two CLA bot requests; the complete comment union has 244. No comment was removed because it was an older checkpoint. Repeated CLA requests and replicated issue checkpoints were reconciled by body/ID while preserving the full raw inputs and indexes. `discussion-counts.json`, the compact index and the deduplicated index retain the scope.

Authority was read against the current local AGENTS and supplied org AGENTS, including the three independent lenses, exact-head requirement and six-round bound. Relevant owner continuations and scope amendments include comments 6249319967, 6249325156, 6250680122, 6287993256, 6299076556, 6461460223, 6480176858 and 6489393346. The direct owner compensation comment 6276421081 retains #3035's ownership of the logical external-operation key.

Both constitutions were loaded: framework 6.1.0 and Elsa 6.3.0. Framework §2.24 remains draft; Elsa §E2.8 activity versioning/Model X and §E2.9 remain draft/provisional and were not treated as newly ratified. The applicable glossary, ADR 0063 accepted adoption, ADR 0078's actor/schema boundaries, ADR 0080 D1–D4, ADR 0086's separate compensation semantics, contributor review/merge gates and spec lifecycle were applied without converting product directions into constitutional amendments.

## Standards boundaries checked

The complete changed-set review covered the public contracts and API inventories, new package/project/assembly references, feature registration and lifetime shape, protocol source/event/aggregator, default-refusing material/checkpoint/queue capabilities, immutable material and pin/closure/retention boundaries, native/model separation, state/document versions and absent-field compatibility, EF schema families/upcasters, the four-engine additive migrations and retained Design migration-history bridge, tests/tier/shard inventory, feature/catalog/root-index changes, and source-bound qualification/reproduction tooling.

The new BPMN `.Core` has no interpreter or EF dependency. Shared Runtime contracts do not import BPMN semantics. The implementation consumes the released interpreter; the direct model state does not create a native execution-header substitute. Physical writer/reader tuple checks and the queue claim-validation capability fail closed. Coalescing refuses unsupported model participation instead of silently buffering it. The named public protocol initialization event uses one source aggregator and an immutable duplicate-checked registry. Public binary API shape comparisons remain separate from version-magnitude permission.

No additional actionable standards finding was established beyond S1. This statement is advisory review output, not an overall acceptance, merge or program-completion verdict.

## Independent evidence checks performed in this review

- Four current/frozen manifests: **399 declared entries checked**, including the nested Studio entries; zero missing/escaping paths, byte/hash mismatches or declared decoded-hash/length mismatches. This is an entry count across manifests, not 399 unique assets. The baseline's **104 assets and 22 decoded whole-store hashes** pass.
- **50 distinct SQLite snapshots** from the frozen baseline, source04c qualification and source9c frontend adoption pass `PRAGMA integrity_check` on temporary copies opened with `mode=ro&immutable=1`. Original artifacts were not modified. See `sqlite-integrity-audit.json`.
- Raw compressed TRX files reproduce the eight-suite **6,928 passed / 0 failed / 0 skipped** result at executing source `04c2b53adf7b8eeda6cdf2c879f697b867fc5ad2`: BPMN 492, Runtime 3,083, Runtime EF 1,790, Architecture 594, native providers 212, standard host 41, Publishing 61 and Design 655.
- Independently parsed raw `--list-tests` logs and TRX test definitions/results have **zero missing or extra method identities for every suite**. Runtime 3,072→3,083, Architecture 585→594 and Design 585→655 are the recorded deferred-theory expansions, not omitted/skipped methods. See `independent-discovery-audit.json`.
- Git tree/object comparisons show the relevant backend/build/qualification boundaries (`Essentials`, `Extensions`, build/package props, tools/bpmn, e2e-tests/bpmn, tools/ef and the tier manifest) unchanged from both source04c and source9c to reviewed head d144. See `evidence-audit.json`.
- The focused source9c boundary probe confirms S1 as described above. Scratch build outputs were isolated outside the repository and removed after retaining source, log, generator and provenance.
- `git diff --check` over the non-evidence PR paths exits 0; the repository working tree remains clean.

These are artifact/source/contract checks. The eight whole .NET suites, live native-provider engines, actual old-fault recoveries and actual Studio journeys were **not rerun by this reviewer**. Their original receipts retain their executing sources and statuses. Source04c's full Studio proof remains two families/four new completed runs/twelve stores/13 controls and four actual old-fault cuts/eight stores. Source9c's 116 affected frontend tests and eight-store editor/export/Test Run/Inspector checks remain a frontend compatibility smoke, with no new publication/backend restart stage. The twelve recorded binary API comparisons remain shape evidence with resolved references and no suppressions; they are not version-magnitude authorization. Earlier failed probes, incomplete repair attempts, quarantines/cancellations and later passes remain distinct.

## Existing holds and remaining acceptance — not new findings

The existing owner Framework §4.2/Elsa §E5 versus shared 4.0 pre-GA ADR 0080 D4 decision card and `needs:decision`, owner-deferred GitGuardian incident 39189012, exact final-head required checks, normal protected merge and resulting-main gates remain unresolved. The supplied final-head check snapshot includes queued checks and neutral/skipped advisory jobs; it is not green final-head CI. No hold was reasked, waived or relabelled as a new implementation finding.

T041/T043 evidence and T044's prepared `Implemented` status take effect under the lifecycle transition rule at implementation merge; they do not claim shipped/program completion now. #2735 is already delivered by 3335/3338/3409. #2898/#2907/#2908/#2892 stay open/Partial. First-class direct-model Studio, wider/upgraded-host ETL, non-SQLite actual-host acceptance, #3242, Orleans and task-runner deferrals remain explicit.

The coordinating author must aggregate the independent sixth-round findings under the original bound. This reviewer performed no issue/label/claim/receipt/authority edits and makes no merge authorization.
