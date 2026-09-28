using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Elsa.Persistence.EntityFramework.Tests;

/// <summary>
/// Shared arrange/assert for the "a row with a newer, incompatible schema reports skew, not corruption" tests
/// (#2108, #2119). Each sets a row's schema version ahead of what the running build reads and damages its content,
/// so a skewed row's changed shape must be diagnosed by the version check before any deserializer or integrity
/// clause ever sees it. Each EF module's test project compiles this file in, the same way it compiles in
/// <see cref="UnorderedRowLimitGuard"/>.
/// </summary>
internal static class EfSchemaVersionSkewTestSupport
{
    public const string SkewedSchemaVersion = "2.0.0";
    public const string CorruptContentJson = "not-json";

    /// <summary>Sets a row's schema version ahead of the current build's and corrupts its content, then saves.</summary>
    public static async Task ArrangeSkewedRowAsync(DbContext context, Action<string> setSchemaVersion, Action<string> setContentJson)
    {
        setSchemaVersion(SkewedSchemaVersion);
        setContentJson(CorruptContentJson);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
    }

    /// <summary>
    /// Sets the schema version of every row in <paramref name="entityType"/>'s table and corrupts its
    /// <paramref name="contentProperty"/> column in SQL, past the context. A context that stamps its own writes would
    /// put the current version back on save, and a column EF converts takes no raw text through the context.
    /// </summary>
    public static async Task ArrangeSkewedTableAsync(DbContext context, Type entityType, string contentProperty, string schemaVersion = SkewedSchemaVersion)
    {
        var entity = context.Model.FindEntityType(entityType)!;
        var sql = context.GetService<ISqlGenerationHelper>();
        string Column(string property) => sql.DelimitIdentifier(entity.FindProperty(property)!.GetColumnName());
        // Identifiers from the model, values as parameters.
        var update = "UPDATE " + sql.DelimitIdentifier(entity.GetTableName()!, entity.GetSchema()) +
                     " SET " + Column(EfSchemaVersionMaterializationInterceptor.PropertyName) + " = {0}, " + Column(contentProperty) + " = {1}";
        await context.Database.ExecuteSqlRawAsync(update, schemaVersion, CorruptContentJson);
        context.ChangeTracker.Clear();
    }

    /// <summary>Asserts the skew was reported with the expected module and versions.</summary>
    public static void AssertSchemaVersionSkew(EfSchemaVersionSkewException skew, string module, string expectedSchemaVersion)
    {
        Assert.Equal(module, skew.Module);
        Assert.Equal(SkewedSchemaVersion, skew.Found);
        Assert.Equal(expectedSchemaVersion, skew.Expected);
    }
}
