using System.Reflection;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Configuration;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

/// <summary>The Runtime module's single declared chain for journal and derived history projections.</summary>
internal static class RuntimeHistoryEfModule
{
    internal const string SchemaFamily = "RuntimeHistory";
    internal const string SchemaVersion = "1.0.0";
    internal static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(RuntimeHistoryEfModule).Assembly, SchemaFamily);
    private static readonly RequiredShape[] Shapes = RequiredShapes();

    // Metadata only: capability checks never inspect history rows or activate execution work.
    internal static bool HasProjectionModel(RuntimeDbContext context)
    {
        foreach (var shape in Shapes)
        {
            var entity = context.Model.FindEntityType(shape.Entity);
            if (entity is null || entity.FindPrimaryKey() is not { } key ||
                !key.Properties.Select(property => property.Name).SequenceEqual(shape.Key)) return false;
            foreach (var member in shape.Properties)
            {
                var property = entity.FindProperty(member.Name);
                var binding = property?.PropertyInfo;
                if (property is null || binding is null || property.ClrType != member.PropertyType || binding.DeclaringType != member.DeclaringType ||
                    binding.Name != member.Name) return false;
            }
            if (entity.FindProperty("Revision")?.IsConcurrencyToken != true) return false;
            var indexes = entity.GetIndexes().ToArray();
            foreach (var required in shape.Indexes)
                if (!indexes.Any(index => (!required.Unique || index.IsUnique) &&
                    (required.Unique ? index.Properties.Count == required.Properties.Length : index.Properties.Count >= required.Properties.Length) &&
                    index.Properties.Take(required.Properties.Length).Select(property => property.Name).SequenceEqual(required.Properties))) return false;
        }
        return Shapes.Length > 0;
    }

    private static RequiredShape[] RequiredShapes()
    {
        // Derive mandatory key/index definitions from the same owned configurations as Runtime.
        // No provider conventions or storage access are needed for these explicit definitions.
        var builder = new ModelBuilder();
        RuntimeHistoryEntityConfiguration.Configure(builder);
        RuntimeHistoryOccurrenceEntityConfiguration.Configure(builder);
        return Chain.ContentColumns.Select(column => column.Entity).Distinct().Select(type =>
        {
            var entity = builder.Model.FindEntityType(type) ?? throw new InvalidOperationException("The declared history family has no owned entity configuration.");
            return new RequiredShape(type, type.GetProperties(BindingFlags.Instance | BindingFlags.Public),
                entity.FindPrimaryKey()!.Properties.Select(property => property.Name).ToArray(),
                entity.GetIndexes().Select(index => new RequiredIndex(index.Properties.Select(property => property.Name).ToArray(), index.IsUnique)).ToArray());
        }).ToArray();
    }

    private sealed record RequiredShape(Type Entity, PropertyInfo[] Properties, string[] Key, RequiredIndex[] Indexes);
    private sealed record RequiredIndex(string[] Properties, bool Unique);
}
