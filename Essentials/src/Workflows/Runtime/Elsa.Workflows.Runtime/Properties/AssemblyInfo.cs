using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Elsa.Workflows.Runtime.Tests")]

// Native activity delivery stories qualify the internal history composition before public host enablement.
[assembly: InternalsVisibleTo("Elsa.Activities.Runtime.Tests")]

// The repo-owned pump shares registration selection; external hosts consume the public GC contract.
[assembly: InternalsVisibleTo("Elsa.Workflows.Runtime.ReferenceGarbageCollection")]

// The distribution leaf composes the same configured local mailbox factory as the core runtime.
[assembly: InternalsVisibleTo("Elsa.Workflows.Runtime.Distributed")]

// API composition replaces only this domain's unavailable access default; external adapters use the Core contract.
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Elsa.Workflows.Runtime.Api")]

// The same Runtime domain EF aggregate replaces only the owned volatile journal default.
[assembly: InternalsVisibleTo("Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore")]
