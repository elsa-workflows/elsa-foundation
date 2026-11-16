using Elsa.Decisions.Core;
using Elsa.Decisions.Persistence.Core;
using Elsa.Decisions.Workflows.Runtime;
using Elsa.Decisions.Workflows.Runtime.Activities;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Decisions.Workflows.Design;

internal sealed class DecisionPinSource(IDecisionReader decisions, IDecisionCompiler compiler, IDecisionPublicationAuthorizer authorization,
    IPersistenceAccessContextAccessor access) : IExecutableCompilationSource
{
    public async ValueTask<ExecutableCompilationContribution> GetContributionAsync(ExecutableCompilationContext context,
        CancellationToken cancellationToken = default)
    {
        var claims = new List<ExecutableNodeMetadataClaim>();
        var nodes = context.RootActivity.DescendantsAndSelf().Where(node => node.ActivityType == EvaluateDecision.ActivityType).ToArray();
        if (nodes.Length == 0) return new();
        var scope = access.Current.RequireScope();
        access.Current.EnsureTenantScope(context.TenantId);
        var tenant = context.TenantId ?? scope.Value;
        foreach (var node in nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var binding = node.InputBindings.GetValueOrDefault(nameof(EvaluateDecision.Decision));
            SecretBindingDiagnostics.ThrowIfSecretRead(binding, node.ExecutableNodeId, nameof(EvaluateDecision.Decision));
            if (binding?.Source != RuntimeInputBindingSource.Literal || binding.LiteralValue is not { ValueKind: System.Text.Json.JsonValueKind.String } literal ||
                literal.GetString() is not { Length: > 0 and <= 512 } selection) throw Invalid();
            var separator = selection.LastIndexOf('@');
            if (separator <= 0 || separator == selection.Length - 1) throw Invalid();
            var assetId = selection[..separator];
            var versionId = selection[(separator + 1)..];
            await authorization.EnsureAsync(tenant, assetId, cancellationToken);
            if (versionId == "latest")
                versionId = (await decisions.FindAssetAsync(tenant, assetId, cancellationToken))?.PublishedVersionId ?? throw Invalid();
            var version = await decisions.FindVersionAsync(tenant, assetId, versionId, cancellationToken) ?? throw Invalid();
            if (version.AssetId != assetId || version.Id != versionId || version.IsSensitive ||
                node.InputBindings.Values.Any(input => input.EffectivePolicy.HidesValue()))
                throw new DecisionInvocationException("protected-binding-unsupported", "This decision binding cannot preserve the required workflow source or value protection.");
            var compiled = compiler.Compile(version.Xml, cancellationToken);
            if (!compiled.Success || compiled.Artifact is not { } artifact || artifact.ContentHash != version.Artifact.ContentHash ||
                artifact.SourceHash != version.SourceHash || artifact.SourceHash != version.Artifact.SourceHash ||
                artifact.DecisionId != version.Artifact.DecisionId || artifact.ProfileId != version.Artifact.ProfileId ||
                artifact.SemanticsId != version.Artifact.SemanticsId ||
                !artifact.ExecutableBytes.SequenceEqual(version.Artifact.ExecutableBytes)) throw Invalid();
            var pin = new DecisionWorkflowPin(selection, tenant, assetId, versionId, artifact);
            claims.Add(new(node.ExecutableNodeId, DecisionWorkflowPin.MetadataKey, pin.Serialize()));
        }
        return new(nodeMetadata: claims);
    }

    private static DecisionInvocationException Invalid() => new("invalid-selection", "An exact supported published decision must be selected as asset@version or asset@latest.");
}
