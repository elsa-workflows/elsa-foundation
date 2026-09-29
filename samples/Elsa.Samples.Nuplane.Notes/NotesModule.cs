using Elsa.Persistence.EntityFramework;

namespace Elsa.Samples.Nuplane.Notes;

/// <summary>The names the module, its schema family and its features are declared and composed by.</summary>
public static partial class NotesModule
{
    /// <summary>The EF module, as its <c>[EfModule]</c> names it.</summary>
    public const string Name = "Samples.Notes";

    /// <summary>The module's migrations-history name, which also names its finalization tables.</summary>
    public const string HistoryModuleName = "ElsaSamplesNotes";

    /// <summary>The one table: a note per row.</summary>
    public const string TableName = "elsa_samples_notes";

    /// <summary>The module's one schema family: the tables whose rows stamp one persisted-schema version.</summary>
    public const string Family = "SamplesNotes";

    /// <summary>The feature that binds the module's context and has its migrator admit it through its finalization gate.</summary>
    public const string EntityFrameworkCoreFeature = "NotesEntityFrameworkCore";

    /// <summary>The feature that adds and lists notes.</summary>
    public const string NotesFeature = "Notes";

    /// <summary>Where notes are added (POST) and listed (GET).</summary>
    public const string NotesPath = "/demo/notes";

    /// <summary>The family's one chain handle, which every read checks and upcasts through.</summary>
    public static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(NotesModule).Assembly, Family);
}
