using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Elsa.Workflows.Runtime.Tests")]

// The distribution leaf composes the same configured local mailbox factory as the core runtime.

// API composition replaces only this domain's unavailable access default; external adapters use the Core contract.
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Elsa.Workflows.Runtime.Api")]

// The same Runtime domain EF aggregate replaces only the owned volatile journal default.
[assembly: InternalsVisibleTo("Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore")]
[assembly: InternalsVisibleTo("Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests")]
