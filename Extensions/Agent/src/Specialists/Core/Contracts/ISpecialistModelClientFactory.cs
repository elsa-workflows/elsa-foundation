using Elsa.Agent.Specialists.Core.Models;
using Microsoft.Extensions.AI;

namespace Elsa.Agent.Specialists.Core.Contracts;

/// <summary>
/// Trusted model-provider contribution. Implementations resolve the existing operational connection,
/// enforce current scope/resource access and revocation at point of use, and never infer authority from payload fields.
/// </summary>
public interface ISpecialistModelClientFactory
{
    /// <summary>The exact provider key this contribution owns; duplicate registrations are refused.</summary>
    string ProviderKey { get; }

    /// <summary>
    /// Revalidates the native identity against current access and opens a client without issuing inference.
    /// Options reflect the validated published settings; the runtime owns model, schema, limits and tool admission.
    /// </summary>
    ValueTask<SpecialistModelClientLease> OpenAsync(SpecialistModelOpenRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Exact immutable model request plus native provenance for point-of-use authorization.</summary>
public sealed record SpecialistModelOpenRequest(SpecialistRuntimeDescriptor Descriptor, SpecialistInvocationIdentity Invocation);

/// <summary>A transient owned client and options. It is never serialized into workflow or journal state.</summary>
public sealed class SpecialistModelClientLease(
    IChatClient client,
    ChatOptions options,
    string capabilitiesFingerprint,
    int connectionVersion,
    IAsyncDisposable? ownedScope = null) : IAsyncDisposable
{
    private int disposed;

    public IChatClient Client { get; } = client ?? throw new ArgumentNullException(nameof(client));
    public ChatOptions Options { get; } = options ?? throw new ArgumentNullException(nameof(options));
    public string CapabilitiesFingerprint { get; } = capabilitiesFingerprint;
    public int ConnectionVersion { get; } = connectionVersion;

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        try { Client.Dispose(); }
        finally
        {
            if (ownedScope is not null) await ownedScope.DisposeAsync();
        }
    }
}
