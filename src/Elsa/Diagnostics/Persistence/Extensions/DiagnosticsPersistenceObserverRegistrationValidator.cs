using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elsa.Diagnostics.Persistence.Extensions;

internal sealed class DiagnosticsPersistenceObserverRegistrationOptions;

internal sealed record DiagnosticsPersistenceObserverRegistrationState(IServiceCollection Services);

internal sealed class DiagnosticsPersistenceObserverRegistrationValidator(
    DiagnosticsPersistenceObserverRegistrationState state)
    : IValidateOptions<DiagnosticsPersistenceObserverRegistrationOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        DiagnosticsPersistenceObserverRegistrationOptions options)
    {
        var message = DiagnosticsPersistenceRegistration.GetObserverConflictMessage(state.Services);
        return message is null
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(message);
    }
}
