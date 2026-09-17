using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// The host-wide apply mode every module's <see cref="EfModuleMigrator{TContext}"/> reads. It is bound from
/// configuration, so switching a deployment to out-of-process migration is an operator change, not a code change.
/// </summary>
public sealed class EfMigrateOptions
{
    /// <summary>
    /// The section the policy is read from: <c>Elsa:Persistence:EntityFramework:Migrate:Policy</c>, or the
    /// environment variable <c>Elsa__Persistence__EntityFramework__Migrate__Policy</c>. In a CShells host the
    /// shell's own <c>Configuration</c> node wins over the host's, because that is how a shell overrides any key.
    /// </summary>
    public const string SectionName = "Elsa:Persistence:EntityFramework:Migrate";

    public EfMigratePolicy Policy { get; set; } = EfMigratePolicy.AutoMigrate;
}

/// <summary>
/// Reads <see cref="EfMigrateOptions.Policy"/> from the configuration of the container the migrator resolves its
/// options from — a shell's <c>ShellConfiguration</c> (shell keys over host keys) or a plain host's configuration.
/// A container with no <see cref="IConfiguration"/>, or one that leaves the key unset, keeps the AutoMigrate
/// default. A value that names no policy is refused: taking the default instead would auto-migrate the database
/// the operator meant to protect, and the log would look exactly like a healthy start.
/// </summary>
internal sealed class EfMigrateOptionsConfigurator(IServiceProvider services) : IConfigureOptions<EfMigrateOptions>
{
    public void Configure(EfMigrateOptions options)
    {
        var configured = services.GetService<IConfiguration>()?.GetSection(EfMigrateOptions.SectionName)[nameof(EfMigrateOptions.Policy)];
        if (string.IsNullOrWhiteSpace(configured))
            return;

        // TryParse alone accepts any number, including one no policy is defined for, so IsDefined does the refusing.
        if (!Enum.TryParse<EfMigratePolicy>(configured, ignoreCase: true, out var policy) || !Enum.IsDefined(policy))
        {
            throw new InvalidOperationException(
                $"Configuration '{EfMigrateOptions.SectionName}:{nameof(EfMigrateOptions.Policy)}' is '{configured}'. " +
                $"Use '{nameof(EfMigratePolicy.AutoMigrate)}' or '{nameof(EfMigratePolicy.Validate)}'.");
        }

        options.Policy = policy;
    }
}
