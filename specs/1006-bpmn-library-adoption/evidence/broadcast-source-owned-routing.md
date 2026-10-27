# Source-owned stimulus routing

Source parent `482aa79e6a26a96db28f82ed8672222a3ffa5ac9`; the [machine receipt](broadcast-source-owned-routing.json)
binds the exact source and controls. This delivers the bounded T050 routing component. **Publisher
activation, quiescent upgrade admission, actual host recovery and current Studio acceptance remain open.**
The publisher executor/staging are unchanged, the released BPMN adapter remains dormant and the legacy
checkpoint writer remains present.

The source-owned router rereads the actual native/EF outbox source, publisher and current claim using
provider time. Caller context identifies the source; even canonically equivalent caller JSON cannot
replace its persisted immutable input. A present empty winner is authoritative; data-only, replaced,
settled, expired or reclaimed sources refuse. EF reads the physical source again after publisher lookup,
requires a clean context outside caller transactions, and preserves caller tracking on refusal.

One owning `IStimulusDeliveryIntentAdapter` projects each exact intent kind. The named-event adapter
validates required/duplicate/unknown routing fields and event identity, then projects the original nested
payload, key, correlation and publisher-local target without effects. Decode failures preserve intent
identity and cause through `StimulusDeliveryIntentException`. Missing/duplicate registrations refuse.

Complete selection captures all exact eligible bookmarks before any start, retains every ordered
binding including same-artifact entries, and installs the whole cohort through the qualified provider.
Only the physically reread winner drives fan-out and retries. Delayed first delivery includes waits
created after publisher commit; retries do not discover later waits/bindings or an identically rearmed
registration. Existing artifact-scoped start keys, same-artifact shedding, resume keys, local ResumeOnly,
correlation narrowing and ordinary Test Run exclusion remain intact. Native publication-source selection
checks tenant eligibility, including an actual peer reference replacement between initial validation and
fenced cohort acceptance.

Default start/resume dispatchers qualify the actual selected source/material/execution tuple. Saved
resumes carry the exact bookmark condition from the earlier admission increment, use the physical
persisted input, and revalidate source and wait after actor lookup before enqueue. Non-shed start refusal
and unavailable storage retain owed delivery instead of reporting success. A saved cohort's complete
scope/isolation/correlation validation precedes any start; even a provider-installed malformed resume
cannot cause a valid start prefix. Recognized nested infrastructure causes remain retryable.

`AddWorkflowStimulusRouter` supplies the host-neutral composition used by the existing Runtime API
feature; no host code was copied. Original router/dispatcher constructors and ordinary external routing
remain compatible. Original source-selector default-literal calls compile. Core API accounting is
**13 additions, zero removals**. Runtime now tracks its owning public surface: an actual parent-source
compile proves **1,872 existing declarations plus four additions, zero removals**, rather than treating
the new baseline as 1,876 new APIs. No persistence schema or frozen authoring change is included.

| Verification | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Whole Workflows Runtime | 3,033 | 0 | 0 |
| Whole Runtime EF | 1,649 | 0 | 0 |
| Whole Activities Runtime | 695 | 0 | 0 |
| New cases included in those runs | 84 | 0 | 0 |

The 84 new cases are 43 native router/default-dispatcher controls, 22 native/SQLite canonical-source
reader controls, four actual EF aggregate/fresh-context/coalescing router controls and 15 owning adapter
controls. The mailbox in routing fixtures records acceptance and deduplication; it does not execute
callbacks or prove durable actor recovery. Constructed outages/ACK loss are explicit component cuts,
not observed production incidents. The whole suites include earlier exact callback/provider controls.

All 13 behavioral mutants failed assertions, with no compilation failures, and exact source was restored
before the whole runs. Discovery/TRX reconciliation, source hashes, parent API comparison, locked restore,
map checks and architecture guards are in the machine receipt. Three independent final component reviews
are clean; the requested 14 installed-cohort controls close the final acceptance gap. Full-PR review rounds
remain **0**; these component reviews do not replace the eventual bounded full-PR loop.

The frozen authoring fingerprint remains
`41aa0bb979ea46fc2533b057e3677f408ab54c3411fecfafaa238a2adae38c92`.
Existing legacy captures remain historical writer evidence. This increment proves neither upgraded-host
recovery nor Studio compatibility and closes neither #2898, #2907 nor the program. Orleans and task-runner
follow-up remain deferred.
