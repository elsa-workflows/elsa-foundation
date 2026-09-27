using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// Shared arrange/assert for the "#2108: a row with a newer incompatible schema reports skew, not
/// corruption" tests repeated across several EF Runtime store test files. Each of those tests sets a
/// row's schema version ahead of what the running build understands and corrupts its content, so a
/// skewed row's changed shape must be diagnosed by the version check before any deserializer ever sees it.
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

    /// <summary>Asserts the skew was reported with the expected module and versions.</summary>
    public static void AssertSchemaVersionSkew(EfSchemaVersionSkewException skew, string module, string expectedSchemaVersion)
    {
        Assert.Equal(module, skew.Module);
        Assert.Equal(SkewedSchemaVersion, skew.Found);
        Assert.Equal(expectedSchemaVersion, skew.Expected);
    }
}
