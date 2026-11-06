using System.Runtime.CompilerServices;

// Only the same-domain coordinator mints opaque owner sessions inside the configured issuer callback.
[assembly: InternalsVisibleTo("Elsa.Workflows.Runtime")]
