using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Bpmn.Model;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Bpmn.Tests;

public sealed partial class BpmnDirectExecutionTests
{
    [Fact]
    public async Task A_current_writer_publishes_format2_material_without_inventing_a_fault_policy()
    {
        await using var host = await OpenAsync();
        var seed = new WorkflowExecutable(WorkflowExecutionHarness.Identity, WorkflowExecutionHarness.NewFaultingNode("v2-worker", "Expected failure"),
            new Dictionary<string, WorkflowExecutableResumeTarget>(), WorkflowExecutionHarness.Timestamp, new Dictionary<string, string>(),
            inputContract: new(1, []), dependencies: null, incidentStrategy: IncidentStrategyBuiltIns.FaultReference);
        await host.PublishAsync(seed, "v2-material-seed");
        await using var scope = await ScopeAsync(host);
        var store = scope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>();
        var closed = (await store.FindAsync(seed.Identity.ArtifactId))!;
        var hasher = scope.ServiceProvider.GetRequiredService<IWorkflowExecutableHasher>();
        var hash = hasher.ComputeHash(closed.RootActivity, closed.InputContract!, closed.Dependencies,
            closed.PersistenceMode, closed.WorkflowVariables, closed.IncidentStrategy);
        var worker = new WorkflowExecutable(closed.Identity with { ArtifactId = hasher.CreateArtifactId("artifact-", hash), ArtifactHash = hash },
            closed.RootActivity, closed.ResumeTargets, closed.CreatedAt, closed.CompatibilityMetadata, closed.InputContract,
            closed.Dependencies, closed.RuntimeRequirements, closed.StorageDriverRequirements, closed.IncidentStrategy,
            closed.PersistenceMode, closed.WorkflowVariables);
        await host.PublishAsync(worker, "v2-material-worker");
        Assert.True(hasher.MatchesHash((await store.FindAsync(worker.Identity.ArtifactId))!));
        var runtime = scope.ServiceProvider.GetRequiredService<IBpmnModelRuntime>();
        var caller = new BpmnModelCaller("operator", null);
        var process = new BpmnProcessDefinition("current-format2", Elements:
            [new("start", BpmnElementTypes.StartEvent), new("work", BpmnElementTypes.ServiceTask, bindingRef: "worker"), new("end", BpmnElementTypes.EndEvent)],
            SequenceFlows: [new("start-work", "start", "work"), new("work-end", "work", "end")]);
        var published = await runtime.PublishAsync(new(2, JsonSerializer.SerializeToElement(process, Web), [],
            [new("worker", worker.Identity, "worker.business-error")]), "current-format2-publish", caller);
        var material = (await store.FindMaterialAsync(published.Executable.ArtifactId))!.Model!;
        Assert.Equal(2, material.Definition.GetProperty("formatVersion").GetInt32());
        Assert.False(material.Definition.GetProperty("serviceBindings")[0].TryGetProperty("faultPolicy", out _));
        Assert.Equal(published.Executable.ArtifactHash, material.ComputeContentHash());
        Assert.Equal(worker.Identity, Assert.Single(material.Dependencies).Executable);
        if (Environment.GetEnvironmentVariable("ELSA_BPMN_V2_MATERIAL_EVIDENCE") is not { Length: > 0 } path) return;
        var source = await CaptureSourceReceiptAsync();
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        await JsonSerializer.SerializeAsync(output, new
        {
            schemaVersion = 1, kind = "current-writer-format2-material", capturedAt = DateTimeOffset.UtcNow,
            captureProvenance = source, identity = material.Identity, protocol = material.Protocol,
            profile = material.Profile, definition = material.Definition, dependencies = material.Dependencies,
            inputContract = material.InputContract, outputs = material.Outputs, runtimeRequirements = material.RuntimeRequirements,
            expectedContentHash = material.ComputeContentHash(),
            scope = "Current ordinary publication with a freshly closed worker; material/hash witness, not execution or old-writer upgrade recovery."
        }, Web);
    }

    private static async Task<CaptureSourceReceipt> CaptureSourceReceiptAsync()
    {
        DirectoryInfo? repository = new(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "Elsa.Server.slnx"))) repository = repository.Parent;
        Assert.NotNull(repository);
        var source = await GitAsync("rev-parse", "HEAD");
        Assert.Equal(string.Empty, await GitAsync("status", "--porcelain"));
        var names = new HashSet<string>(["Elsa.Activities.Bpmn.Tests", "Elsa.Activities.Bpmn", "Elsa.Workflows.Runtime.Core",
            "Elsa.Workflows.Runtime", "Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore", "Bpmn.Model", "Bpmn.Semantics"], StringComparer.Ordinal);
        var assemblies = AppDomain.CurrentDomain.GetAssemblies().Where(assembly => names.Contains(assembly.GetName().Name!)).ToArray();
        Assert.Equal(names.Count, assemblies.Length);
        var hashes = assemblies.ToDictionary(assembly => Path.GetFileName(assembly.Location),
            assembly => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(assembly.Location))));
        var versions = assemblies.ToDictionary(assembly => Path.GetFileName(assembly.Location),
            assembly => assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? string.Empty);
        Assert.EndsWith("+" + source, versions["Elsa.Activities.Bpmn.Tests.dll"]);
        return new(1, source, false, hashes, versions);

        async Task<string> GitAsync(params string[] arguments)
        {
            var start = new ProcessStartInfo("git") { WorkingDirectory = repository.FullName, UseShellExecute = false,
                RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.True(process.ExitCode == 0, await stderr);
            return (await stdout).Trim();
        }
    }

    private sealed record CaptureSourceReceipt(int Version, string SourceSha, bool SourceDirty,
        IReadOnlyDictionary<string, string> BinaryHashes, IReadOnlyDictionary<string, string> BinaryVersions);
}
