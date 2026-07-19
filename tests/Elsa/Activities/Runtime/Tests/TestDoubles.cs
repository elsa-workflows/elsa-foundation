using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>Serializes process-global console redirection for console-parity activity tests.</summary>
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

/// <summary>Seeds an artifact and its published source reference for public execute-boundary tests.</summary>
internal static class PublishedExecutableSeeder
{
    public static async ValueTask SaveAsync(IServiceProvider services, WorkflowExecutable executable)
    {
        var identity = executable.Identity;
        await services.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(executable);
        await services.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().SaveAsync(
            new WorkflowExecutableSourceReference(
                $"published:{identity.ArtifactId}",
                identity.ArtifactId,
                "WorkflowDefinitionVersion",
                identity.DefinitionVersionId,
                identity.ArtifactVersion,
                identity.DefinitionId,
                identity.DefinitionVersionId,
                identity.ArtifactVersion,
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch,
                WorkflowExecutableReferenceScope.Published));
    }
}
