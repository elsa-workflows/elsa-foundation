using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

// Calls the existing publication-retirement and GC ports over controller-owned actual stores.
// It never writes an execution, queue, claim or receipt and never supplies a synthetic clock.
internal static class MaterialRetentionProbe
{
    internal static async Task RunAsync(IServiceProvider services, JsonElement owner, RuntimeModelExecutionState model, JsonSerializerOptions json)
    {
        if (model.Revision != 1 || model.Lifecycle != RuntimeModelLifecycle.Running || model.TerminalResult is not null ||
            model.Protocol != new RuntimeExecutionProtocol("elsa.bpmn", 1))
            throw new InvalidOperationException("Retirement requires the actual first committed native-work model decision.");
        var artifacts = services.GetRequiredService<IWorkflowExecutableStore>();
        var references = services.GetRequiredService<IWorkflowExecutableSourceReferenceStore>();
        var workers = model.Payload.GetProperty("work").EnumerateArray().Select(work =>
            work.GetProperty("nativeWork").Deserialize<RuntimeModelNativeWorkOrigin>(json)!).ToArray();
        if (workers.Length != 2 || workers.Any(worker => !worker.MatchesModel(model)) ||
            await services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(workers[0].WorkerExecutionId) is not null ||
            await services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(workers[1].WorkerExecutionId) is not null)
            throw new InvalidOperationException("Both real native admissions must remain owed before retirement.");
        var selected = owner.GetProperty("retirementSources").Deserialize<RetirementSource[]>(json)
            ?? throw new InvalidDataException("Owned publication retirement sources are missing.");
        var orphan = owner.GetProperty("orphanArtifactId").GetString()!;
        if (selected.Length is not (3 or 4) || selected.Select(source => source.SourceReferenceId).Distinct(StringComparer.Ordinal).Count() != selected.Length ||
            selected.Count(source => source.Executable.ArtifactId == model.Executable.ArtifactId) != 1 ||
            selected.Count(source => source.Executable.ArtifactId == orphan) != 1 || workers.Any(worker =>
                !selected.Any(source => WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(source.Executable, worker.WorkerExecutable))))
            throw new InvalidDataException("Only this journey's exact model, worker, optional parent and orphan publications may be retired.");
        var before = new List<WorkflowExecutableSourceReference>();
        var retained = new List<RuntimeExecutionMaterial>();
        foreach (var source in selected)
        {
            var reference = await references.FindAsync(source.SourceReferenceId) ?? throw new InvalidDataException("An owned publication source is absent.");
            var material = await artifacts.FindMaterialAsync(reference.ArtifactId) ?? throw new InvalidDataException("An owned published material is absent.");
            if (reference.TenantId != model.TenantId || !reference.IsLive(DateTimeOffset.UtcNow) || reference.ArtifactId != source.Executable.ArtifactId ||
                reference.DefinitionId != source.Executable.DefinitionId || reference.DefinitionVersionId != source.Executable.DefinitionVersionId ||
                reference.ArtifactVersion != source.Executable.ArtifactVersion || !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(material.Identity, source.Executable))
                throw new InvalidDataException("A retirement source does not name its exact actual publication.");
            before.Add(reference);
            if (material.Identity.ArtifactId != orphan) retained.Add(material);
        }
        var at = DateTimeOffset.UtcNow;
        foreach (var source in selected)
            if (!await references.RetireAsync(source.SourceReferenceId, at, "owned-runtime-only-rollback-qualification"))
                throw new InvalidOperationException("The normal publication retirement port refused its owned live source.");
        var collector = services.GetRequiredService<IWorkflowExecutableReferenceGarbageCollector>();
        if (!collector.SupportsStimulusDeliveryRetention) throw new InvalidOperationException("The selected collector does not retain durable delivery roots.");
        var sweep = await collector.SweepAsync();
        var after = new List<RuntimeExecutionMaterial>();
        foreach (var material in retained)
        {
            var actual = await artifacts.FindMaterialAsync(material.Identity.ArtifactId) ?? throw new InvalidDataException("Collection removed owed pinned material.");
            if (actual.IncarnationId != material.IncarnationId || !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(actual.Identity, material.Identity))
                throw new InvalidDataException("Collection changed an owed material pin or incarnation.");
            after.Add(actual);
        }
        if (await artifacts.FindMaterialAsync(orphan) is not null || sweep.DeletedArtifactCount < 1)
            throw new InvalidDataException("The actual collector did not remove the unreferenced control artifact.");
        var unchanged = await services.GetRequiredService<IRuntimeModelExecutionReader>().FindAsync(model.ExecutionId);
        if (!JsonElement.DeepEquals(JsonSerializer.SerializeToElement(model, json), JsonSerializer.SerializeToElement(unchanged, json)))
            throw new InvalidDataException("Publication retirement/collection changed the actual model execution.");
        var referencesAfter = new List<WorkflowExecutableSourceReference?>();
        foreach (var source in selected) referencesAfter.Add(await references.FindAsync(source.SourceReferenceId));
        object Product(Assembly assembly) => new { name = assembly.GetName().Name,
            informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion,
            sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(assembly.Location))) };
        Console.WriteLine(JsonSerializer.Serialize(new { operation = "--retire-and-sweep", processId = Environment.ProcessId,
            controllerProcessId = owner.GetProperty("controllerProcessId").GetInt32(), executable = Environment.ProcessPath,
            startedAtUtc = at, completedAtUtc = DateTimeOffset.UtcNow,
            referencesBefore = before, referencesAfter,
            materialsBefore = retained, materialsAfter = after, orphanArtifactId = orphan, orphanCollected = true, sweep, modelBefore = model, modelAfter = unchanged,
            executionStateEdited = false, claimEdited = false, clockEdited = false, supportsStimulusDeliveryRetention = collector.SupportsStimulusDeliveryRetention,
            products = new[] { Product(typeof(MaterialRetentionProbe).Assembly), Product(typeof(IWorkflowExecutableStore).Assembly), Product(artifacts.GetType().Assembly), Product(collector.GetType().Assembly) } }, json));
    }

    private sealed record RetirementSource(string SourceReferenceId, WorkflowExecutableIdentity Executable);
}
