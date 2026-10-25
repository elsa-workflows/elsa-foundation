# Integrated review remediation

Round 1 reviewed `6557ace50173b491d3a8ffde1ec61de752d7ff36` against main `e220081f7e0ef459b8b520564529d214a71ce76c`. Independent correctness review found no actionable defect. Standards and acceptance reviews identified API tracking/catalog omissions, outdated spec input wording, and provider evidence predating the stored-invocation delivery-validator change. The live issue readiness paragraph was already updated; the reviewer had an older snapshot.

## API and documentation

`Elsa.Workflows.Runtime.Core` now opts into the existing PublicApiAnalyzers integration, with its private analyzer dependency in the lock file and both baseline files. Compiler SARIF supplied 8,867 unique API entries, including the new unavailable exception, retained scheduler work, deferral statuses/result and retry delay. Elsa 4 is an unreleased preview, so this follows existing Tasks.Core and Rules.Core convention: shipped contains only the nullable directive and the current surface is unshipped. This baseline does not change public visibility or runtime behavior.

The initial build with empty tracking files failed on missing API declarations as expected; after populating the compiler-derived baseline, the focused Core build passed. The runtime handler catalog now documents the supported durable bookmark-resume deferral boundary and propagation without acknowledgement/poisoning for other paths. Spec input wording points to current task status instead of describing completed implementation as pending.

## Validation

The [current-source four-provider matrix](provider-review-qualification.json) passed all 8 cases, zero failures/skips, with a stable working-tree fingerprint. The full architecture suite passed 585/585 with zero failures/skips after API tracking was enabled. The generated solution-filter check also passed. [Raw artifact hashes and counters](integrated-review-remediation.json) identify these runs. Earlier package/native/process/upgrade/compatibility evidence retains its original source and scope; these changes add analyzer tracking and documentation, not a new product implementation.

## Secret-scanner findings

GitGuardian incidents 38834294, 38834295, 38834296, 38834297, 38834298 and 38844107 identify `tokenOwner` in the process-boundary and retained-upgrade evidence. Root and an independent reviewer traced these fields to `RulesBackgroundWorker` generating a worker ID with `Guid.NewGuid()`, `RuleExecution` passing it as the claim owner, and `EfRuleRunStore.Leases` storing that owner. `ProcessProbe` copies the lease owner to its marker, which the process and upgrade harnesses retain. These are opaque worker identities for lease attribution, not authentication credentials. Database credentials are generated separately and passed through child-process environment variables; these six flagged fields are not derived from them.

The exact evidence remains intact. No scanner configuration, detector, workflow, branch protection or git history was changed. Individual false-positive dispositions and the resulting check status are recorded in the PR; this document does not claim that an external check passed before verification.

Final review, exact-head hosted checks, merge and resulting-main verification remain separate pending gates.
