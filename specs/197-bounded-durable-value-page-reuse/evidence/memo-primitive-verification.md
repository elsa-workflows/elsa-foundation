# T004–T005 memo primitive verification

The isolated raw-page memo is implemented and accepted by root and independent source/test review. It is not yet connected to a runtime store or session. This evidence establishes neither actual-EF eligibility nor any workflow query reduction, latency improvement, HTTP/REST behavior, recovery or concurrency repair.

## Candidate and checks

Based on implementation checkpoint `b232996f2db92efa09cbea7f74eeec7bc03b3e73`:

- Memo source SHA-256: `88cc85de67fdeb8a67143905e546301c8e0caac1f47c3be04baf3423d8ff6c05`.
- Memo tests SHA-256: `6e298bc5e753197a01098d3ee584f12f533888bbd545834d1bcf16592eb0f22c`.
- Root command: `dotnet test tests/essentials/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj --no-restore --filter 'FullyQualifiedName~CoalescingDurableValuePageMemoTests|FullyQualifiedName~WorkflowsRuntimeCheckpointPersistenceFeatureTests' --logger 'trx;LogFileName=t002-t005-root-restored.trx'`.
- Result: 24 passed, zero failed/skipped: 14 memo tests and ten configuration tests. The final restored TRX SHA-256 is `25c763b72a97dafe7cd6cde748fb7db8b17d833ef29d4aeb504a2969acea4a2a`.

The tests exercise exact context/request/codec-reference keys, detached complete row/page copies and JSON lifetime, successful empty pages, failed/cancelled provider reads, exact and over page/row/byte limits, cumulative content limits, mutable provider data, estimation/clone fallback, duplicate concurrent loads, active/overlapping write boundaries, late fills and permanent disablement. Root reviewed the test assertions as well as the implementation.

## Mutation proof

Removing only the pre-clone cumulative-budget predicate makes `Cumulative_content_cap_clears_prior_entries_and_returns_the_whole_overflow_page` fail: the provider metadata is enumerated twice instead of once because the overflowing result was cloned. That focused run failed one test as intended. The original source was restored byte-for-byte to the hash above and all 24 focused tests then passed. This proves that the allocation guard assertion detects the relevant regression; it is not a workflow-level reduction proof.

## Bounds and remaining obligations

The estimator uses checked arithmetic. An accepted partial content estimate is at most 4 MiB and a string byte count is bounded by `Int32`, so `Int64` addition overflow is unreachable before the content cap. Generation/write-count saturation likewise cannot be reached by a bounded practical test. These are defensive guards, not executed overflow tests or a claim of 100% branch coverage. The redundant per-page 1,024-row guard was removed because query limits are at most 500; the cumulative 1,024-row boundary and a mutable provider list that grows beyond its original query limit are tested.

The primitive takes no request cancellation token or current-context accessor. T006–T014 must prove eligibility, live guards and all actual store/write/owner call sites before enabling reuse. T003 remains partial until a real authored-cap session exposes and preserves both boolean settings. Final affected suites, provider/architecture/Maps gates, normal-host HTTP/REST, four-client response/settlement, malformed-input and interruption/recovery proof remain required.
