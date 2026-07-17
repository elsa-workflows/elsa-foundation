using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimePostCommitIntentContributionTests
{
    private const string MarkerKind = "Test.Marker";

    [Fact]
    public void AddRuntimePostCommitIntentHandler_RepeatedIdenticalContributionIsIdempotent()
    {
        var services = new ServiceCollection();

        services.AddRuntimePostCommitIntentHandler<FirstMarkerHandler>(MarkerKind);
        services.AddRuntimePostCommitIntentHandler<FirstMarkerHandler>(MarkerKind);

        var contribution = Assert.Single(services
            .Where(descriptor => descriptor.ServiceType == typeof(RuntimePostCommitIntentHandlerContribution))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<RuntimePostCommitIntentHandlerContribution>());
        Assert.Equal(MarkerKind, contribution.IntentKind);
        Assert.Equal(typeof(FirstMarkerHandler), contribution.HandlerType);
        Assert.Same(RuntimePostCommitRetryPolicy.None, contribution.RetryPolicy);
        Assert.Single(services.Where(descriptor => descriptor.ServiceType == typeof(FirstMarkerHandler)));
    }

    [Fact]
    public void PolicyBearingRegistration_IsIdempotentAndRejectsPolicyConflict()
    {
        var services = new ServiceCollection();
        var retry = RuntimePostCommitRetryPolicy.UntilAcknowledged(TimeSpan.FromSeconds(5));

        services.AddRuntimePostCommitIntentHandler<FirstMarkerHandler>(MarkerKind, retry);
        services.AddRuntimePostCommitIntentHandler<FirstMarkerHandler>(MarkerKind, RuntimePostCommitRetryPolicy.UntilAcknowledged(TimeSpan.FromSeconds(5)));

        var contribution = Assert.Single(services
            .Where(descriptor => descriptor.ServiceType == typeof(RuntimePostCommitIntentHandlerContribution))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<RuntimePostCommitIntentHandlerContribution>());
        Assert.True(contribution.RetryPolicy.RetryUntilAcknowledged);
        Assert.Throws<InvalidOperationException>(() =>
            services.AddRuntimePostCommitIntentHandler<FirstMarkerHandler>(MarkerKind, RuntimePostCommitRetryPolicy.None));
    }

    [Fact]
    public void AddRuntimePostCommitIntentHandler_ConflictsFailDeterministicallyWithSortedIdentities()
    {
        var firstOrder = CaptureConflict<FirstMarkerHandler, SecondMarkerHandler>();
        var secondOrder = CaptureConflict<SecondMarkerHandler, FirstMarkerHandler>();

        Assert.Equal(firstOrder.Message, secondOrder.Message);
        Assert.Contains(MarkerKind, firstOrder.Message);
        Assert.Contains(typeof(FirstMarkerHandler).FullName!, firstOrder.Message);
        Assert.Contains(typeof(SecondMarkerHandler).FullName!, firstOrder.Message);
    }

    [Fact]
    public void RuntimePostCommitIntentDispatcher_DefensivelyRevalidatesManuallyRegisteredContributions()
    {
        var services = new ServiceCollection();
        services.AddScoped<FirstMarkerHandler>();
        services.AddScoped<SecondMarkerHandler>();
        services.AddSingleton(new RuntimePostCommitIntentHandlerContribution(MarkerKind, typeof(SecondMarkerHandler)));
        services.AddSingleton(new RuntimePostCommitIntentHandlerContribution(MarkerKind, typeof(FirstMarkerHandler)));
        services.AddScoped<IRuntimePostCommitIntentDispatcher, RuntimePostCommitIntentDispatcher>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var exception = Assert.Throws<InvalidOperationException>(
            () => scope.ServiceProvider.GetRequiredService<IRuntimePostCommitIntentDispatcher>());

        Assert.Contains(MarkerKind, exception.Message);
        Assert.True(
            exception.Message.IndexOf(typeof(FirstMarkerHandler).FullName!, StringComparison.Ordinal) <
            exception.Message.IndexOf(typeof(SecondMarkerHandler).FullName!, StringComparison.Ordinal));
    }

    [Fact]
    public void RegistrationOwnsIntentKindAndRejectsBlankKinds()
    {
        Assert.Null(typeof(IRuntimePostCommitIntentHandler).GetProperty("Kind"));

        var services = new ServiceCollection();
        Assert.Throws<ArgumentException>(() =>
            services.AddRuntimePostCommitIntentHandler<FirstMarkerHandler>(" "));
    }

    private static InvalidOperationException CaptureConflict<TFirst, TSecond>()
        where TFirst : class, IRuntimePostCommitIntentHandler
        where TSecond : class, IRuntimePostCommitIntentHandler
    {
        var services = new ServiceCollection();
        services.AddRuntimePostCommitIntentHandler<TFirst>(MarkerKind);
        return Assert.Throws<InvalidOperationException>(() =>
            services.AddRuntimePostCommitIntentHandler<TSecond>(MarkerKind));
    }

    private sealed class FirstMarkerHandler : IRuntimePostCommitIntentHandler
    {
        public ValueTask HandleAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class SecondMarkerHandler : IRuntimePostCommitIntentHandler
    {
        public ValueTask HandleAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }
}
