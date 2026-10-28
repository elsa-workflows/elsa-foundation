using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Elsa.Workflows.Runtime.Tests")]

// The repo-owned pump shares registration selection; external hosts consume the public GC contract.
[assembly: InternalsVisibleTo("Elsa.Workflows.Runtime.ReferenceGarbageCollection")]
