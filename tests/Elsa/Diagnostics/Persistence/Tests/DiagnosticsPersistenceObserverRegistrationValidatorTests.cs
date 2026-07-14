using Elsa.Diagnostics.Persistence.Extensions;
using Elsa.Diagnostics.Persistence.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Diagnostics.Persistence.Tests;

public sealed class DiagnosticsPersistenceObserverRegistrationValidatorTests
{
    [Fact]
    public void Validate_WhenOneObserverIsRegistered_Succeeds()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDiagnosticsPersistenceObserver, FirstObserver>();
        var validator = new DiagnosticsPersistenceObserverRegistrationValidator(
            new DiagnosticsPersistenceObserverRegistrationState(services));

        var result = validator.Validate(
            Options.DefaultName,
            new DiagnosticsPersistenceObserverRegistrationOptions());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_WhenObserversConflict_FailsWithActionableDescriptions()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDiagnosticsPersistenceObserver, FirstObserver>();
        services.AddSingleton<IDiagnosticsPersistenceObserver, SecondObserver>();
        var validator = new DiagnosticsPersistenceObserverRegistrationValidator(
            new DiagnosticsPersistenceObserverRegistrationState(services));

        var result = validator.Validate(
            Options.DefaultName,
            new DiagnosticsPersistenceObserverRegistrationOptions());

        Assert.True(result.Failed);
        var message = Assert.Single(result.Failures);
        Assert.Contains(typeof(FirstObserver).FullName!, message, StringComparison.Ordinal);
        Assert.Contains(typeof(SecondObserver).FullName!, message, StringComparison.Ordinal);
        Assert.Contains(nameof(DiagnosticsPersistenceRegistration.ReplaceDiagnosticsStore), message, StringComparison.Ordinal);
    }

    private sealed class FirstObserver : TestObserver;
    private sealed class SecondObserver : TestObserver;

    private abstract class TestObserver : IDiagnosticsPersistenceObserver
    {
        public void RecordState(DiagnosticsDrainState state) { }
        public void RecordRetry(DiagnosticsPersistenceOperation operation, int attempt, int maxAttempts) { }
        public void RecordOperationFailure(DiagnosticsPersistenceOperation operation) { }
        public void RecordLoss(DiagnosticsPersistenceLossReason reason, long count) { }
    }
}
