namespace Elsa.Rules.Core;

/// <summary>Replacement contract: exactly one trusted installed semantic identity source per host composition.</summary>
/// <remarks>
/// The source verifies the deployed package/asset receipt. Null means unavailable, never permission to use
/// a supplied pin or infer the latest version. An existing invocation retains its original expected pin.
/// </remarks>
public interface IRuleImplementationIdentitySource
{
    ValueTask<RuleSemanticPin?> ReadAsync(CancellationToken cancellationToken = default);
}
