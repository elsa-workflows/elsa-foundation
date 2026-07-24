using System.Diagnostics;
using System.Text.Json;
using Json.Schema;
using Xunit;

namespace Elsa.Diagnostics.Persistence.Tests;

public sealed class DiagnosticsPerformanceContractTests
{
    [Fact]
    public void Manifests_are_schema_valid_complete_and_define_exactly_forty_acceptance_lanes()
    {
        var root = RepositoryRoot();
        var directory = Path.Combine(root, "tools", "performance", "diagnostics");
        using var cases = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "cases.json")));
        using var workload = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "workload.json")));
        AssertSchemaValid(directory, "workload");
        AssertSchemaValid(directory, "datasets");
        AssertSchemaValid(directory, "cases");

        var operations = workload.RootElement.GetProperty("operations")
            .EnumerateArray()
            .ToDictionary(operation => operation.GetProperty("id").GetString()!);
        var definedCases = cases.RootElement.GetProperty("cases").EnumerateArray().ToArray();
        Assert.Equal(definedCases.Length, definedCases.Select(@case => @case.GetProperty("id").GetString()).Distinct().Count());

        foreach (var @case in definedCases)
        {
            var operationId = @case.GetProperty("operation").GetString()!;
            var profile = @case.GetProperty("profile").GetString()!;
            var operation = operations[operationId];
            Assert.Contains(profile, operation.GetProperty("profiles").EnumerateArray().Select(value => value.GetString()));
            Assert.Equal(
                operation.GetProperty("signals").EnumerateArray().Select(value => value.GetString()).Order(),
                @case.GetProperty("signals").EnumerateArray().Select(value => value.GetString()).Order());
            Assert.Equal(
                operation.GetProperty("dimensions").EnumerateArray().Select(value => value.GetString()).Order(),
                @case.GetProperty("dimensions").EnumerateObject().Select(property => property.Name).Order());
        }

        foreach (var operation in operations.Values)
        foreach (var profile in operation.GetProperty("profiles").EnumerateArray().Select(value => value.GetString()))
            Assert.Single(definedCases.Where(@case =>
                @case.GetProperty("operation").GetString() == operation.GetProperty("id").GetString() &&
                @case.GetProperty("profile").GetString() == profile));

        var acceptanceCases = definedCases.Count(@case => @case.GetProperty("profile").GetString() == "acceptance");
        Assert.Equal(8, acceptanceCases);
        Assert.Equal(40, acceptanceCases * 5);
        Assert.Equal(40, workload.RootElement.GetProperty("acceptance").GetProperty("expectedLanes").GetInt32());
        Assert.Equal(3, workload.RootElement.GetProperty("acceptance").GetProperty("requiredRunsPerLane").GetInt32());
    }

    private static void AssertSchemaValid(string directory, string name)
    {
        using var instance = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, $"{name}.json")));
        var schema = JsonSchema.FromText(File.ReadAllText(Path.Combine(directory, $"{name}.schema.json")));
        var result = schema.Evaluate(instance.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
        Assert.True(result.IsValid, JsonSerializer.Serialize(result));
    }

    [Fact]
    public async Task Evaluator_contract_accepts_bound_baseline_and_rejects_adversarial_evidence()
    {
        var root = RepositoryRoot();
        var script = Path.Combine(root, "tools", "performance", "diagnostics", "tests", "contract-tests.sh");
        var startInfo = new ProcessStartInfo("bash")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(script);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the performance contract test script.");
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.True(process.ExitCode == 0, $"Performance contract script failed with {process.ExitCode}:{Environment.NewLine}{output}{Environment.NewLine}{error}");
    }

    private static string RepositoryRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "Elsa.Server.slnx")))
                return current.FullName;
        throw new InvalidOperationException("Could not locate the repository root.");
    }
}
