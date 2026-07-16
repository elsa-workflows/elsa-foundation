using System.Text.Json;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;

namespace Elsa.Expressions.Services;

/// <summary>Routes a portable expression to its constructor-injected language handler.</summary>
public sealed class PortableExpressionEvaluator : IPortableExpressionEvaluator
{
    private readonly IReadOnlyDictionary<string, IPortableExpressionHandler> _handlers;

    public PortableExpressionEvaluator(IEnumerable<IPortableExpressionHandler> handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        var snapshot = new Dictionary<string, IPortableExpressionHandler>(StringComparer.Ordinal);
        foreach (var handler in handlers)
        {
            ArgumentNullException.ThrowIfNull(handler);
            ArgumentException.ThrowIfNullOrWhiteSpace(handler.Language);
            if (!snapshot.TryAdd(handler.Language, handler))
                throw new InvalidOperationException($"More than one portable expression handler is registered for language '{handler.Language}'.");
        }

        _handlers = snapshot;
    }

    public async ValueTask<JsonElement> EvaluateAsync(ExpressionEvaluationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.CancellationToken.ThrowIfCancellationRequested();
        if (!request.Capabilities.IsBindingPure ||
            !StringComparer.Ordinal.Equals(request.Capabilities.Profile, ExpressionCapabilityProfiles.BindingPureV1))
        {
            throw new InvalidOperationException(
                $"Portable expression evaluation requires capability profile '{ExpressionCapabilityProfiles.BindingPureV1}'. " +
                $"Profile '{request.Definition.CapabilityProfile}' belongs to an explicitly separate evaluator path.");
        }

        if (!_handlers.TryGetValue(request.Definition.Language, out var handler))
            throw new InvalidOperationException($"No portable expression handler is registered for language '{request.Definition.Language}'.");

        return (await handler.EvaluateAsync(request)).Clone();
    }
}
