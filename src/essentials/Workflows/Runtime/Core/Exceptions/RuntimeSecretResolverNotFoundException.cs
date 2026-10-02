using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.Core.Exceptions;

/// <summary>
/// An activity input takes a secret reference, but the host composes no <see cref="IRuntimeSecretResolver"/>. This is a
/// deployment compatibility failure, not a resolution failure: the activity waits with an activation-failure incident
/// until a feature that provides the resolver is composed, instead of faulting.
/// </summary>
public sealed class RuntimeSecretResolverNotFoundException(string inputKey)
    : Exception($"Activity input '{inputKey}' takes a secret reference, but no {nameof(IRuntimeSecretResolver)} is composed in this host.")
{
    public string InputKey { get; } = inputKey;
}
