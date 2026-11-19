# Native Test Run wrapper/source handoff

Source parent `de716aebf` (full commit in the [machine receipt](broadcast-draft-wrapper-handoff.json)).
**This increment qualifies the native service handoff. Actual EF service recovery, old-writer exclusion and
qualified default cohort GC keep T049 open. Saved broadcast publication stays disabled. No workflow-start
effect, upgraded-host or Studio recovery is established by these native service probes.**

Activity draft Test Run preparation preflights both selected template and executable-source root capabilities
before template writes. It saves immutable wrapper material, reads the actual persisted winner and compares its
full payload, including provenance and resume targets. Artifact ID/hash must match; first-writer producer
attribution and creation time may differ. The winning full physical pin enters the selected renewable root-write
coordinator. Each invocation receives a distinct lease ID, while source identity and expiry remain receipt-stable.
The source provider validates current complete-closure grants atomically and returns its persisted source winner.
Already-prepared receipts continue through saved identities without rebuilding.

The original public 20-argument CLR constructor remains. Its direct callers use a built-in coordinator with
default lease options and their selected material store/clock. The additive constructor receives the host's
selected coordinator through DI. Infrastructure and contextual provider/coordination failures at the new wrapper
boundary expose public Publishing Core ActivityDraftTestRunStorageException with TestRunId and the exact
original inner exception, including aggregate vectors. Requested cancellation propagates unchanged; unsupported
capability and corrupt-wrapper refusals retain their distinct behavior.

Review initially questioned second-run provenance because node metadata is outside the hash. Tracing the actual
placer/hasher showed that sourceReferenceId is also stamped into descriptor payload, which the existing hasher
includes. Independent runs already get distinct wrappers. No hash or placement-identity algorithm was changed.
The three existing new-run controls now verify actual stored metadata/descriptor provenance and distinct artifact
IDs. Review corrected the initial raw-infrastructure boundary, moved its exception to the public Publishing Core
contract and added existing provider/aggregate wrapping. No full-PR review round is consumed.

Final restored whole-project results:

| Check | Passed | Failed / skipped |
|---|---:|---:|
| Runtime | 2,971 | 0 / 0 |
| Runtime EF | 1,505 | 0 / 0 |
| Publishing EF | 171 | 0 / 0 |
| Publishing API | 891 | 0 / 0 |
| Publishing | 61 | 0 / 0 |

Sixteen new native service cases, plus strengthened existing second-run cases, cover before/after-write failure
and same-receipt retry, fixed expiry and one immutable source winner, actual first-writer producer pin, conflicting
stored metadata/hash/incident policy, original constructor ABI, unsupported legacy coordinator, a genuine legacy
source-contract facade with coherent template adapter, requested/unrequested cancellation and catchable raw or
already-contextualized infrastructure failures. A real asynchronous two-preparation barrier holds actual native
grants in both invocations: after the first completes and releases, the second still prevents an actual deletion
guard. A recording dispatcher deliberately loses acknowledgement without executing a workflow. Constructed
material-conflict rows and injected coordinator faults are not legacy execution checkpoints or EF transaction
acknowledgement-loss proof.

All eight valid behavioral mutations are caught with exact source restoration, including shared receipt-wide
lease identity, proposed producer pin, skipped winner validation, lost domain context, removed source preflight
and unfenced source save. No compile-error mutation is counted. The machine receipt reconciles discovery/TRX,
records generated-filter and API-tracking guards, locked restores, the Core/API Release build and the unchanged frozen authoring fingerprint
`41aa0bb979ea46fc2533b057e3677f408ab54c3411fecfafaa238a2adae38c92`.

Publishing Core and API now opt into their own PublicApiAnalyzers/ledgers. Baseline builds verify 1,210 pre-existing
Core declarations and 1,845 pre-existing API declarations against parent source, with the new exception or
constructor excluded, then restore the exact candidate bytes. Only three Core symbols (exception, constructor,
TestRunId getter) and one API constructor are additions; zero removals. Both baseline counts include existing
surface, not newly introduced API. The two existing generated endpoint declarations are included in the API
baseline. Runtime Core's ledger and the Runtime schema are unchanged.

Final component source/catalog/receipt reviews remain separate from full-PR review rounds, still zero. New head
requires exact-head checks; draft Full tier skips are not qualification. GitGuardian's older public-source-digest
finding remains unresolved; no check or protection is bypassed.

Next: prove actual Test Run service recovery through fresh EF contexts and committed-transaction acknowledgement
loss, then finish old-writer/quiescence and qualified default cohort GC gates. T050/T051, other story gates,
duplicate-semantics deletion/guards, final current host/Studio, full-PR review/checks/resulting-main and ADR0063
remain open. #2898/#2907/program production acceptance stays open; Orleans/task-runner remain deferred. No legacy
execution-checkpoint writer is deleted here.
