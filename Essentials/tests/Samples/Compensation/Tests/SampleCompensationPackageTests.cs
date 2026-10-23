using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CShells.Features;
using Elsa.Activities.Design.Core.Reconciliation;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Samples.Compensation.Activities;
using Elsa.Samples.Compensation.Ledger;
using Elsa.Samples.Compensation.Reconciliation;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Samples.Compensation.Tests;

/// <summary>
/// The parts of the sample a reader copies: the feature's registrations, the catalog entries that must match the
/// activities' inputs, the three workflow files, and the guide's uncertain-outcome snippet, which is taken from
/// <see cref="Reserve"/> so it compiles.
/// </summary>
public sealed class SampleCompensationPackageTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string SampleDirectory = Path.Combine(RepositoryRoot, "samples", "Elsa.Samples.Compensation");

    [Fact]
    public void The_feature_depends_on_compensation_and_registers_its_catalog_and_ledger()
    {
        var attribute = typeof(SampleCompensationFeature).GetCustomAttribute<ShellFeatureAttribute>()!;
        Assert.Equal("SampleCompensation", attribute.Name);
        Assert.Equal(new object[] { "ActivitiesCompensation" }, attribute.DependsOn);

        var services = new ServiceCollection();
        new SampleCompensationFeature { Directory = "/var/elsa/ledger" }.ConfigureServices(services);

        Assert.Contains(services, service => service.ServiceType == typeof(IActivityReconciliationSource) &&
                                             service.ImplementationType == typeof(SampleCompensationActivityCatalog));
        var ledger = Assert.IsType<ReservationLedger>(Assert.Single(services, service => service.ServiceType == typeof(ReservationLedger)).ImplementationInstance);
        Assert.Equal(Path.GetFullPath("/var/elsa/ledger"), ledger.DirectoryPath);
    }

    [Fact]
    public void The_ledger_directory_defaults_to_a_folder_under_the_working_directory()
    {
        var services = new ServiceCollection();
        new SampleCompensationFeature().ConfigureServices(services);

        var ledger = (ReservationLedger)Assert.Single(services, service => service.ServiceType == typeof(ReservationLedger)).ImplementationInstance!;
        Assert.Equal(Path.GetFullPath(SampleCompensationFeature.DefaultDirectory), ledger.DirectoryPath);
    }

    [Theory]
    [InlineData(typeof(Reserve))]
    [InlineData(typeof(Release))]
    public async Task Each_catalog_entry_matches_its_activity_s_inputs(Type activityType)
    {
        var entry = Assert.Single(await new SampleCompensationActivityCatalog().Read(CancellationToken.None),
            candidate => candidate.ActivityTypeKey == activityType.FullName);
        Assert.Equal(WellKnownRuntimeActivityConsumers.ClrActivity, entry.ConsumerKey);

        var declared = activityType.GetProperties()
            .Select(property => (Property: property, Input: property.GetCustomAttribute<ActivityInputAttribute>()))
            .Where(candidate => candidate.Input is not null)
            .Select(candidate => (Key: candidate.Input!.Key ?? candidate.Property.Name, Name: candidate.Property.Name,
                Required: candidate.Property.GetCustomAttribute<RequiredAttribute>() is not null,
                // Optional inputs are nullable, so an unbound input is null rather than a default that looks authored.
                Nullable: Nullable.GetUnderlyingType(candidate.Property.PropertyType) is not null ||
                          new NullabilityInfoContext().Create(candidate.Property).WriteState == NullabilityState.Nullable))
            .OrderBy(input => input.Key, StringComparer.Ordinal)
            .ToArray();
        var catalogued = entry.Inputs
            .Select(input => (Key: input.ReferenceKey, input.Name, Required: input.IsRequired, Nullable: input.IsNullable))
            .OrderBy(input => input.Key, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(declared, catalogued);
        Assert.All(declared, input => Assert.NotEqual(input.Required, input.Nullable));
    }

    [Fact]
    public void Each_workflow_file_names_only_activities_the_workbench_offers()
    {
        string[] known =
        [
            "Elsa.Activities.Compensation.Activities.CompensationScope", "Elsa.Activities.Compensation.Activities.Confirm",
            typeof(Reserve).FullName!, typeof(Release).FullName!
        ];
        var files = Directory.GetFiles(Path.Combine(SampleDirectory, "workflows"), "*.json").Select(Path.GetFileName).Order(StringComparer.Ordinal);
        Assert.Equal(["nested-confirm.json", "operator-recovery.json", "reservation-saga.json"], files);

        foreach (var file in files)
        {
            var body = Workflow(file!);
            Assert.False(string.IsNullOrWhiteSpace(body["name"]?.GetValue<string>()), file);
            var root = body["state"]!["rootActivity"]!;
            Assert.Equal(("order", known[0]), (root["nodeId"]!.GetValue<string>(), root["activityVersionId"]!.GetValue<string>()));
            Assert.All(Nodes(root), node => Assert.Contains(node["activityVersionId"]!.GetValue<string>(), known));
            var ids = Nodes(root).Select(node => node["nodeId"]!.GetValue<string>()).ToArray();
            Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        }
    }

    [Fact]
    public void Every_release_binds_its_reservation_to_its_own_step_s_result()
    {
        foreach (var file in new[] { "reservation-saga.json", "nested-confirm.json", "operator-recovery.json" })
        {
            var steps = Nodes(Workflow(file)["state"]!["rootActivity"]!)
                .Where(node => node["structure"] is not null)
                .SelectMany(scope => scope["structure"]!["payload"]!["steps"]!.AsArray().Select(step => (Scope: scope["nodeId"]!.GetValue<string>(), Step: step!)))
                .Where(item => item.Step["compensation"] is JsonObject)
                .ToArray();
            Assert.NotEmpty(steps);
            foreach (var (scope, step) in steps)
            {
                var binding = Input(step["compensation"]!, "reservation")["value"]!;
                Assert.Equal("ActivityResult", binding["expressionType"]!.GetValue<string>());
                Assert.Equal((step["activity"]!["nodeId"]!.GetValue<string>(), ReservationResult.OutputKey, scope),
                    (binding["value"]!["producerNodeId"]!.GetValue<string>(), binding["value"]!["projectionKey"]!.GetValue<string>(),
                        binding["value"]!["producerScopeId"]!.GetValue<string>()));
            }
        }
    }

    [Fact]
    public void The_operator_recovery_fails_every_delivery_release_and_waits_between_attempts()
    {
        var root = Workflow("operator-recovery.json")["state"]!["rootActivity"]!;
        Assert.Equal((3, "00:00:05"), (Input(root, "handlerMaxAttempts")["value"]!["value"]!.GetValue<int>(),
            Input(root, "handlerRetryDelay")["value"]!["value"]!.GetValue<string>()));
        var release = Assert.Single(Nodes(root), node => node["nodeId"]!.GetValue<string>() == "release-delivery");
        Assert.Equal(99, Input(release, "failTimes")["value"]!["value"]!.GetValue<int>());
    }

    [Fact]
    public void The_guide_s_uncertain_outcome_snippet_is_the_sample_s_code()
    {
        var guide = File.ReadAllText(Path.Combine(RepositoryRoot, "website", "docs", "essentials", "compensation.md"));
        var section = guide[guide.IndexOf("## Uncertain outcomes", StringComparison.Ordinal)..];
        var snippet = Regex.Match(section, "```csharp\\n(?<code>.*?)\\n```", RegexOptions.Singleline).Groups["code"].Value;
        Assert.False(string.IsNullOrWhiteSpace(snippet), "The guide's Uncertain outcomes section has a C# snippet.");

        var source = File.ReadAllText(Path.Combine(SampleDirectory, "Activities", "Reserve.cs"));
        Assert.Contains(Normalize(snippet), Normalize(source), StringComparison.Ordinal);
    }

    private static JsonNode Workflow(string file) => JsonNode.Parse(File.ReadAllText(Path.Combine(SampleDirectory, "workflows", file)))!;

    private static JsonNode Input(JsonNode node, string key) =>
        Assert.Single(node["inputs"]!.AsArray(), input => input!["referenceKey"]!.GetValue<string>() == key)!;

    private static IEnumerable<JsonNode> Nodes(JsonNode node)
    {
        yield return node;
        foreach (var step in node["structure"]?["payload"]?["steps"]?.AsArray() ?? [])
        foreach (var child in new[] { step!["activity"], step["compensation"] }.OfType<JsonNode>())
        foreach (var descendant in Nodes(child))
            yield return descendant;
    }

    private static string Normalize(string code) => Regex.Replace(code, "\\s+", " ").Trim();

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Elsa.Server.slnx")))
                return directory.FullName;
        }

        throw new InvalidOperationException($"No Elsa.Server.slnx above {AppContext.BaseDirectory}.");
    }
}
