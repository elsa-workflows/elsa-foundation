using System.Text.Json;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>A constructor test-double returning a fixed activity. Used to assert registry + factory dispatch.</summary>
internal sealed class FakeConstructorA(string consumerKey, params string[] schemaVersions) : IActivityConstructor
{
    public IActivity Returned { get; } = new WriteLine();
    public string ConsumerKey { get; } = consumerKey;
    public IReadOnlySet<string> SupportedSchemaVersions { get; } = new HashSet<string>(schemaVersions.Length == 0 ? [RuntimeActivityDescriptor.InitialSchemaVersion] : schemaVersions, StringComparer.Ordinal);

    public ValueTask<IActivity> ConstructAsync(
        RuntimeActivityDescriptor descriptor,
        IReadOnlyDictionary<string, InputArgument> inputs,
        IReadOnlyDictionary<string, OutputArgument> outputs,
        CancellationToken cancellationToken) => new(Returned);
}

/// <summary>A second, distinct constructor type with the same descriptor type — to trip the dup-guard.</summary>
internal sealed class FakeConstructorB(string consumerKey, params string[] schemaVersions) : IActivityConstructor
{
    public string ConsumerKey { get; } = consumerKey;
    public IReadOnlySet<string> SupportedSchemaVersions { get; } = new HashSet<string>(schemaVersions.Length == 0 ? [RuntimeActivityDescriptor.InitialSchemaVersion] : schemaVersions, StringComparer.Ordinal);

    public ValueTask<IActivity> ConstructAsync(
        RuntimeActivityDescriptor descriptor,
        IReadOnlyDictionary<string, InputArgument> inputs,
        IReadOnlyDictionary<string, OutputArgument> outputs,
        CancellationToken cancellationToken) => new((IActivity)new WriteLine());
}

/// <summary>
/// Redirects <see cref="Console.Out"/> for the duration of an action and returns what was written.
/// Shared by the console-parity activity tests (WriteLine/WriteLines). <see cref="Console.SetOut"/> is
/// process-global, so callers must share the <c>"ConsoleCapture"</c> xUnit collection to serialize.
/// </summary>
internal static class ConsoleCapture
{
    public static async Task<string> RunAsync(Func<ValueTask> action)
    {
        var original = Console.Out;
        await using var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            await action();
        }
        finally
        {
            Console.SetOut(original);
        }

        return writer.ToString();
    }
}

/// <summary>
/// A minimal no-op <see cref="IActivity"/> used as a placeholder when constructing a
/// <c>SimpleActivityExecutionContext</c> for expression-evaluation tests that never actually execute the activity.
/// </summary>
internal sealed class TestActivity : IActivity
{
    public string Id { get; set; } = "activity-1";
    public string NodeId { get; set; } = "node-1";
    public string? Name { get; set; }
    public string Type { get; set; } = "Test.Activity";
    public string Version { get; set; } = "1.0.0";
    public Dictionary<string, object> CustomProperties { get; set; } = new();
    public Dictionary<string, object> SyntheticProperties { get; set; } = new();
    public Dictionary<string, object> Metadata { get; set; } = new();
    public ValueTask<bool> CanExecuteAsync(IActivityExecutionContext context) => new(true);
    public ValueTask ExecuteAsync(IActivityExecutionContext context) => ValueTask.CompletedTask;
}

/// <summary>Seeds an artifact and the live Published reference required by the public execute boundary.</summary>
internal static class PublishedExecutableSeeder
{
    public static async ValueTask SaveAsync(IServiceProvider services, WorkflowExecutable executable)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(executable);
        var identity = executable.Identity;
        await services.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(executable);
        await services.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().SaveAsync(
            new WorkflowExecutableSourceReference(
                SourceReferenceId: $"published:{identity.ArtifactId}",
                ArtifactId: identity.ArtifactId,
                SourceKind: "WorkflowDefinitionVersion",
                SourceId: identity.DefinitionVersionId,
                SourceVersion: identity.ArtifactVersion,
                DefinitionId: identity.DefinitionId,
                DefinitionVersionId: identity.DefinitionVersionId,
                ArtifactVersion: identity.ArtifactVersion,
                CreatedAt: DateTimeOffset.UnixEpoch,
                PublishedAt: DateTimeOffset.UnixEpoch,
                Scope: WorkflowExecutableReferenceScope.Published));
    }
}
