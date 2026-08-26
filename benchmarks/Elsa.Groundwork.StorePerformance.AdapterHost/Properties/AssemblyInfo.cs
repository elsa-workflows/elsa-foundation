using System.Runtime.CompilerServices;

// The host has no public surface by design — it is an executable the matrix runner spawns, not a library.
// Its contracts are exercised offline by its own test project instead.
[assembly: InternalsVisibleTo("Elsa.Groundwork.StorePerformance.AdapterHost.Tests")]
