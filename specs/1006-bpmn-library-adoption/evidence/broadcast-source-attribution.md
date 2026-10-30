# T049: captured publication attribution for saved starts

This increment preserves the selected source data needed by later saved-start authority. **T049 remains
open.** Source parent: `0303e67b143bb14cc6d5be38474d9b6795dd4118`. The
[machine receipt](broadcast-source-attribution.json) binds six source/test/contract/catalog hashes.
No selection, saved-start authority or publisher protocol is enabled here.

## Model and wire

The original `StimulusStartRecipient(binding, executable)` CLR constructor remains. Its source is
uncaptured and its wire still omits source capture. The additive three-argument constructor captures
`WorkflowExecutableSourceProvenance`, including explicit null for legitimate reference-less legacy.
`SourceProvenanceCaptured` distinguishes that null from absent historical data. Uncaptured entries
cannot authorize a saved start when that path is implemented.

The versioned `SourceCapture` wrapper requires every attribution field, including nullable values.
Missing present fields and unsupported capture versions refuse instead of becoming a reference-less
start. Constructor checks enforce basic shape and any non-null selected activation/slot selectors;
the caller must still resolve an authoritative ordinary reference and prove legacy eligibility.
The constructor itself grants no authority.

Captured provenance remains separate from executable content identity. Deduplicated content can retain
its original producer definition/version/label while the selected publication has different source
attribution; neither is overwritten. Cohort equality includes capture presence and full provenance,
so captured-null cannot equal uncaptured and a changed publication cannot silently count as replay.

This is an explicit refinement of the unpublished family2 payload with publisher protocol disabled.
Historical fixtures and receipts retain their exact original bytes/source. They do not establish the
refined wire's mixed-writer, rollback or upgraded-host behavior. Final quiescence and host gates remain.

## Verification

| Control | Result |
|---|---:|
| Whole Workflow Runtime project | 2813 passed, 0 failed, 0 skipped |
| Whole Runtime EF project | 1100 passed, 0 failed, 0 skipped |
| Focused Core snapshot controls | 61 passed, including 20 new cases |
| New provider controls | 7 passed |
| Restored behavioral mutations | 7 detected; no compilation failures |
| Core API additions / removals | 3 / 0 |

The four memory/EF positive cases install complete captured/captured-null cohorts, reopen providers,
reuse the persisted winner against an uncaptured competing proposal, then renew, retry, reclaim and
complete without losing attribution. They deliberately construct source facts rather than resolving
actual publication authority. Three actual EF corrupt-content reads refuse missing capture version,
missing provenance and truncated source-reference identity through the checked physical reader.

The seven mutations invent capture for the old constructor, omit capture on wire, drop required nullable
attribution, ignore capture in equality, replace publication provenance with producer metadata, accept
an unknown capture version and omit selected-publication consistency. Each fails behaviorally; all
source was restored before the final whole-project runs.

Reproduce with the existing Workflow Runtime and Runtime EF test projects. Filter
`StimulusDeliverySnapshotTests`, `Source_capture_survives` and `Corrupt_present_source_capture` for
focused cases. Existing warnings remain. Frozen authoring SHA256 is unchanged:
`41aa0bb979ea46fc2533b057e3677f408ab54c3411fecfafaa238a2adae38c92`.

## Review and remaining work

Correctness, standards and acceptance component/receipt reviews are clean. Full-PR review remains
at zero rounds.

T049 must still extract/reuse ordinary live-source resolution, validate captured facts against selected
references, implement compact saved-cohort authority with current physical source/scope/claim expiry
checks before duplicate return, preserve artifact-scoped keys and ordinary start policy, and integrate
provider acceptance under leases plus GC/source/execution/dedup retention. Reference-less eligibility
and arbitrary retained-start refusal require their own controls.

T050 activation and T051 actual host/fresh-provider recipient/retry/restart/retention proof remain open,
as do T045, other story evidence, duplicate-semantics deletion/guards, final current host/Studio, full
review/required checks, resulting-main verification and the third ADR0063 PR. #2898/#2907 and program
production acceptance remain open. Orleans/task-runner remain deferred.
