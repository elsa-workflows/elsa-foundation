using System.Collections.Concurrent;
using CShells.Features;
using Elsa.Activities.Design.Core.Models;
using Elsa3.Activities.Design.Import.Contracts;
using Elsa3.Activities.Design.Import.Publishing.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa3.Activities.Design.Import.Integration.Tests.Support;

/// <summary>The imported activity version ids the host's publish seam refuses; a test adds and clears them.</summary>
public sealed class PublishRefusals
{
    public const string Code = "test.publish.refused";

    private readonly ConcurrentDictionary<string, byte> _versionIds = new(StringComparer.Ordinal);

    private volatile bool _all;

    public void Refuse(string versionId) => _versionIds[versionId] = 0;

    /// <summary>Refuses every version, including those a folder import publishes while the shell activates (spec 235).</summary>
    public void RefuseAll() => _all = true;

    public void Clear()
    {
        _all = false;
        _versionIds.Clear();
    }

    public bool Refuses(string versionId) => _all || _versionIds.ContainsKey(versionId);

    /// <summary>The idempotency key of every publish request the seam saw, in order (spec 235 research R-4).</summary>
    public ConcurrentQueue<string> IdempotencyKeys { get; } = new();
}

/// <summary>
/// The import host's test seam: wraps the bridge's publish port so a version in <see cref="PublishRefusals"/> is refused
/// as the publisher refuses one, with <see cref="PublishRefusals.Code"/> and one diagnostic, and every other version is
/// published through the real bridge.
/// </summary>
[ShellFeature(Name, DependsOn = new object[] { "Elsa3ImportActivitiesPublishing" })]
public sealed class Elsa3ImportPublishSeamFeature : IShellFeature
{
    public const string Name = "Elsa3ImportPublishSeam";

    public void ConfigureServices(IServiceCollection services) =>
        services.Replace(ServiceDescriptor.Scoped<IReusableActivityImportPublisher>(provider => new RefusingPublisher(
            ActivatorUtilities.CreateInstance<PublisherReusableActivityImportPublisher>(provider),
            provider.GetRequiredService<PublishRefusals>())));

    private sealed class RefusingPublisher(IReusableActivityImportPublisher inner, PublishRefusals refusals) : IReusableActivityImportPublisher
    {
        public ValueTask<ReusableActivityImportPublishOutcome> PublishAsync(ReusableActivityImportPublishRequest request, CancellationToken cancellationToken = default)
        {
            refusals.IdempotencyKeys.Enqueue(request.IdempotencyKey);
            return Decide(request, cancellationToken);
        }

        private ValueTask<ReusableActivityImportPublishOutcome> Decide(ReusableActivityImportPublishRequest request, CancellationToken cancellationToken) =>
            refusals.Refuses(request.RequestedVersionId)
                ? ValueTask.FromResult(ReusableActivityImportPublishOutcome.Refused(PublishRefusals.Code,
                [
                    new(PublishRefusals.Code, ActivityDiagnosticSeverity.Error, "The test seam refuses this version.",
                        new ActivityDiagnosticSubject("ActivityDraft", request.DraftId))
                ]))
                : inner.PublishAsync(request, cancellationToken);
    }
}
