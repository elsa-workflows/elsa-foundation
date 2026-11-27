using System.Reflection;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Xunit;

namespace Elsa.Activities.Design.Tests.Contracts;

/// <summary>
/// Guards the one silent failure mode of <see cref="ActivityValueOutcomesAttribute"/>: its constructor argument is
/// resolved as an input <b>contract key</b> (that is how <c>ExecutableNodeCompiler.AddValueDerivedOutcomes</c> looks
/// up the authored binding, and how the design facet advertises the source), but it is a plain string, so writing
/// the property name instead of the key compiles, publishes, and simply produces no dynamic outcome ports at all.
/// Every declaration is checked against the activity's real input keys here so that mistake fails loudly.
/// </summary>
public sealed class ActivityValueOutcomeSourceTests
{
    public static TheoryData<Type> DeclaringActivities
    {
        get
        {
            var data = new TheoryData<Type>();
            foreach (var type in ActivityContractSurfaceScan.ShippedActivityTypes
                         .Where(type => type.GetCustomAttribute<ActivityValueOutcomesAttribute>(inherit: true) is not null))
                data.Add(type);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(DeclaringActivities))]
    public void Value_outcome_source_names_a_real_input_contract_key(Type activityType)
    {
        var declaration = activityType.GetCustomAttribute<ActivityValueOutcomesAttribute>(inherit: true)!;

        var inputKeys = activityType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => (Property: property, Attribute: property.GetCustomAttribute<ActivityInputAttribute>(inherit: true)))
            .Where(candidate => candidate.Attribute is not null)
            .Select(candidate => candidate.Attribute!.Key ?? candidate.Property.Name)
            .ToArray();

        Assert.True(
            inputKeys.Contains(declaration.InputKey, StringComparer.Ordinal),
            $"[ActivityValueOutcomes] on '{activityType.FullName}' names source '{declaration.InputKey}', which is not " +
            $"an input contract key on that activity. Declared keys: {string.Join(", ", inputKeys.Order(StringComparer.Ordinal))}. " +
            "The compiler resolves the source by contract key, so a property-name spelling silently yields no ports.");
    }

    /// <summary>The attribute is worth having only if some activity uses it; an empty sweep would pass vacuously.</summary>
    [Fact]
    public void At_least_one_shipped_activity_declares_value_derived_outcomes() =>
        Assert.NotEmpty(ActivityContractSurfaceScan.ShippedActivityTypes
            .Where(type => type.GetCustomAttribute<ActivityValueOutcomesAttribute>(inherit: true) is not null));

    /// <summary>Sanity: the shared activity-type sweep really sees the shipped library, not an empty set.</summary>
    [Fact]
    public void Shipped_activity_sweep_is_not_empty() =>
        Assert.NotEmpty(ActivityContractSurfaceScan.ShippedActivityTypes);
}

/// <summary>
/// The set of concrete <c>IActivity</c> types in the shipped activity assemblies, loaded normally (not through the
/// reflection-only <c>MetadataLoadContext</c> the snapshot guard uses) so attribute instances can be read directly.
/// </summary>
public static class ActivityContractSurfaceScan
{
    public static IReadOnlyList<Type> ShippedActivityTypes { get; } = ActivityContractSurfaceSnapshotTests
        .ShippedActivityAssemblies
        .SelectMany(assembly => assembly.GetTypes())
        .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IActivity).IsAssignableFrom(type))
        .OrderBy(type => type.FullName, StringComparer.Ordinal)
        .ToArray();
}
