using Elsa.Jobs.Core;
using Elsa.Secrets.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Jobs.Secrets;

/// <summary>Worker-invocation-bound resolution of accepted declared references; no caller-selected tenant.</summary>
public interface IJobSecrets
{
    ValueTask<string> ResolveAsync(SecretReference reference, CancellationToken cancellationToken = default);
}

/// <summary>One exact JSON-pointer reference slot in a declared input schema.</summary>
public sealed class JobSecretReferenceSlot(string pointer, IReadOnlyList<SecretReference> allowedReferences, bool required = true)
{
    public string Pointer { get; } = pointer;
    public IReadOnlyList<SecretReference> AllowedReferences { get; } = Array.AsReadOnly(allowedReferences.ToArray());
    public bool Required { get; } = required;
}

/// <summary>Host-owned data contract for one logical handler and input schema; never serialized into a JobRequest.</summary>
public sealed class JobSecretDeclaration(JobHandlerIdentity handler, JobSchema inputSchema,
    IReadOnlyList<JobSecretReferenceSlot> referenceSlots, IReadOnlyList<string>? protectedInputPointers = null)
{
    public JobHandlerIdentity Handler { get; } = handler;
    public JobSchema InputSchema { get; } = inputSchema;
    public IReadOnlyList<JobSecretReferenceSlot> ReferenceSlots { get; } = Array.AsReadOnly(referenceSlots.ToArray());
    public IReadOnlyList<string> ProtectedInputPointers { get; } = Array.AsReadOnly(protectedInputPointers?.ToArray() ?? []);
}

public sealed class JobSecretsOptions
{
    public IList<JobSecretDeclaration> Declarations { get; } = new List<JobSecretDeclaration>();
}

public static class JobSecretsRegistration
{
    /// <summary>Adds pure admission checks and an invocation-scoped facade. Installs no Secrets provider or value store.</summary>
    public static IServiceCollection AddJobSecrets(this IServiceCollection services, Action<JobSecretsOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services); ArgumentNullException.ThrowIfNull(configure);
        if (services.Any(item => item.ServiceType == typeof(JobSecretCatalog)))
            throw new InvalidOperationException("Jobs Secrets is already configured.");
        var options = new JobSecretsOptions(); configure(options);
        services.AddSingleton(new JobSecretCatalog(options.Declarations));
        services.AddScoped<JobSecretsSession>();
        services.AddScoped<IJobSecrets>(provider => provider.GetRequiredService<JobSecretsSession>());
        services.AddScoped<IJobDataPolicy>(provider => provider.GetRequiredService<JobSecretsSession>());
        return services;
    }
}
