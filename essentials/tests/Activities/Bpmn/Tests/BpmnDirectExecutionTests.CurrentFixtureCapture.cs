using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Bpmn.Tests;

public sealed partial class BpmnDirectExecutionTests
{
    /// <summary>Optional complete-store capture from an ordinary current writer; never repairs archived rows.</summary>
    private async Task CaptureCurrentFixtureAsync(string scenario, string stage, IServiceProvider services, string executionId)
    {
        if (Environment.GetEnvironmentVariable("ELSA_BPMN_CURRENT_FIXTURE_EVIDENCE") is not { Length: > 0 } root) return;
        var output = Path.Combine(root, scenario);
        Directory.CreateDirectory(output);
        DirectoryInfo? repository = new(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "Elsa.Server.slnx"))) repository = repository.Parent;
        Assert.NotNull(repository);
        var sourceSha = await GitAsync("rev-parse", "HEAD");
        Assert.Equal(string.Empty, await GitAsync("status", "--porcelain"));
        var names = new HashSet<string>(["Elsa.Activities.Bpmn.Tests", "Elsa.Activities.Bpmn", "Elsa.Workflows.Runtime.Core",
            "Elsa.Workflows.Runtime", "Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore", "Bpmn.Model", "Bpmn.Semantics"], StringComparer.Ordinal);
        var assemblies = AppDomain.CurrentDomain.GetAssemblies().Where(assembly => names.Contains(assembly.GetName().Name!)).ToArray();
        Assert.Equal(names.Count, assemblies.Length);
        var binaryHashes = assemblies.ToDictionary(assembly => Path.GetFileName(assembly.Location),
            assembly => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(assembly.Location))));
        var binaryVersions = assemblies.ToDictionary(assembly => Path.GetFileName(assembly.Location),
            assembly => assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? string.Empty);
        Assert.EndsWith("+" + sourceSha, binaryVersions["Elsa.Activities.Bpmn.Tests.dll"]);
        var snapshot = Path.Combine(output, stage + ".sqlite");
        Assert.False(File.Exists(snapshot), "Use a new fixture capture directory.");
        await using (var source = new SqliteConnection($"Data Source={database};Pooling=False"))
        await using (var destination = new SqliteConnection($"Data Source={snapshot};Pooling=False"))
        {
            await source.OpenAsync();
            await destination.OpenAsync();
            source.BackupDatabase(destination);
        }
        var bytes = await File.ReadAllBytesAsync(snapshot);
        await File.WriteAllTextAsync(Path.Combine(output, stage + ".json"), JsonSerializer.Serialize(new
        {
            captureProvenance = new { version = 1, sourceSha, sourceDirty = false,
                databaseSha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)), databaseByteLength = bytes.Length, binaryHashes, binaryVersions },
            capturedAt = DateTimeOffset.UtcNow, scenario, stage, executionId,
            state = await services.GetRequiredService<IRuntimeModelExecutionReader>().FindAsync(executionId),
            native = await services.GetRequiredService<IWorkflowExecutionStateStore>().ListAllAsync(),
            modelQueue = (await services.GetRequiredService<IWorkflowSchedulerWorkQueue>().ListAsync(new(executionId))).Items
        }, Web));

        async Task<string> GitAsync(params string[] arguments)
        {
            var start = new ProcessStartInfo("git") { WorkingDirectory = repository.FullName, UseShellExecute = false,
                RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var text = await process.StandardOutput.ReadToEndAsync();
            var errors = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.True(process.ExitCode == 0, errors);
            return text.Trim();
        }
    }
}
