using System.Reflection;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

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
            foreach (var required in shape.Properties)
            {
                var member = required.Member;
                var property = entity.FindProperty(member.Name);
                var binding = property?.PropertyInfo;
                if (property is null || binding is null || property.ClrType != member.PropertyType || binding.DeclaringType != member.DeclaringType ||
                    binding.Name != member.Name || property.GetBeforeSaveBehavior() != required.BeforeSave ||
                    property.GetAfterSaveBehavior() != required.AfterSave || property.ValueGenerated != required.ValueGenerated) return false;
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
        // Derive mandatory key/index/write definitions from the same owned configurations as Runtime.
        // No provider conventions or storage access are needed for these explicit definitions.
        var builder = new ModelBuilder();
        var types = Chain.ContentColumns.Select(column => column.Entity).Distinct().ToArray();
        foreach (var type in types)
            foreach (var member in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
                builder.Entity(type).Property(member.PropertyType, member.Name);
        RuntimeHistoryEntityConfiguration.Configure(builder);
        RuntimeHistoryOccurrenceEntityConfiguration.Configure(builder);
        return types.Select(type =>
        {
            var entity = builder.Model.FindEntityType(type) ?? throw new InvalidOperationException("The declared history family has no owned entity configuration.");
            var properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public).Select(member =>
            {
                var property = entity.FindProperty(member.Name)!;
                return new RequiredProperty(member, property.GetBeforeSaveBehavior(), property.GetAfterSaveBehavior(), property.ValueGenerated);
            }).ToArray();
            return new RequiredShape(type, properties,
                entity.FindPrimaryKey()!.Properties.Select(property => property.Name).ToArray(),
                entity.GetIndexes().Select(index => new RequiredIndex(index.Properties.Select(property => property.Name).ToArray(), index.IsUnique)).ToArray());
        }).ToArray();
    }

    private sealed record RequiredShape(Type Entity, RequiredProperty[] Properties, string[] Key, RequiredIndex[] Indexes);
    private sealed record RequiredProperty(PropertyInfo Member, PropertySaveBehavior BeforeSave, PropertySaveBehavior AfterSave, ValueGenerated ValueGenerated);
    private sealed record RequiredIndex(string[] Properties, bool Unique);
}
