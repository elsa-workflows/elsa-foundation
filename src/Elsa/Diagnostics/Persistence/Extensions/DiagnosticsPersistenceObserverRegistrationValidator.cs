using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elsa.Diagnostics.Persistence.Extensions;

public sealed class DiagnosticsPersistenceObserverRegistrationOptions;

public sealed record DiagnosticsPersistenceObserverRegistrationState(IServiceCollection Services);

public sealed class DiagnosticsPersistenceObserverRegistrationValidator(
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
