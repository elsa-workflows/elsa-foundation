using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Elsa.Persistence.EntityFramework.Tests;

/// <summary>
/// Filters the finalization tables every module maps (<see cref="EfSchemaFinalization.Maps"/>) out of a model's
/// entity types, so a module's own-table assertions don't have to repeat that predicate. Each EF module's test
/// project compiles this file in, the same way it compiles in <see cref="UnorderedRowLimitGuard"/>.
/// </summary>
internal static class EfSchemaFinalizationTestFilter
{
    /// <summary>The module's own entity types, with the finalization tables every module maps left out.</summary>
    public static IEnumerable<IEntityType> ExcludingSchemaFinalization(this IEnumerable<IEntityType> entityTypes) =>
        entityTypes.Where(entityType => !EfSchemaFinalization.Maps(entityType.ClrType));
}
