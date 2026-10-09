using System.Reflection;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Canary.Fixtures;
using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Xunit;

namespace Elsa.Secrets.Workflows.Tests;

/// <summary>
/// No name the canary gives a value matches a name-based redactor (spec 188, T078, research R10): not the canary
/// activities' type names, which the catalog, the runtime and every inspection surface record, not their input names,
/// under which the diagnostic snapshot factory captures a value, and not the secret reference names. A redactor that
/// matched one would hide the value by its name, and a disabled protection would stay green for the wrong reason.
/// </summary>
public sealed class CanaryNamingTests
{
    private static readonly Type[] CanaryActivityTypes = typeof(CanaryActivity).Assembly.GetTypes()
        .Where(type => type.Namespace == typeof(CanaryActivity).Namespace && typeof(IActivity).IsAssignableFrom(type) && !type.IsAbstract)
        .ToArray();

    public static TheoryData<string> Names()
    {
        var names = new TheoryData<string>();
        foreach (var type in CanaryActivityTypes)
            names.Add(type.FullName!);
        foreach (var input in CanaryActivityTypes.SelectMany(type => type.GetProperties())
                     .Select(property => (Property: property, Attribute: property.GetCustomAttribute<ActivityInputAttribute>()))
                     .Where(candidate => candidate.Attribute is not null)
                     .Select(candidate => candidate.Attribute!.Key ?? candidate.Property.Name)
                     .Distinct(StringComparer.Ordinal))
            names.Add(input);
        foreach (var label in WorkflowSecretCanaryTests.ScenarioLabels)
            names.Add(WorkflowSecretCanaryTests.ReferenceName(label, "0123abcd"));
        return names;
    }

    [Fact]
    public void The_canary_activities_are_found() =>
        Assert.Equal(
            [typeof(CanaryActivity), typeof(CanaryCheckpointActivity), typeof(CanaryChildActivity), typeof(CanaryNotifiedStructuralActivity), typeof(CanaryNotifyingChildActivity), typeof(CanaryStructuralActivity)],
            CanaryActivityTypes.OrderBy(type => type.Name, StringComparer.Ordinal));

    [Theory]
    [MemberData(nameof(Names))]
    public void The_diagnostic_snapshot_factory_does_not_redact_a_value_by_this_name(string name) =>
        Assert.Equal("string", DefaultDiagnosticSnapshotFactory.CaptureSnapshot("canary", name).Kind);

    [Theory]
    [MemberData(nameof(Names))]
    public void No_default_sensitive_name_of_the_OpenTelemetry_redactor_occurs_in_this_name(string name) =>
        Assert.DoesNotContain(new OpenTelemetryDiagnosticsOptions().SensitiveNames, sensitive => name.Contains(sensitive, StringComparison.OrdinalIgnoreCase));
}
