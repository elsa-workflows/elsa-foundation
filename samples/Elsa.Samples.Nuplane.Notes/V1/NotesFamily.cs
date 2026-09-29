using Elsa.Persistence.EntityFramework;
using Elsa.Samples.Nuplane.Notes;

// Release 1.0.0: one version, no upcasters, and no document columns to upcast.
[assembly: EfSchemaFamily(NotesModule.Family, NotesModule.Name, NotesModule.CurrentVersion)]

namespace Elsa.Samples.Nuplane.Notes;

public static partial class NotesModule
{
    /// <summary>The version this build reads and writes: notes have an id, a text and a creation time.</summary>
    public const string CurrentVersion = "1.0.0";
}
