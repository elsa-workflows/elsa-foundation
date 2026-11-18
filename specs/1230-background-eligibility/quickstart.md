# Quickstart: check background eligibility

1. Build and run the eligibility tests:

   ```bash
   dotnet test Essentials/tests/Workflows/Runtime/Tests --filter "FullyQualifiedName~BackgroundEligibility"
   ```

   Every criterion C1–C6, C9, C10 has a refusal test at each point that checks it in the component tests, and each
   message names the criterion. Through the real call sites, the tests cover the combinations listed under *Recorded
   limits* in tasks.md.

2. Publish a workflow with a variable-reading activity placed in the background. Publication fails with
   `VF-BG-001 (C9)`. Place it inline and it publishes.

3. Place a marked built-in activity (see the spec's *Built-in assessment*) in the background on a host with
   `BackgroundActivities` enabled. It runs on a worker and its result is applied on the coordinator.

4. Run the causal-order tests (US6) on both providers:

   ```bash
   dotnet test Essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests --filter "FullyQualifiedName~A_dependent_step_runs_after_the_background_result_is_applied|FullyQualifiedName~Unordered_background_writes_to_one_variable|FullyQualifiedName~A_failed_result_commit_applies_nothing"
   ```
