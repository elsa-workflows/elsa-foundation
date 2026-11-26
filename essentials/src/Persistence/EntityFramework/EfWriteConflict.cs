using Microsoft.EntityFrameworkCore;

namespace Elsa.Persistence.EntityFramework;

/// <summary>The kinds of lost write race a store may retry through <see cref="EfWriteRetry"/>.</summary>
[Flags]
public enum EfWriteConflict
{
    None = 0,

    /// <summary>An optimistic-concurrency predicate matched no row: <see cref="DbUpdateConcurrencyException"/>.</summary>
    Concurrency = 1,

    /// <summary>A racing insert committed the same unique key first.</summary>
    UniqueKey = 2,

    /// <summary>A serialization failure, deadlock, lock timeout, or busy database, anywhere in the exception chain.</summary>
    Transient = 4
}
