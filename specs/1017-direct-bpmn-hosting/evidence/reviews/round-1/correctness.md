# PR #3410 complete-PR correctness review — round 1

Reviewed head: `7cfbf739f0d4c3c220097918b04c2b33b5cb3bb3`.
Base: `main`, scoped with `git diff origin/main...HEAD`; merge base `c986f9ddfdec2c641b2f1c1f5d8f82f8dd19e529`. The later unrelated main commit is outside the review diff. Repository/head remained unchanged.

## Actionable finding

**[P2] Reject a retry of human/timer work before resolving its service policy.**

Changed location: `Essentials/src/Activities/Bpmn/Elsa.Activities.Bpmn/Internal/BpmnDirectExecutionDriver.cs:152`.

A supported format-4 human/timer model may explicitly contain `serviceBindings: []`: both `BpmnModelDefinition` and `Graph` permit this shape, and actual publication succeeds. After starting it, submit `retry-service` with the real human invocation/attempt and an arbitrary nonexistent incident ID. `work` resolves to the human item, so this line calls `Single` on the empty service-binding inventory before the `incident-not-current`/retry-eligibility checks. It throws `InvalidOperationException` instead of retaining a rejected command receipt. The selected-model scheduler deliberately preserves driver failures, so this permanently failing FIFO input remains at the head and prevents later normal completion and cancellation from executing.

**Actual adversarial verification:** one isolated, standard production Workbench, authenticated public APIs only. Publication returned HTTP 200; start returned 201. The invalid retry returned 202/Pending and the host logged `Sequence contains no matching element` at line 152. A subsequent ordinary human completion and ordinary cancellation both returned 202/Pending. The checked physical reader retained Running/revision 1, only the original start receipt, and all three queued inputs in order. No execution, queue, receipt, claim, or clock was seeded/edited. Owned process 98649 was terminated cleanly. No build was required.

Evidence: `/tmp/elsa-2898-review-1/correctness-retry-human/` contains `reproduce.py`, accepted `definition.json`, `products.json`, `http.json`, `host.log`, before/after/final checked physical observations, `summary.json`, the private whole store and `process.json`. Driver SHA-256 `8bf6327b5c524fbaea93f02049005d2f7bf6614432c0360b6544588ec4e304d7`; actual product source `d7ee22b164bc04098bedde19e5b4722ed6f2b871`. The Workbench, driver, Runtime Core, Runtime and EF hashes exactly match the committed main-adoption receipt. `git diff d7ee22b16 HEAD -- Essentials/src` is empty, so the executing production implementation is the reviewed implementation.

Resolve the incident/current-work eligibility before looking up a service binding, or use a nonthrowing lookup and record the appropriate rejection. Cover this accepted empty-service human model and verify that invalid retry is rejected durably and a following valid completion/cancellation still progresses.

## Limits and other checks

No second actionable correctness finding survived source inspection. Traced the new model checkpoint participant and native-admission CAS, shared ownership/claim path, native result and parent consumption receipts, cancellation-before-admission retirement, held retry attempt identity, timer deadline/source validation, material closure/retention and rollback stamps. Existing broadcast selection/snapshot authority is reused rather than newly implemented in this diff. No other reviewer's output was read.

The executed diagnostic covers SQLite, the built-in single-node host and this failure scenario; it does not rerun the complete projects, provider matrix, actual upgrade/Studio journeys or clustered placement. The retained qualification bundles keep their original executing source/limits. The supplied discussion was inspected for scope, prior fixes and holds; repetitive historical evidence/CLA content was deduplicated during reading. There are no submitted reviews or review threads in the supplied cache.

The pending T042 magnitude decision and owner-deferred GitGuardian/scan gate are existing release holds, not new correctness findings. No merge clearance or resolution of #3242/#2907/the broader program is implied.
