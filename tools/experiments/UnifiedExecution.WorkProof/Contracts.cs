using System.Text;
using System.Text.Json;
using System.Security.Cryptography;

namespace Elsa.UnifiedExecution.WorkProof;

/// <summary>Identity and authorization scope carried by every durable operation.</summary>
public sealed record WorkScope(string TenantId, string PersistenceScope, string Authority, string Capability)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(this.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(this.PersistenceScope);
        ArgumentException.ThrowIfNullOrWhiteSpace(this.Authority);
    }
}

public interface IWorkScopeAuthorizer
{
    void EnsureAuthorized(WorkScope scope);
}

/// <summary>Local proof credential issuer; this is an experiment boundary, not a production identity provider.</summary>
public sealed class HmacWorkScopeAuthorizer(string key) : IWorkScopeAuthorizer
{
    private readonly byte[] _key = Encoding.UTF8.GetBytes(string.IsNullOrWhiteSpace(key) ? throw new ArgumentException("A proof key is required.", nameof(key)) : key);

    public WorkScope Issue(string tenantId, string persistenceScope, string authority)
    {
        var unsigned = new WorkScope(tenantId, persistenceScope, authority, "");
        unsigned.Validate();
        return unsigned with { Capability = Sign(unsigned) };
    }

    public void EnsureAuthorized(WorkScope scope)
    {
        scope.Validate();
        byte[] supplied;
        try { supplied = Convert.FromHexString(scope.Capability); }
        catch (FormatException) { throw new UnauthorizedAccessException("Scope capability is malformed."); }
        byte[] expected = Convert.FromHexString(Sign(scope with { Capability = "" }));
        if (!CryptographicOperations.FixedTimeEquals(expected, supplied))
            throw new UnauthorizedAccessException("Scope capability does not authorize this tenant, persistence scope, and authority.");
    }

    private string Sign(WorkScope scope)
    {
        string value = $"{scope.TenantId}\n{scope.PersistenceScope}\n{scope.Authority}";
        return Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }
}

/// <summary>A pinned unit of work. InputJson is the immutable dispatch snapshot.</summary>
public sealed record WorkDefinition(string InvocationId, string HandlerId, string HandlerVersion, string InputJson);

/// <summary>Fenced ownership of one work invocation.</summary>
public sealed record WorkLease(
    WorkScope Scope,
    string ExecutionId,
    string InvocationId,
    string Owner,
    long Fence,
    int Attempt,
    string HandlerId,
    string HandlerVersion,
    string InputJson);

/// <summary>Durable result accepted from a worker, including conflicting/late outcomes.</summary>
public sealed record WorkReceipt(string InvocationId, string Status, string ResultJson, string Fingerprint, bool Duplicate = false);

/// <summary>Explicit component decision to persist state, dispatch work, queue continuations, or finish.</summary>
public sealed record CompositionPlan(
    string StateJson,
    IReadOnlyList<WorkDefinition> Work,
    IReadOnlyList<string> Continuations,
    bool IsComplete);

/// <summary>Composition owns its joins and completion decision; the store persists the decision.</summary>
public interface ICompositionPolicy
{
    string Id { get; }
    CompositionPlan Start(string inputJson);
    CompositionPlan ApplyResult(string stateJson, WorkReceipt receipt);
    CompositionPlan Continue(string stateJson, string continuationId);
}

public sealed record CommitReceipt(string CommitId, string Fingerprint, string StateJson, string Status, IReadOnlyList<string> WorkIds, IReadOnlyList<string> ContinuationIds);

internal static class JsonSupport
{
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value);
    public static T Deserialize<T>(string value) => JsonSerializer.Deserialize<T>(value) ?? throw new InvalidDataException($"Could not deserialize {typeof(T).Name}.");
    public static string Fingerprint(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
