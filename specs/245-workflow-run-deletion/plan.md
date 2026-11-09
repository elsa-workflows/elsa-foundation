# Plan

1. Add a narrow persistence-owned terminal deletion contract with a provider-issued concurrency snapshot. Implement coherent first-party EF and in-memory cleanup; fail closed for unsupported/mixed backends.
2. Add a runtime management endpoint and capability relation, enforcing management and resource/tenant authorization before provider mutation.
3. Add accessible per-run actions and visible-page bulk selection, confirmation, bounded requests, partial-error recovery and stable pagination in Studio.
4. Apply the owner's stop-then-delete choice using the existing checkpointed cancellation path; never equate raw row deletion or cancellation acceptance with a stopped and deleted run.
5. Run focused provider/API/UI tests, isolated browser acceptance and integrated review. Open a scoped PR with gate evidence.
