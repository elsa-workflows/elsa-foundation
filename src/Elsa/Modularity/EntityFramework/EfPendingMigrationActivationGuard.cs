using Elsa.Modularity.Core.Contracts;
using Elsa.Modularity.Core.Models;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.Tooling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace Elsa.Modularity.EntityFramework;

/// <summary>
/// Refuses to enable a feature whose EF module's schema is behind (spec 171 FR-062, FR-067–FR-069, ADR
/// 0076 D9). It maps a feature to its module(s) through <c>[UsesEfModule]</c>, resolves that module's
/// provider and connection from the feature's own configuration, and — under
/// <c>Elsa:Persistence:EntityFramework:Migrate:Policy=Validate</c> — asks the module's own history table
/// whether anything is outstanding. Under <c>AutoMigrate</c> it opens nothing: the host migrates the
/// module itself at Prepare, so there is nothing to refuse.
/// </summary>
/// <remarks>
/// <para>
/// Every refusal names the feature and the module and nothing else. The request this guard reads has had
/// its secret settings restored to their real values before validation, so it is holding live connection
/// strings (FR-061); the one realistic way one reaches an operator is a driver's own exception message
/// echoing it back, so a failure to read a database contributes its <em>type</em> to the refusal and its
/// message is dropped rather than trusted to a redaction pattern. This type writes no log line at all,
/// for the same reason.
/// </para>
/// <para>
/// It checks pending migrations, not post-migration actions. A declared action is audited by
/// <c>EfModuleMigrator</c> under both policies (FR-056), and auditing one here would mean opening the
/// database under <c>AutoMigrate</c> too — exactly what FR-069 forbids. A module that declares actions has
/// <c>post-migrate</c> named in its refusal instead, so the operator is pointed at the whole path rather
/// than only its first step.
/// </para>
/// </remarks>
public sealed class EfPendingMigrationActivationGuard(IServiceProvider services, IEfModuleAssemblySource assemblies)
    : IFeatureActivationGuard
{
    private const string ProviderSetting = "Provider";
    private const string ConnectionStringSetting = "ConnectionString";
    private const string ConnectionNameSetting = "ConnectionName";
    private const string SchemaSetting = "Schema";

    public async Task<FeatureActivationDecision> EvaluateAsync(
        FeatureActivationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var loaded = assemblies.GetAssemblies();
        var usages = EfProviderAgreement.Discover(loaded);
        var enabled = context.EnabledFeatures;
        var mapped = enabled
            .Select(feature => (Feature: feature, Usage: Find(usages, feature.Id)))
            .Where(candidate => candidate.Usage is not null)
            .ToArray();
        if (mapped.Length == 0)
            return FeatureActivationDecision.Allowed;

        var modules = EfModuleCatalog.Discover(loaded);
        var policy = EfMigrateOptions.Resolve((IConfiguration?)services.GetService(typeof(IConfiguration)));
        var probed = new Dictionary<string, Probe>(StringComparer.Ordinal);
        var refusals = new List<FeatureActivationRefusal>();

        foreach (var (feature, usage) in mapped)
        foreach (var module in usage!.Modules)
        {
            // A module name nothing loaded declares is not this guard's to judge: the feature's own
            // startup is where an unresolvable dependency surfaces, with the whole shell's composition
            // in view rather than one attribute's string.
            if (EfModuleCatalog.Find(modules, module) is not { } descriptor)
                continue;

            foreach (var source in Sources(feature, usage, module, enabled, usages))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var settings = Settings.From(source.Configuration);
                var key = settings.Key(descriptor, policy);
                if (!probed.TryGetValue(key, out var probe))
                {
                    probe = await ProbeAsync(descriptor, settings, policy, cancellationToken);
                    probed[key] = probe;
                }

                if (probe.Verdict is not Verdict.Current)
                    refusals.Add(new(feature.Id, Describe(feature.Id, descriptor, source.Feature, settings, probe)));
            }
        }

        return refusals.Count == 0 ? FeatureActivationDecision.Allowed : new(refusals);
    }

    /// <summary>
    /// The enabled feature(s) whose configuration decides how <paramref name="module"/> connects, and so
    /// what this guard probes. Ordinarily that is the feature itself. A feature with no <c>Provider</c>
    /// setting of its own — <c>WorkflowsDashboardEntityFrameworkCoreFeature</c> reads two modules' contexts
    /// and registers neither module's migrations — is evaluated through whichever enabled features in the
    /// same shell do register them (FR-065). When none is enabled, this yields nothing and the guard passes
    /// for that module: the feature's own startup check is what refuses a dashboard whose backends are not
    /// EF-owned, and it can say so with the shell's whole composition in view.
    /// </summary>
    private static IEnumerable<(string Feature, JsonElement Configuration)> Sources(
        FeatureApplyItem feature,
        EfFeatureModuleUsage usage,
        string module,
        IReadOnlyList<FeatureApplyItem> enabled,
        IReadOnlyList<EfFeatureModuleUsage> usages)
    {
        if (usage.DeclaresProvider)
            return [(feature.Id, feature.Configuration)];

        return enabled
            .Select(candidate => (Candidate: candidate, Usage: Find(usages, candidate.Id)))
            .Where(other =>
                other.Usage is { DeclaresProvider: true } &&
                other.Usage.Modules.Contains(module, StringComparer.OrdinalIgnoreCase))
            .Select(other => (other.Candidate.Id, other.Candidate.Configuration));
    }

    private async Task<Probe> ProbeAsync(
        EfModuleDescriptor descriptor,
        Settings settings,
        EfMigratePolicy policy,
        CancellationToken cancellationToken)
    {
        // An engine this host cannot bind is a refusal whatever the policy says (FR-069): the feature
        // cannot start on a provider that is not there, and finding that out opens no database.
        if (EfRelationalProviderBinding.DescribeBindingFailure(settings.Provider) is { } binding)
            return new(Verdict.Unbindable, Redact(binding, settings));

        // Under AutoMigrate the module's own migrator brings the schema current at Prepare, so there is
        // nothing here to refuse — and nothing to look at. The database is not opened at all, not even to
        // find out whether it could be.
        if (policy is EfMigratePolicy.AutoMigrate)
            return Probe.Current;

        if (descriptor.ProviderContext(settings.Provider) is not { } contextType)
            return new(Verdict.Unsupported);

        string connection;
        string? schema;
        try
        {
            connection = EfConnectionDefaults.ResolveConnectionString(
                services,
                descriptor.Owner,
                settings.Provider,
                settings.ConnectionString,
                settings.ConnectionName,
                descriptor.DefaultConnectionName,
                descriptor.DefaultSqliteConnectionString);
            schema = EfSchema.Resolve(services, descriptor.Owner, settings.Provider, settings.Schema);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            // These are this build's own messages, naming a connection's *name* or a schema rather than a
            // connection string — and redacted all the same, because "this one cannot leak" is exactly the
            // assumption FR-061 declines to make.
            return new(Verdict.Unresolved, Redact(failure.Message, settings));
        }

        try
        {
            await using var context = CreateContext(descriptor, contextType, settings.Provider, connection, schema);
            await EfDatabaseMigrator.ApplyAsync(
                context,
                EfRelationalProviderBinding.ExpectedProviderName(settings.Provider),
                EfMigratePolicy.Validate,
                cancellationToken);
            return Probe.Current;
        }
        catch (EfPendingMigrationsException)
        {
            // EfDatabaseMigrator's own fail-closed signal: the database was reached and read, and its
            // history says migrations are outstanding. Caught by that dedicated type rather than by
            // InvalidOperationException, so the failure below — which is a failure to read the database at
            // all — is never reported as a pending migration, nor the other way round.
            return new(Verdict.Pending);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            // Fail closed (FR-069): a database that cannot be opened, or whose history table cannot be
            // read, is treated as having every migration pending rather than optimistically as current.
            // Only the failure's type travels into the refusal; a driver's message is the one realistic
            // path for a credential to reach an operator, and no redaction pattern is proof that it did not.
            return new(Verdict.Unreadable, failure.GetType().Name);
        }
    }

    private static DbContext CreateContext(
        EfModuleDescriptor descriptor,
        Type contextType,
        string provider,
        string connection,
        string? schema)
    {
        var builder = (DbContextOptionsBuilder)Activator.CreateInstance(typeof(DbContextOptionsBuilder<>).MakeGenericType(contextType))!;
        EfRelationalProviderBinding.Use(
            builder,
            provider,
            connection,
            descriptor.HistoryTableName,
            descriptor.Assembly.GetName().Name,
            schema);
        return (DbContext)Activator.CreateInstance(contextType, builder.Options)!;
    }

    private static string Describe(
        string feature,
        EfModuleDescriptor descriptor,
        string configuredBy,
        Settings settings,
        Probe probe)
    {
        var via = string.Equals(configuredBy, feature, StringComparison.OrdinalIgnoreCase)
            ? ""
            : $" (configured by enabled feature '{configuredBy}', which registers that module's migrations)";
        var head = $"Feature '{feature}' depends on EF module '{descriptor.Name}'{via}";
        var policy = $"'{EfMigrateOptions.SectionName}:{nameof(EfMigrateOptions.Policy)}={nameof(EfMigratePolicy.Validate)}'";

        return probe.Verdict switch
        {
            Verdict.Pending =>
                $"{head} which has migrations that are not applied to its database, and this host runs {policy}. " +
                $"Nothing was saved. Apply them out of process, then enable the feature again:{Environment.NewLine}  " +
                Commands(descriptor, settings.Provider),
            Verdict.Unreadable =>
                $"{head} whose database could not be reached ({probe.Detail}), so every migration is treated as " +
                $"pending while this host runs {policy}. Nothing was saved. Check that module's connection " +
                $"settings, apply its migrations, then enable the feature again:{Environment.NewLine}  " +
                Commands(descriptor, settings.Provider),
            Verdict.Unresolved =>
                $"{head} whose database connection could not be resolved: {probe.Detail} Nothing was saved.",
            Verdict.Unsupported =>
                $"{head} which does not support the '{settings.Provider}' provider it is configured for. Nothing was saved.",
            Verdict.Unbindable =>
                $"{head} whose '{settings.Provider}' provider engine could not be bound: {probe.Detail} Nothing was saved.",
            // Reached only by a verdict added without a sentence to go with it, which is a refusal that
            // would otherwise be reported as an empty one.
            _ => throw new ArgumentOutOfRangeException(nameof(probe), probe.Verdict, "This verdict is not a refusal.")
        };
    }

    /// <summary>The exact command(s) that bring <paramref name="descriptor"/>'s schema current, in ADR 0076 D9's order.</summary>
    private static string Commands(EfModuleDescriptor descriptor, string provider)
    {
        var named = EfRelationalProviderBinding.Select(provider, descriptor.Name, "Sqlite", "SqlServer", "PostgreSql", "MySql");
        // D5: EF cannot emit an idempotent script for SQLite, so `script` refuses that provider outright.
        // Naming it here would name a command that cannot run; `apply` is the path for a SQLite deployment.
        var bringCurrent = EfRelationalProviderBinding.Normalize(provider) == "sqlite"
            ? $"dotnet elsa persistence apply --modules {descriptor.Name} --provider {named} --connection-env ELSA_CONNECTION"
            : $"dotnet elsa persistence script --modules {descriptor.Name} --provider {named} --output <directory>";

        // A module that declares a post-migration action is not current until that has run too, and only
        // `post-migrate` ever runs one (D8) — so the whole path is named rather than only its first step.
        return descriptor.PostMigration.Count == 0
            ? bringCurrent
            : $"{bringCurrent}{Environment.NewLine}  {EfPostMigrationActions.CommandFor(descriptor.Name, named)}";
    }

    /// <summary>
    /// Defense in depth on every piece of borrowed text: whatever it says, the values this request carries
    /// are scrubbed out of it before an operator can read it.
    /// </summary>
    private static string Redact(string text, Settings settings) =>
        EfToolingRedaction.Redact(text, settings.ConnectionString ?? "");

    private static EfFeatureModuleUsage? Find(IReadOnlyList<EfFeatureModuleUsage> usages, string feature) =>
        usages.FirstOrDefault(usage => string.Equals(usage.Feature, feature, StringComparison.OrdinalIgnoreCase));

    private enum Verdict
    {
        /// <summary>Nothing outstanding, or nothing this guard looks at under the configured policy.</summary>
        Current,
        Pending,
        Unreadable,
        Unresolved,
        Unsupported,
        Unbindable
    }

    private sealed record Probe(Verdict Verdict, string? Detail = null)
    {
        public static readonly Probe Current = new(Verdict.Current);
    }

    /// <summary>The four settings every mapped feature carries, as the request spells them.</summary>
    private sealed record Settings(string Provider, string? ConnectionString, string? ConnectionName, string? Schema)
    {
        public static Settings From(JsonElement configuration) =>
            new(
                // FR-037: a feature that declares the setting and leaves it unset is its own default, Sqlite.
                Read(configuration, ProviderSetting) is { } provider && !string.IsNullOrWhiteSpace(provider)
                    ? provider
                    : EfProviderAgreement.UnsetProvider,
                Read(configuration, ConnectionStringSetting),
                Read(configuration, ConnectionNameSetting),
                Read(configuration, SchemaSetting));

        /// <summary>A separator no connection string, provider name or schema can contain.</summary>
        private const char Separator = '\u001f';

        /// <summary>
        /// What makes two probes the same question, so one module shared by nine enabled features is opened
        /// once rather than nine times.
        /// </summary>
        public string Key(EfModuleDescriptor descriptor, EfMigratePolicy policy) =>
            string.Join(Separator, descriptor.Name, policy.ToString(), Provider, ConnectionString, ConnectionName, Schema);

        /// <summary>
        /// Matched case-insensitively, the way CShells' own feature binder matches a configuration key to
        /// the property it sets.
        /// </summary>
        private static string? Read(JsonElement configuration, string name)
        {
            if (configuration.ValueKind is not JsonValueKind.Object)
                return null;

            foreach (var property in configuration.EnumerateObject())
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                    return property.Value.ValueKind is JsonValueKind.String ? property.Value.GetString() : null;

            return null;
        }
    }
}
