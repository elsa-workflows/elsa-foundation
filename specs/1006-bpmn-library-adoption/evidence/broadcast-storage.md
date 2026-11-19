# T047: immutable recipient storage and provider boundary

T047 completes the finite storage/CAS increment of [FR-015](../contracts/broadcast-recipient-snapshot.md)
on the existing #3338 branch. Source parent: `bbdee782cfbd6c7d7c61083a8ced81738b3c270d`.
The [machine receipt](broadcast-storage.json) binds the implementation, tests, contract and catalog to
SHA256 hashes. This component does not enable a publisher or dispatch any recipient.

## Implemented boundary

- Both existing providers implement the scoped `IStimulusDeliveryStore` port on their existing outbox
  storage. Registration ownership includes the port; a competing provider cannot silently replace it.
- The original Core 14-parameter and EF two-parameter CLR constructors remain. The additive source
  protocol version, committed persistence scope and complete/empty snapshot are omitted when absent.
  The six additional Core API entries remove no prior API. Legacy checkpoint fingerprint code and the
  frozen authoring fingerprint remain unchanged.
- Publisher-source acceptance validates the committed scope against actual provider access before
  checkpoint participant effects. Installation revalidates exact immutable intent/source, committed
  scope, authoritative publisher facts and the current owner/fence/deadline using the provider clock.
  A null-tenant legacy publisher supplies no substitute for the retained persistence scope.
- In-memory installation holds the existing write gate/current-row lock. EF installation uses the
  existing ContentJson and Revision CAS; a lost install or renewal race reloads/revalidates the actual
  winner. Neither a losing proposal nor a stale pre-install claim object replaces that winner.
- Current content survives claim, renewal, retry, completion, shared redrive transitions, equivalence,
  checkpoint participant staging and coalescing pending conversion. A saved empty set stays selected.
  Requested cancellation passes through; contextual unavailability retains its original cause.

## Family rollout

The declared family reads `1.0.0` through its explicit upcaster to `2.0.0`. The old-row upcaster preserves
absence without inventing recipients and refuses new protocol fields hidden under a legacy stamp,
including casing variants. Checked readers refuse null/duplicated present protocol fields and a source
scope differing from its physical scoped row. The declared row rewriter uses the existing checked
read/write path and revision CAS for normal post-finalization backfill.

Ordinary writes emit the gate's observed version. At `1.0.0` all three additive fields remain absent;
a protocol-bearing source requires `2.0.0` at its original commit, before selection. Existing holds,
fleet confirmation, finalization and write interception remain authoritative. No row is downgraded,
no gate is forced forward, and a no-fleet host cannot enable the protocol beyond its seed version.
T048 must use the shared schema dormancy contract at publisher acceptance before effects.

## Verification

| Control | Result |
|---|---:|
| Whole Workflow Runtime project | 2744 passed, 0 failed |
| Whole Runtime EF project | 1082 passed, 0 failed |
| New provider / actual gate controls | 60 passed |
| New Runtime model / public coalescing fold controls | 9 passed |
| Current native preflight build / reader controls | build passed; 15 passed |
| Restored behavioral mutations | 11 detected; no compilation failures |

Provider controls cover competing complete/empty snapshots, restart reads, CAS winner reload, renewal
and takeover interleavings, same-owner higher fences, expiry without takeover, renewal beyond an old
claim object's deadline, stale-object completion, source commit isolation, original pending-scope
forgery and later context forgery, strict family/wire refusal, cancellation and unavailable causes.
The actual gate tests cover held-v1 ordinary checkpoint/claim/renew/completion, pre-write protocol
refusal, supported finalization plus refresh on the same host, actual backfill and no downgrade.

The atomic scope controls have matching-scope positive twins. Their participant fixture deliberately
passes through the unrelated executable lease boundary; it does not prove executable retention or GC.
An earlier missing lease fixture could refuse for the wrong reason. The strengthened controls succeed
with the correct scope, preserve no effects with the forged scope, and both provider admission mutations
now fail both direct-save and atomic participant controls.

The 11 restored mutations remove expiry, fence, committed-scope bindings, saved-winner reuse, CAS
winner reload, cohort copying, coalescing protocol preservation, gate-format selection, coherent-v1
refusal, and each provider's original source-scope admission. The scope-binding mutation removes both
independent model guards; EF physical-scope protection remains. All source bytes were restored before
the final whole-project runs. Existing repository warnings do not imply warning-free builds.

The frozen authoring SHA256 remains
`41aa0bb979ea46fc2533b057e3677f408ab54c3411fecfafaa238a2adae38c92`.

## Actual original reader refusal

[The portable reader receipt](broadcast-storage/original-reader.json) records four compressed whole
SQLite fixtures and their raw/compressed hashes. The fixtures are written through actual current EF
providers with constructed publisher/source state. They do not represent actual workflow executions.
The original native reader's four assembly hashes match the previously recorded
[T046 inventory](broadcast-legacy-inventory.json), source `674b9f11db79521226e8c3c0f5b6772bb69688df`.

That original binary refuses both `2.0.0` fixtures with `EfSchemaVersionSkewException`: one before
selection and one after full cohort installation. It reads both corresponding v1 controls. Controls
remove only the three additive source fields and restore the old stamp; physical identity projections
and all retained legacy data stay. Thus protection exists before the first recipient is selected.

To reproduce, use the native `DeliveryPreflight` reader built from that pinned original source and run
it against each decompressed fixture with its recorded raw SHA256. The expected exit codes/results are
in the reader receipt. Regenerate provider fixtures with `ELSA_T047_PROVIDER_WITNESS_DIR` set while
running `StimulusDeliveryStoreTests.Export_closed_provider_witnesses_for_original_reader_refusal`.
The current native reader's unsupported-family control now uses `999.0.0`; a separate positive control
verifies that current family `2.0.0` can preserve legacy absence.

## Review and remaining gates

Independent correctness, standards and acceptance component reviews are clean after correcting gate
format selection, missing committed scope evidence, coalescing pending conversion, expired selection
and original source-scope admission. Full-PR review remains at zero rounds. The actual original-reader
rerun and final whole-project results are distinct from the earlier review's pending evidence.

T048–T051 remain required: publisher schema dormancy/stamping and exact canonical durable-source
context, retained-start authority/GC, recipient execution and actual source-host/fresh-provider
retry/restart evidence. T045, other story evidence, deletion/guards, final current Studio/host proof,
full-PR review/required checks and resulting-main verification remain open. ADR0063 is still proposed;
#2898/#2907 and the program retain their own production acceptance gates. Running binary rollback
remains unsupported after new-family writes. No production quiescence or delivery claim is made here.
