using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Elsa.Groundwork.StorePerformance.Benchmarks.Tests")]

// The adapter child host must serialize the native-plan evidence document and deserialize the RunRequest
// with byte-identical semantics to ArtifactStore.JsonOptions. Replicating those options in the host would
// silently drift the moment a converter is added here, and the drift would surface as a fail-closed
// correctness rejection minutes into a matrix run. Sharing the options is the narrower risk.
[assembly: InternalsVisibleTo("Elsa.Groundwork.StorePerformance.AdapterHost")]
[assembly: InternalsVisibleTo("Elsa.Groundwork.StorePerformance.AdapterHost.Tests")]
