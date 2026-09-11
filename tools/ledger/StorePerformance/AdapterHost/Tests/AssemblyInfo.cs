using Xunit;

// Native explain capture uses process-global environment variables and artifact directories.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
