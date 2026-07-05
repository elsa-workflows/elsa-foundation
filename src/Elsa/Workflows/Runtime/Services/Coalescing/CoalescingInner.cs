namespace Elsa.Workflows.Runtime.Core.Services.Coalescing;

/// <summary>
/// Typed holder capturing the pre-decoration (durable) implementation of a runtime store contract so both the
/// coalescing decorator and the coalescing session can reach the inner store without re-resolving the decorated
/// registration (which would recurse). Registered by the opt-in coalescing wiring; never used on the default path.
/// </summary>
public sealed class CoalescingInner<TService>(TService value) where TService : class
{
    public TService Value { get; } = value ?? throw new ArgumentNullException(nameof(value));
}
