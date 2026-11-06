using System.Runtime.InteropServices;
using System.Text.Json;
using Elsa.Jobs.Core;
using Elsa.Jobs.Workflows.Runtime.Activities;
using Elsa.Jobs.Workflows.Runtime.Models;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Jobs.Workflows.Design.Services;

/// <summary>Contributes immutable logical selections without resolving a worker's CLR handler.</summary>
internal sealed class JobsHandlerPinSource : IExecutableCompilationSource
{
    private const int MaximumLiteralBytes = 32_768;
    private static readonly JsonSerializerOptions LiteralJson = new(JsonSerializerDefaults.Web) { MaxDepth = 16 };

    public ValueTask<ExecutableCompilationContribution> GetContributionAsync(
        ExecutableCompilationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        var claims = new List<ExecutableNodeMetadataClaim>();
        foreach (var node in context.RootActivity.DescendantsAndSelf()
                     .Where(node => StringComparer.Ordinal.Equals(node.ActivityType, RunJob.ActivityType) ||
                         StringComparer.Ordinal.Equals(node.ActivityType, SubmitJob.ActivityType))
                     .OrderBy(node => node.ExecutableNodeId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var handler = ReadLiteral<JobHandlerIdentity>(node, nameof(RunJob.Handler));
            var inputSchema = ReadLiteral<JobSchema>(node, nameof(RunJob.InputSchema));
            var resultSchema = ReadLiteral<JobSchema>(node, nameof(RunJob.ResultSchema));
            var queue = ReadLiteral<string>(node, nameof(RunJob.Queue));
            var policy = ReadLiteral<JobExecutionPolicy>(node, nameof(RunJob.Policy));
            var mode = ReadLiteral<JobAssociationMode>(node, nameof(RunJob.AssociationMode));
            try
            {
                if (node.ActivityType == RunJob.ActivityType ? mode != JobAssociationMode.Awaited
                    : mode is not (JobAssociationMode.Independent or JobAssociationMode.Detached)) throw new JobException("jobs.native.invalid", "Unsupported Jobs activity association mode.");
                var pin = new JobWorkflowHandlerPin(1, handler, inputSchema, resultSchema, queue, policy, mode);
                claims.Add(new(node.ExecutableNodeId, RunJob.PinMetadataKey, pin.Serialize()));
            }
            catch (JobException)
            {
                throw Refused(node, "requires valid bounded handler, schema, queue and policy selections with a supported association mode.");
            }
        }

        return ValueTask.FromResult(new ExecutableCompilationContribution(nodeMetadata: claims));
    }

    private static T ReadLiteral<T>(ExecutableNode node, string inputKey)
    {
        var binding = node.InputBindings.GetValueOrDefault(inputKey);
        SecretBindingDiagnostics.ThrowIfSecretRead(binding, node.ExecutableNodeId, inputKey);
        if (binding?.Source != RuntimeInputBindingSource.Literal ||
            binding.Literal is not { Presence: ValuePresence.Present } || binding.LiteralValue is not { } literal)
            throw Refused(node, $"requires a literal '{inputKey}' selection.");
        if (JsonMarshal.GetRawUtf8Value(literal).Length > MaximumLiteralBytes)
            throw Refused(node, $"has an oversized '{inputKey}' selection.");

        try
        {
            return literal.Deserialize<T>(LiteralJson)!;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException)
        {
            throw Refused(node, $"requires a valid bounded '{inputKey}' literal selection.");
        }
    }

    private static ArgumentException Refused(ExecutableNode node, string reason) =>
        new($"{(node.ActivityType == SubmitJob.ActivityType ? nameof(SubmitJob) : nameof(RunJob))} node '{node.ExecutableNodeId}' {reason}");
}
