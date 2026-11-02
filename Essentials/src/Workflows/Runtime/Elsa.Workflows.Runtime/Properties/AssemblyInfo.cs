using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Elsa.Workflows.Runtime.Tests")]

// The repo-owned pump shares registration selection; external hosts consume the public GC contract.
[assembly: InternalsVisibleTo("Elsa.Workflows.Runtime.ReferenceGarbageCollection")]

// The distribution leaf composes the same configured local mailbox factory as the core runtime.
[assembly: InternalsVisibleTo("Elsa.Workflows.Runtime.Distributed")]
