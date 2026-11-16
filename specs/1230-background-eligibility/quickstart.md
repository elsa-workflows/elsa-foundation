# Quickstart: check background eligibility

1. Build and run the eligibility tests:

   ```bash
   dotnet test Essentials/tests/Workflows/Runtime/Tests --filter "FullyQualifiedName~BackgroundEligibility"
   ```

   Every criterion C1–C6, C9, C10 has a refusal test at each point that checks it, and each message names the criterion.

2. Publish a workflow with a variable-reading activity placed in the background. Publication fails with
   `VF-BG-001 (C9)`. Place it inline and it publishes.

3. Place a marked built-in activity (see the spec's *Built-in assessment*) in the background on a host with
   `BackgroundActivities` enabled. It runs on a worker and its result is applied on the coordinator.

4. Run the causal-order pair on both providers:

   ```bash
   dotnet test Essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests --filter "FullyQualifiedName~BackgroundCausalOrder"
   ```
