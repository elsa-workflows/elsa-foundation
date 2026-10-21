using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Design;
using Elsa.Etl.Design.Stores;
using Elsa.Etl.Workflows.Runtime.Activities;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Etl.Workflows.Design.Services;

/// <summary>Embeds exact published ETL artifact envelopes into native RunEtl node metadata.</summary>
internal sealed class EtlPublishedArtifactPinSource(
    IEtlDefinitionStore definitionStore,
    IEtlArtifactCompiler artifactCompiler,
    IPersistenceAccessContextAccessor accessContextAccessor) : IExecutableCompilationSource
{
    private const string PinMetadataKey = "etl.pipeline.v1";
    private const string PipelineInputKey = nameof(RunEtl.Pipeline);
    private static readonly JsonSerializerOptions ArtifactJsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public async ValueTask<ExecutableCompilationContribution> GetContributionAsync(
        ExecutableCompilationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var nodes = context.RootActivity.DescendantsAndSelf()
            .Where(node => StringComparer.Ordinal.Equals(node.ActivityType, RunEtl.ActivityType))
            .OrderBy(node => node.ExecutableNodeId, StringComparer.Ordinal)
            .ToArray();
        if (nodes.Length == 0)
            return new ExecutableCompilationContribution();

        var persistenceScope = accessContextAccessor.Current.RequireScope();
        var scope = new EtlScope(context.TenantId, persistenceScope.Value);
        var claims = new List<ExecutableNodeMetadataClaim>(nodes.Length);
        foreach (var node in nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var selection = ReadSelection(node);
            var artifact = await definitionStore.FindPublishedAsync(
                    scope,
                    selection.DefinitionId,
                    selection.DefinitionVersion,
                    cancellationToken)
                ?? throw Refused(node, "could not resolve its exact published ETL artifact.");

            var serializedArtifact = ValidateAndSerialize(artifact, selection, node);
            claims.Add(new(node.ExecutableNodeId, PinMetadataKey, serializedArtifact));
        }

        return new ExecutableCompilationContribution(nodeMetadata: claims);
    }

    private static EtlPublishedSelection ReadSelection(ExecutableNode node)
    {
        var binding = node.InputBindings.GetValueOrDefault(PipelineInputKey);
        SecretBindingDiagnostics.ThrowIfSecretRead(binding, node.ExecutableNodeId, PipelineInputKey);
        if (binding is null ||
            binding.Source != RuntimeInputBindingSource.Literal ||
            binding.LiteralValue is not { ValueKind: JsonValueKind.String } literal ||
            string.IsNullOrEmpty(literal.GetString()))
            throw Refused(node, $"requires a literal '{PipelineInputKey}' published ETL selection.");

        try
        {
            return EtlPublishedSelection.Parse(literal.GetString()!);
        }
        catch (FormatException exception)
        {
            throw Refused(node, $"requires a valid literal '{PipelineInputKey}' published ETL selection.", exception);
        }
    }

    private string ValidateAndSerialize(
        EtlArtifact artifact,
        EtlPublishedSelection selection,
        ExecutableNode node)
    {
        if (artifact.FormatVersion != EtlArtifact.CurrentFormatVersion ||
            string.IsNullOrEmpty(artifact.DefinitionId) || artifact.DefinitionId.Length > 256 || artifact.DefinitionVersion <= 0 ||
            string.IsNullOrEmpty(artifact.ArtifactId) || artifact.ArtifactId.Length > 96 ||
            string.IsNullOrEmpty(artifact.ContentHash) || artifact.ContentHash.Length != 64 ||
            artifact.Pipeline.ValueKind != JsonValueKind.Object ||
            JsonMarshal.GetRawUtf8Value(artifact.Pipeline).Length > EtlArtifact.MaximumByteLength)
            throw Refused(node, "resolved an invalid or oversized published ETL artifact.");

        EtlArtifact? compiled;
        try
        {
            var pipeline = EtlArtifactJson.Read(artifact.Pipeline);
            compiled = artifactCompiler.Compile(selection.DefinitionId, selection.DefinitionVersion, pipeline).Artifact;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or JsonException or
                                          NotSupportedException or NullReferenceException or OverflowException)
        {
            throw Refused(node, "resolved an invalid published ETL artifact.", exception);
        }

        if (compiled is null ||
            !StringComparer.Ordinal.Equals(artifact.DefinitionId, selection.DefinitionId) ||
            artifact.DefinitionVersion != selection.DefinitionVersion ||
            !StringComparer.Ordinal.Equals(artifact.ContentHash, selection.ContentHash) ||
            !StringComparer.Ordinal.Equals(artifact.ArtifactId, compiled.ArtifactId) ||
            !StringComparer.Ordinal.Equals(artifact.ContentHash, compiled.ContentHash) ||
            !StringComparer.Ordinal.Equals(artifact.Pipeline.GetRawText(), compiled.Pipeline.GetRawText()))
            throw Refused(node, "resolved a published ETL artifact that does not match the selected version and content hash.");

        var serialized = JsonSerializer.Serialize(artifact, ArtifactJsonOptions);
        if (Encoding.UTF8.GetByteCount(serialized) > EtlArtifact.MaximumByteLength)
            throw Refused(node, "resolved an oversized published ETL artifact.");
        return serialized;
    }

    private static ArgumentException Refused(ExecutableNode node, string reason, Exception? innerException = null) =>
        new($"RunEtl node '{node.ExecutableNodeId}' {reason}", innerException);
}
