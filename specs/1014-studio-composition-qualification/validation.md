# Validation evidence

## Planning checkpoint

Only task3364 is active, exclusive claim6442524047, after3363 delivered on verified main26f0893468320500d56fa26dc3fcd42db63a36ba. All prerequisite native issues are completed, no covering open PR/assignee/competing claim/main-red issue exists, and no applicable freeze covers these paths. Spec1014 was reserved after current/archive plus all19open-PR spec trees (highest1013). The mandatory branch hook ran once with the Issue Bus Lite branch name; optional commit/context behavior is recorded in plan.md.

Specification quality checklist is complete. Existing contracts and both constitutions were checked; no unresolved architecture question or extra public surface is assumed. This checkpoint is planning, not runtime qualification. The filter ownership finding is still source-traced until an actual admitted-store reproduction runs. Implementation/whole suites/review/CI/main outcomes remain pending.

## Admitted response ownership: reproduced and corrected

The new public route-group/filter regression uses AdministrationHost's real enrolled native store and a genuine reconciled asset. It first proves an ordinary200/body, then replaces four successful handler results with204 (error/refusal results cannot qualify), and requests an ordinary response again. Original product fails1/1 at that final request: TaskCanceledException with inner SemaphoreSlim.WaitUntilCountOrTimeoutAsync -> PackageContentReader.TryAcquireResponseAsync line31, after all four replacements completed. Before log/TRX: /tmp/studio3364-filter-before.log and studio3364-filter-before.trx. Load was about7-8 on8cores; the causal four-permit sequence and exact semaphore stack identify the leak, rather than a generic slow HTTP operation.

Correction registers the existing idempotent lease with HttpResponse.RegisterForDisposeAsync before handing the payload result to host filters. Existing result/error finally cleanup retains early release; no public API, capacity or native protocol changes. The same new regression and all6existing PackageAsset cases pass7/7,0fail/skip in studio3364-filter-after.trx (/tmp/studio3364-filter-after.log). The private test host gains only an optional endpoint mapping callback, preserving all existing default mappings and avoiding ambiguous duplicate routes. Whole ordinary Release Studio suite passes636/636,0fail/skip in1m36 (studio3364-lease-whole.trx and /tmp/studio3364-lease-whole.log). Discovery lists629 entries, matching629TRX definitions/636runtime results. Architecture/maps remain final affected checks.
