using CShells.Lifecycle;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Jint.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Expressions.JavaScript.Jint.Services;

/// <summary>The service collection the JavaScript engine was composed in, read again at shell activation.</summary>
internal sealed record JavaScriptValueComposition(IServiceCollection Services);

/// <summary>
/// Fails shell activation when the host composes more than one <see cref="IScriptValueBridge"/> or
/// <see cref="IJavaScriptPrecisionPolicy"/> (both <see cref="JavaScriptValueReplacementContractAttribute"/>), naming every
/// registration, whatever order they were registered in. The engine registers the defaults with <c>TryAdd</c>, so a host
/// that replaces one with <c>services.Replace(...)</c>, or registers its own first, composes one; a plain second
/// <c>AddSingleton</c> would otherwise win silently by registration order (framework §2.6.2).
/// </summary>
internal sealed class JavaScriptValueCompositionValidator(JavaScriptValueComposition composition) : IShellInitializer
{
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Ensure<IScriptValueBridge>();
        Ensure<IJavaScriptPrecisionPolicy>();
        return Task.CompletedTask;
    }

    private void Ensure<TContract>()
    {
        var registrations = composition.Services
            .Where(descriptor => descriptor.ServiceType == typeof(TContract) && !descriptor.IsKeyedService)
            .Select(Describe)
            .ToArray();
        if (registrations.Length > 1)
            throw new InvalidOperationException(
                $"More than one '{typeof(TContract).Name}' is registered: {string.Join(", ", registrations)}. It is a replacement contract: " +
                "replace Elsa's default with services.Replace(...) instead of adding a second registration.");
    }

    private static string Describe(ServiceDescriptor descriptor) =>
        descriptor.ImplementationType?.FullName is { } implementationType
            ? $"'{implementationType}'"
            : descriptor.ImplementationInstance?.GetType().FullName is { } instanceType
                ? $"an instance of '{instanceType}'"
                : "a factory registration";

    /// <summary>Registers the check once per service collection, in the Prepare phase.</summary>
    internal static void Register(IServiceCollection services)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(JavaScriptValueComposition)))
            return;

        services.AddSingleton(new JavaScriptValueComposition(services));
        services.AddShellInitializer<JavaScriptValueCompositionValidator>(LifecyclePhase.Prepare, 0);
    }
}
