using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;

namespace Elsa.Secrets.Workflows.Tests.Support;

/// <summary>One request handed to the Secrets module's resolver.</summary>
public sealed record RecordedSecretResolution(string TenantId, SecretReference Reference, CancellationToken CancellationToken);

/// <summary>Records every request handed to the Secrets module's resolver.</summary>
public sealed class SecretResolutionRecorder
{
    private readonly List<RecordedSecretResolution> _requests = [];

    public IReadOnlyList<RecordedSecretResolution> Requests
    {
        get
        {
            lock (_requests)
                return _requests.ToArray();
        }
    }

    public void Add(RecordedSecretResolution request)
    {
        lock (_requests)
            _requests.Add(request);
    }
}

/// <summary>Records the request and returns the configured test answer.</summary>
public sealed class RecordingSecretValueResolver(SecretResolutionRecorder recorder) : ISecretValueResolver
{
    public ResolvedSecret? Answer { get; set; } = ResolvedSecret.Success("value", new SecretMetadata());

    public ValueTask<ResolvedSecret> ResolveAsync(string tenantId, SecretReference reference, CancellationToken cancellationToken = default)
    {
        recorder.Add(new(tenantId, reference, cancellationToken));
        return ValueTask.FromResult(Answer!);
    }
}
