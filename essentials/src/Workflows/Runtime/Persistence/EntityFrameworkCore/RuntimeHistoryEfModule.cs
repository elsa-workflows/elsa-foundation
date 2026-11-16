using System.Reflection;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;

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
                    property.GetAfterSaveBehavior() != required.AfterSave || property.ValueGenerated != required.ValueGenerated ||
                    !HasOwnedMemberAccess(property, member)) return false;
                // Configured conversions/comparers can silently change or suppress the owned scalar writes.
                var declared = (IReadOnlyProperty)property;
                if (declared.GetValueConverter() is not null ||
                    declared.GetValueComparer() is { } comparer && !HasDefaultComparer(comparer)) return false;
            }
            if (entity.FindProperty("Revision")?.IsConcurrencyToken != true) return false;
            var indexes = entity.GetIndexes().ToArray();
            foreach (var required in shape.Indexes)
                if (!indexes.Any(index => (required.Unique == index.IsUnique) &&
                    (required.Unique ? index.Properties.Count == required.Properties.Length : index.Properties.Count >= required.Properties.Length) &&
                    index.Properties.Take(required.Properties.Length).Select(property => property.Name).SequenceEqual(required.Properties))) return false;
            // An extra uniqueness constraint may reject valid history even when all required indexes remain.
            // A superset of an owned unique key is redundant and adds no restriction.
            var uniqueKeys = shape.Indexes.Where(index => index.Unique).Select(index => index.Properties).Append(shape.Key).ToArray();
            bool IsCompatible(IEnumerable<IProperty> properties) => uniqueKeys.Any(owned =>
                owned.All(name => properties.Any(property => property.Name == name)));
            if (indexes.Where(index => index.IsUnique).Any(index => !IsCompatible(index.Properties)) ||
                entity.GetKeys().Any(actual => !IsCompatible(actual.Properties))) return false;
        }
        return Shapes.Length > 0;
    }

    private static bool HasDefaultComparer(ValueComparer comparer) => comparer.IsDefault() ||
        comparer.GetType().Assembly == typeof(ValueComparer).Assembly &&
        comparer is IInfrastructure<ValueComparer> wrapper && wrapper.Instance.IsDefault();

    private static bool HasOwnedMemberAccess(IProperty property, PropertyInfo owned)
    {
        var field = owned.DeclaringType!.GetField($"<{owned.Name}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        bool Matches(MemberInfo actual) => actual.Equals(owned) || field is not null && actual.Equals(field);
        return Matches(property.GetMemberInfo(forMaterialization: false, forSet: false)) &&
            Matches(property.GetMemberInfo(forMaterialization: false, forSet: true)) &&
            Matches(property.GetMemberInfo(forMaterialization: true, forSet: true));
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
