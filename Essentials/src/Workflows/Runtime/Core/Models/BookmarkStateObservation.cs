namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>A provider's current bookmark state and physical replacement generation; not delivery authority.</summary>
public sealed class BookmarkStateObservation
{
    public BookmarkStateObservation(BookmarkState state, long generation)
        : this(state, generation, null) { }

    public BookmarkStateObservation(BookmarkState state, long generation, PersistenceScope? scope)
    {
        ArgumentNullException.ThrowIfNull(state); ArgumentNullException.ThrowIfNull(state.Metadata);
        if (generation <= 0) throw new ArgumentOutOfRangeException(nameof(generation));
        State = state with { Payload = state.Payload?.Clone(), Metadata = RuntimeModelMetadata.Snapshot(state.Metadata) };
        Generation = generation;
        Scope = scope;
    }

    /// <summary>The observed EF physical scope, or null for globally shared native material; not caller authority.</summary>
    public PersistenceScope? Scope { get; }
    public BookmarkState State { get; }
    public long Generation { get; }
}
