# T049: saved-start admission through the ordinary dispatcher

**T049 remains open; publisher activation remains disabled.** Source parent:
`913b7450fe670a83e75ddb34550edcb6e06adae1`. The [machine receipt](broadcast-saved-admission.json)
binds 14 implementation, contract, test and handoff files. This increment connects the verified cohort
to ordinary start admission; it does not qualify actual host/Studio recovery or close the CI merge gates.

The additive dispatcher constructor composes `IStimulusDeliveryStore`. Original constructors retain their
saved-mode refusal without that port. Before even keyed duplicate lookup, saved admission requires the
physical current source/claim, acceptance witness version 1, captured entry, exact binding node/metadata,
original per-artifact start key/execution ID and ordinary published-root context. It accepts no child,
variables, inputs, test authority or root-authority override. Explicit legacy publisher tenant/partition
absence remains intact; the normal published-root/default-partition behavior still applies.

New admission compares the actual complete material pin and uses captured provenance, including legitimate
reference-less null, without live-reference recapture. Ordinary start policy, dispatch options and shedding
remain active. A duplicate must match the complete accepted pin and root scope/authority, while preserving
the first binding's actual source attribution. Several same-artifact bindings retain one original key;
a shed first binding consumes no key and the next can still accept. Source authority is rechecked after
awaited policy and duplicate lookup, and after actor lookup immediately before enqueue.

The ordinary start-command wire is unchanged. A round-trip check refuses malformed internal UTF-16 that
would lose exact saved binding/provenance/context through the ordinary serializer, before actor lookup.
The first positive control exposed this loss using the earlier storage fixture's unpaired-surrogate
metadata. Normal-metadata positive twins and deliberate malformed-metadata refusal now cover both providers;
the failure does not rewrite or abandon the persisted source/cohort. Public interface XML and catalogs
name saved-source, stale-claim, policy/authority, material and cancellation failure semantics.

## Verification

| Restored control | Result |
|---|---:|
| Whole Workflow Runtime | 2875 passed, 0 failed, 0 skipped |
| Whole Runtime EF | 1317 passed, 0 failed, 0 skipped |
| New native/EF admission cases | 99 |
| Focused admission and related controls | 130 passed |
| Behavioral mutations | 14 detected, no compilation errors |
| Core API declarations added / removed | 0 / 0 |

Runtime discovery reconciles 2864 entries − 3 unchanged deferred theories + 14 cases = 2875. EF lists and
executes 1317 cases. Every TRX result maps to discovery. Four Runtime and two EF truncated display-name
collisions are reported separately without subtracting them from discovery. Existing warnings remain.
Frozen authoring SHA256 remains `41aa0bb979ea46fc2533b057e3677f408ab54c3411fecfafaa238a2adae38c92`.

Controls use real native/EF source installation, physical lookup, retirement, claim takeover and state stores,
then recording actors at the dispatch boundary. They cover exact source tampering and unmarked winners before
duplicates, old constructors, full material and duplicate pins, root-scope/authority conflicts, retired-source
and reference-less positive twins, ordinary policy, options, same-artifact keys/shedding, and takeover during
policy/duplicate/actor lookup. An EF writer's existing tenant guard makes a foreign-tenant existing-row case
inapplicable there; native memory covers that case, and both providers reject foreign request scope.

One queued-handler control captures the admitted command, records actual source acknowledgement, deletes the
publisher, proves another live source read cannot authorize it, and runs the production start handler to
schedule its WorkflowStarted checkpoint using retained material. The handler uses an in-memory queue and
records no actual host completion: this is finite handoff evidence, not fresh durable mailbox/process recovery.

Review found a missing complete-pin comparison on saved duplicates; eight actual provider cases and the
corresponding mutation now detect it without replacing actual winning source attribution. Documentation
review corrected the formerly unconditional live-publication promise. Fourteen mutations remove witness,
original key, entry node/metadata, request-root context, duplicate pin/root context, material pin, captured
provenance, ordinary policy, post-policy/actor/duplicate source checks, wire fidelity or actual duplicate
attribution. Every mutant fails behaviorally, and source was restored before whole-project runs.

Correctness, standards and acceptance final source and receipt reviews are clean.
Full-PR rounds remain **0**.

## Remaining gates

T049 final GC composition remains open. T050 must derive stimulus input from the immutable source intent in
its owning adapter, select all exact waits before starts, atomically validate bookmark generation at recipient
acceptance, and enforce publisher/schema dormancy before activation. The authority grants saved publication
selection only; these controls do not prove immutable input projection. T051 actual host/fresh-provider
recovery, T045/other stories, duplicate-semantics deletion/guards, final current host/Studio, full PR review,
exact-head required checks, resulting-main verification and third ADR0063 PR remain open.

CI run 40369434902 on the previous pushed head failed its configured retries for the Core implementation-shape
guard (`RuntimeChildControlContext`), DispatchWorkflow behavioral output/outcome drives, schema-readability
expectations for the evolved outbox family, and the runtime export/import probe's suspended waited parent.
These are branch-owned merge blockers to repair next; local Runtime/EF passes do not override them. The existing
GitGuardian incident 38996611 flags a public source SHA256 in an older receipt and is unresolved. No check or
protection is bypassed. #2898/#2907 and program production acceptance remain open; Orleans/task-runner are deferred.
