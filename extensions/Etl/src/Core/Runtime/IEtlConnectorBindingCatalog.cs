namespace Elsa.Etl.Core.Runtime;

/// <summary>Lists safe current host-approved connector aliases for one exact trusted scope.</summary>
/// <remarks>The v1 file catalog remains independent. Endpoints, credentials and revision handles are not authoring metadata.</remarks>
public interface IEtlConnectorBindingCatalog
{
    /// <summary>Returns at most <paramref name="take"/> entries in ordinal alias/role order after an exclusive pair.</summary>
    /// <remarks>The boundary pair must be both null or both present; take is between 1 and 1000. Current alias/role pairs are unique across connector kinds.</remarks>
    IReadOnlyList<EtlConnectorBindingSummary> ListBindings(EtlScope scope, string? afterAlias, string? afterRole, int take);
}

/// <summary>A current alias, its Source/Lookup/MainOutput/ErrorOutput role, and its connector kind.</summary>
public sealed record EtlConnectorBindingSummary(string Alias, string Role, EtlConnectorKind Kind);

/// <summary>Resolves current approved bindings and immutable configuration pins for a validated connector pipeline.</summary>
/// <remarks>Resolution is scoped and performs no source reads or output writes. Authenticated bindings require an explicit tenant; credentials are resolved separately per execution attempt.</remarks>
public interface IEtlConnectorBindingResolver
{
    ValueTask<EtlConnectorBindingResolution> ResolveAsync(EtlScope scope, EtlPipelineV2 pipeline,
        CancellationToken cancellationToken = default);
}

/// <summary>A complete immutable binding set, or a stable value-free failure code.</summary>
/// <remarks>Success has non-null bindings and a null failure code; refusal has null bindings and a non-null failure code.</remarks>
public sealed record EtlConnectorBindingResolution(EtlConnectorRunBindings? Bindings, string? FailureCode);
