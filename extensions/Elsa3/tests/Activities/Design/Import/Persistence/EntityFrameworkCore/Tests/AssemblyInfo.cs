using Xunit;

// The suites share temporary SQLite files per test, but the concurrency proofs time two applies against one
// file; running classes in parallel would only add unrelated writer contention to those measurements.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
