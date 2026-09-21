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

    /// <summary>What a host that configures nothing gets.</summary>
    public const EfMigratePolicy DefaultPolicy = EfMigratePolicy.AutoMigrate;

    public EfMigratePolicy Policy { get; set; } = DefaultPolicy;

    /// <summary>
    /// The policy <paramref name="configuration"/> names, or <see cref="DefaultPolicy"/> when it names none.
    /// Exposed because not every reader of this key resolves it through the options pipeline: the
    /// feature-activation guard runs in the host container, where no module has registered
    /// <see cref="EfMigrateOptions"/> at all, and a guard that silently read the default there would allow
    /// exactly the activations a Validate host configured it to refuse.
    /// </summary>
    public static EfMigratePolicy Resolve(IConfiguration? configuration) =>
        TryResolve(configuration, out var policy) ? policy : DefaultPolicy;

    /// <summary>
    /// <see cref="Resolve"/> for a caller that must tell "configured" apart from "left unset", and so keep a
    /// value set another way instead of overwriting it with the default. A value that names no policy is
    /// refused rather than defaulted, in both.
    /// </summary>
    public static bool TryResolve(IConfiguration? configuration, out EfMigratePolicy policy)
    {
        policy = DefaultPolicy;
        var configured = configuration?.GetSection(SectionName)[nameof(Policy)];
        if (string.IsNullOrWhiteSpace(configured))
            return false;

        // TryParse alone accepts any number, including one no policy is defined for, so IsDefined does the refusing.
        if (!Enum.TryParse(configured, ignoreCase: true, out policy) || !Enum.IsDefined(policy))
        {
            throw new InvalidOperationException(
                $"Configuration '{SectionName}:{nameof(Policy)}' is '{configured}'. " +
                $"Use '{nameof(EfMigratePolicy.AutoMigrate)}' or '{nameof(EfMigratePolicy.Validate)}'.");
        }

        return true;
    }
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
        if (EfMigrateOptions.TryResolve(services.GetService<IConfiguration>(), out var policy))
            options.Policy = policy;
    }
}
