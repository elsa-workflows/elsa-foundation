// FastEndpoints stores its configuration in process-static state (FastEndpoints.Config). The
// configurator and integration tests in this assembly mutate and observe that shared state, so they
// must not run concurrently with one another.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
