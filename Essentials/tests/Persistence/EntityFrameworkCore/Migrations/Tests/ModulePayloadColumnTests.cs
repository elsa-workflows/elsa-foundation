using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Persistence.EntityFrameworkCore.Migrations.Tests;

/// <summary>
/// Pins which columns each module declared as payload columns, on every provider.
/// <para>
/// A module declares them by name, and a name no entity carries is ignored on purpose, so one list can serve a model
/// whose entities differ. That tolerance is also the failure mode this file exists to catch: a typo in a module's
/// list silently matches nothing, every other test stays green, and the column quietly stops being encoded. Asserting
/// the exact set per context is what makes the wiring visible.
/// </para>
/// </summary>
public sealed class ModulePayloadColumnTests
{
    /// <summary>
    /// Context type name to the payload column names it must carry a converter on, counted across every entity that
    /// declares one. A module that adds a payload column adds it here too, deliberately.
    /// </summary>
    private static readonly Dictionary<string, Dictionary<string, int>> Expected = new(StringComparer.Ordinal)
    {
        ["RuntimeDbContext"] = new(StringComparer.Ordinal)
        {
            ["ContentJson"] = 30,
            ["PayloadJson"] = 1,
            ["MetadataJson"] = 1,
            ["CleanupSafeFailureJson"] = 1
        },
        ["ExecutionCommandTransportDbContext"] = new(StringComparer.Ordinal) { ["PayloadJson"] = 1 },
        ["WorkflowsDesignDbContext"] = new(StringComparer.Ordinal)
        {
            ["StateSource"] = 2,
            ["RecordsJson"] = 2,
            ["ActivityPresentationJson"] = 2,
            ["ResultJson"] = 1
        },
        ["EfOpenTelemetryDbContext"] = new(StringComparer.Ordinal)
        {
            ["PayloadJson"] = 7,
            ["ServiceMembershipJson"] = 1,
            ["WorkflowMembershipJson"] = 1
        },
        ["StructuredLogsDbContext"] = new(StringComparer.Ordinal) { ["PayloadJson"] = 1, ["OutcomeJson"] = 1 },
        ["PublishingSnapshotReviewDbContext"] = new(StringComparer.Ordinal) { ["Content"] = 2 },
        ["Elsa3ImportDbContext"] = new(StringComparer.Ordinal) { ["ContentJson"] = 2 },
        ["ActivitiesDesignDbContext"] = new(StringComparer.Ordinal)
        {
            ["AuthoritativeResultJson"] = 1,
            ["MutatedUnitsJson"] = 1,
            ["PlanJson"] = 1,
            ["ReceiptJson"] = 1,
            ["DefinitionMaterialJson"] = 1,
            ["DescriptorPayloadSource"] = 1,
            ["InputsSource"] = 1,
            ["OutputsSource"] = 1,
            ["DesignFacetsSource"] = 1
        }
    };

    [Theory]
    [MemberData(nameof(Providers))]
    public void Every_wired_module_encodes_exactly_the_columns_it_declared(string provider)
    {
        foreach (var type in ModuleContextCatalog.Contexts(provider))
        {
            var expected = Expected.GetValueOrDefault(BaseName(type));
            if (expected is null)
                continue;
            using var context = ModuleContextCatalog.Create(type, ModuleContextCatalog.PlaceholderConnection(provider));

            var converted = Converted(context);

            Assert.Equal(expected.OrderBy(pair => pair.Key, StringComparer.Ordinal), converted.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        }
    }

    /// <summary>
    /// A module that is deliberately not wired must stay unwired. Secrets holds secret material and Identity and
    /// Studio Preferences hold columns short enough that encoding them buys nothing.
    /// </summary>
    [Theory]
    [MemberData(nameof(Providers))]
    public void A_module_that_declared_nothing_encodes_nothing(string provider)
    {
        foreach (var type in ModuleContextCatalog.Contexts(provider).Where(type => !Expected.ContainsKey(BaseName(type))))
        {
            using var context = ModuleContextCatalog.Create(type, ModuleContextCatalog.PlaceholderConnection(provider));

            Assert.Empty(Converted(context));
        }
    }

    /// <summary>
    /// The column no module may ever declare. Four provider computed columns read it in the database, so a
    /// client-side encoding would make every activity definition vanish from paged reads rather than error.
    /// </summary>
    [Theory]
    [MemberData(nameof(Providers))]
    public void The_activity_authority_columns_are_never_encoded(string provider)
    {
        foreach (var type in ModuleContextCatalog.Contexts(provider))
        {
            using var context = ModuleContextCatalog.Create(type, ModuleContextCatalog.PlaceholderConnection(provider));

            Assert.DoesNotContain(
                Converted(context).Keys,
                name => name.StartsWith("ContentAuthority", StringComparison.Ordinal));
        }
    }

    public static TheoryData<string> Providers()
    {
        var data = new TheoryData<string>();
        foreach (var provider in ModuleContextCatalog.Providers)
            data.Add(provider);
        return data;
    }

    private static string BaseName(Type context) => context.BaseType!.Name;

    private static Dictionary<string, int> Converted(DbContext context) => context.Model
        .GetEntityTypes()
        .SelectMany(entity => entity.GetProperties())
        .Where(EfPayloadColumns.IsPayloadColumn)
        .GroupBy(property => property.Name, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
}
