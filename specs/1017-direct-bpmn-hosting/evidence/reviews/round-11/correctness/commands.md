# Round11 exact commands

Working directory: /Users/sipke/.codex/worktrees/f236/elsa-foundation.

```sh
git rev-parse HEAD origin/main
git status --short
git diff --name-status 0870a6740 f83df1b56
git diff --exit-code 6bd8ac3e8 f83df1b56 -- Essentials/src Essentials/tests Studio/src Studio/tests tools/bpmn
git diff --check 0870a6740 f83df1b56 -- Essentials/src Studio/src
/Users/sipke/.local/bin/gh-codex api repos/elsa-workflows/elsa-foundation/pulls/3410 --jq '{head: .head.sha, base: .base.sha, draft: .draft, state: .state}'

dotnet test Essentials/tests/Activities/Bpmn/Tests/Elsa.Activities.Bpmn.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~An_original_queued_cancelled_result_recovers_without_waiting_for_its_obsolete_native_retry|FullyQualifiedName~Native_cancellation_settles_before_an_obsolete_compensation_retry_becomes_due|FullyQualifiedName~An_old_queued_native_result' --results-directory /tmp/elsa-2898-review11-correctness/results --logger 'trx;LogFileName=focused-bpmn.trx'

dotnet test Essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~Durable_responsibility' --results-directory /tmp/elsa-2898-review11-correctness/results --logger 'trx;LogFileName=focused-ef.trx'

dotnet test Essentials/tests/Activities/Bpmn/Tests/Elsa.Activities.Bpmn.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~An_already_queued_native_result_preserves_cancellation_owed_from_an_earlier_scope_checkpoint|FullyQualifiedName~An_independently_cancelled_native_worker_cannot_settle_while_its_actual_downstream_cancel_is_pending|FullyQualifiedName~Settled_models_release_mailboxes_and_reactivate_for_late_inputs_and_exact_replay|FullyQualifiedName~Published_source_commands_keep_exact_replay_human_attempts_and_tenant_disclosure_boundaries|FullyQualifiedName~Native_default_composition_runs_while_model_reads_refuse_without_a_selected_provider|FullyQualifiedName~A_concurrent_service_fault_settles_every_original_start_without_another_semantic_decision|FullyQualifiedName~Direct_human_work_survives_service_provider_restart_and_completes_without_native_state' --results-directory /tmp/elsa-2898-review11-correctness/results --logger 'trx;LogFileName=ordering-routing-bpmn.trx'
```

The first filter matches exactly the two named delayed-compensation tests; no third similarly named case exists. Results are2/2,26/26,8/8, all zero skipped. All tested products identify6bd, with source equivalence to f83 as recorded. Tests use their existing private disposable harness stores; no manually seeded clock/claim/execution or changed shared source.

Read-only Python SHA256/gzip checks validated the364 outer qualification assets (decoded hashes where supplied),66 nested Studio assets and104 frozen baseline assets. JSON result files retain counts/errors. Full current discussion exports were hashed in context-audit.json and compared by body hash against already reviewed history; six new unique non-CLA bodies were read. Product file metadata/hash inspection is retained in products.json. No new build or actual user-host process was started.
