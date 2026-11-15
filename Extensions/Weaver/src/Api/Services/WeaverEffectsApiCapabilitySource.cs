using System.Reflection;
using CShells.Features;
using Elsa.Api.Capabilities.Contracts;
using Elsa.Api.Capabilities.Models;
using Elsa.Weaver.Services;
using Elsa.Weaver.Core.Models;

namespace Elsa.Weaver.Api;

/// <summary>Projects the selected effects feature's shared admission without gating the legacy Weaver API.</summary>
internal sealed class WeaverEffectsApiCapabilitySource(WeaverRegistrationValidator registrations, WeaverEffectsAdmission effects)
    : IApiCapabilitySource
{
    /// <exception cref="WeaverContributionException">Current effects availability could not be established.</exception>
    public async ValueTask<IReadOnlyCollection<ApiCapabilityDeclaration>> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var selection = registrations.GetEffectsSelection();
        if (selection is null) return [];
        var pending = effects.CheckAsync(cancellationToken).AsTask();
        try
        {
            var refusal = await pending.WaitAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var source = selection.FeatureType.GetCustomAttribute<ShellFeatureAttribute>(inherit: true)?.Name
                ?? selection.FeatureType.FullName ?? selection.FeatureType.Name;
            return [new("elsa.api.weaver.effects", 1,
                [new("weaver-bootstrap", "_elsa/weaver/bootstrap"), new("weaver-proposals", "_elsa/weaver/sessions/{sessionId}/proposals", true)], source)
                { DormantReason = refusal?.Message }];
        }
        catch (Exception cause) when (cause is not (OperationCanceledException or WeaverContributionException))
        {
            throw new WeaverContributionException("Weaver effects API capability 'elsa.api.weaver.effects' could not establish current admission.", cause);
        }
        finally { _ = ObserveAsync(pending); }
    }

    private static async Task ObserveAsync(Task pending)
    {
        try { await pending; }
        catch (Exception) { }
    }
}
