# Final page-reuse correctness qualification

The rebuilt clean candidate `9d6fd212da419ef1d4efd978dc91c138d7776b4e` completed the bounded PostgreSQL capture on 7 October 2026. Root and independent QA verified its retained responses, execution details, trace attribution and cleanup. The [machine-readable receipt](final-live-verification.json) pins source, build, scripts, configuration and the privately retained evidence archive. This qualifies correctness; it does not measure primary-workload query savings or latency.

## Candidate and gates

Workbench built with zero errors and 96 warnings. The build record pins 355 managed output files, including the runtime assembly; the prepare and launch scripts checked every listed hash before starting resources. Source remained clean at the named commit. The host used an isolated PostgreSQL 16.15 database, separate SQLite diagnostics, Coalesced cadence with cap 50, and durable-value page reuse enabled. Both process starts used the same configuration bundle and PostgreSQL database.

Root's complete Runtime suite passed 2,125 tests after the registration change, with 64 eligibility cases and 100% line/branch coverage for the registration class and its nested record. The later recovery-test candidate passed all 904 EF tests and 635 architecture tests, with no skips. Runtime production and Runtime-test bytes did not change between these runs. The native-required provider job passed 185 tests with no skips at `312d86713532315b7281357ea367735a7e514e4f`, which has identical production/provider-test source to the live candidate ([job](https://github.com/elsa-workflows/elsa-foundation/actions/runs/37682838995/job/113003743368)).

The final PR head must still pass its hosted checks and review, followed by resulting-main verification. Prior-head checks are not represented as those future passes. CodeRabbit reviewed the preceding head; no actual Copilot review or Greptile result is claimed.

## HTTP, REST and concurrent settlement

- The primary HttpEndpoint → deterministic SetVariable computation → WriteHttpResponse scenario returned HTTP 200 with exactly `Alice Smith`, completed and had zero incidents. The same scenario passed after restart.
- The REST control retained the direct HTTP-artifact attempt: HTTP 200 admission, then Running with one blocking scheduler-poison incident caused by null input during `firstName` evaluation. This is not the valid REST control.
- The valid companion explicitly projected `WorkflowRequest.content`, completed with `referenceText = Alice Smith`, returned HTTP 200 admission and had zero incidents. The required REST script ran without `-CompanionOnly`.
- All **60 concurrent-control attempts**, in 15 batches of at most four requests, returned HTTP 200 and their exact unique expected body. All 60 joined to distinct execution details showing Completed, zero incidents and Coalesced/50 cadence. No valid-input failure was reproduced in this capture.

Response trace headers were absent. Attribution uses each unique client request `traceparent`, the same run's retained engine-span `elsa.workflow.execution_id`, and that execution's exact detail response. Root and independent QA separately recomputed all 60 joins from SQLite; each trace had one execution identity, and all identities were distinct.

The capture waited for trace-export observation, not additional request attempts. Its three read-only mapping queries observed 44, 48 and then 60 mapped traces. Attempts 57–60 had 18 spans in capture-time metadata and 59 in the final retained database, with the same singleton execution identity. The original metadata remains unchanged. Neither trace settlement nor this passing run explains or repairs the historical 60-request failure in #2388; that issue retains its failed evidence and unresolved attribution.

## Malformed input and durable boundaries

Malformed JSON returned HTTP 202 with a started execution ID. Its retained detail was Running with one blocking `SchedulerWorkPoisoned` incident and a null `firstName` expression failure. This is an observed negative control, not a successful workflow or proof of HTTP 400 behavior. Its observation is consistent with the previously documented parser/null and endpoint fallback mechanism; this change does not alter that contract.

A workflow committed a variable and an Event bookmark. One actual host kill/restart against the same database preserved the suspended activity identity and committed state. A ResumeOnly stimulus then completed that execution with the saved output; the final detail contained exactly one completed Set, Wait and Echo activity, plus the root. A separately generated blocking incident survived unchanged. Two process starts mean **one restart**.

This live restart occurs at a committed bookmark, not during an active memo-populated drain. The separate [persisted interruption-snapshot EF test](integration-verification.md#persisted-interruption-snapshot-recovery) covers a real active-drain database snapshot, recovery in a fresh host, fresh page reads, a newer fence and rejection of the old fence. It is not an OS process-kill test or same-file reopening. Together these proofs cover their stated boundaries without substituting one for the other.

## Retention and acceptance

The original earlier capture and its matcher failure remain preserved in [the base-candidate evidence](live-verification.md). The new capture exited successfully. Its owned processes, PostgreSQL container, content root and generated credential files were removed; root and independent QA confirmed resource absence. Existing databases were not used.

Raw logs, exact details, diagnostics and private scripts are retained in `1306-live-final-9d6fd212d.tar.gz`, SHA-256 `49e521e50bd75407d196cfa5d9aeac9a617241455511ec6c0090c5f865200b9e`. Only the sanitized receipt is committed. The independently reviewed feature preserves public store/paging contracts, schemas, Immediate behavior and durability boundaries; operator guidance documents eligibility, bounds and fallback. Existing causal regressions and the reuse-disabled mutation remain linked from the integration evidence.

Spec197 T001–T017 now have source and bounded correctness evidence. PR review/checks, normal merge and resulting-main gates remain delivery requirements. Program M1/M3/M4/M5, final accounting/measurements, and historical #2388 disposition remain open. The deterministic **6 → 2** backing page SELECT result remains a SQLite EF fixture result only.
