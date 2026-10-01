using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// The settings every EF store a host composes on its own container from its configuration accepts, rather than through a
/// shell feature: the provider, the connection, the schema and pooling, as every first-party EF module names them.
/// </summary>
public interface IEfHostStoreOptions
{
    /// <summary>Relational provider for the store: Sqlite, SqlServer, PostgreSql or MySql.</summary>
    string Provider { get; set; }

    /// <summary>Optional explicit connection string; otherwise <see cref="ConnectionName"/>, else <c>ConnectionStrings:Elsa</c>.</summary>
    string? ConnectionString { get; set; }

    /// <summary>Named connection under <c>ConnectionStrings</c> when <see cref="ConnectionString"/> is omitted.</summary>
    string? ConnectionName { get; set; }

    /// <summary>Optional database schema for the store's tables and its migrations history table.</summary>
    string? Schema { get; set; }

    /// <summary>Reuse contexts from a pool instead of constructing one per operation.</summary>
    bool Pooling { get; set; }
}

/// <summary>A host's configuration of a store it composes on its own container cannot be acted on as written; the message names the key.</summary>
public sealed class EfHostConfigurationException(string message, Exception? innerException = null) : InvalidOperationException(message, innerException);

/// <summary>
/// Reads the configuration of an EF store a host composes on its own container, such as cluster membership's or the Data
/// Protection key store's, the one way: a blank value leaves the default, a value that does not parse is refused, naming its
/// key, and a store section that carries settings but no <see cref="EnabledKey"/> switch is refused rather than ignored, since
/// a host that meant to enable the store and silently did not is the failure that looks like success.
/// </summary>
/// <param name="refuse">The exception a refusal is raised as, for a feature whose contract names its own.</param>
public sealed class EfHostConfigurationReader(Func<string, Exception> refuse)
{
    public const string EnabledKey = "Enabled";

    /// <summary>A reader that refuses with <see cref="EfHostConfigurationException"/>.</summary>
    public EfHostConfigurationReader() : this(message => new EfHostConfigurationException(message))
    {
    }

    /// <summary>
    /// Whether <paramref name="section"/>'s <see cref="EnabledKey"/> switch is on. A section without the switch is off when it
    /// is empty and refused when it carries settings; <paramref name="enable"/> and <paramref name="disable"/> say what true and
    /// false do, as the refusal tells the operator.
    /// </summary>
    public bool IsEnabled(IConfigurationSection section, string enable, string disable)
    {
        ArgumentNullException.ThrowIfNull(section);
        return ReadBool(section, EnabledKey) switch
        {
            null when section.GetChildren().Any() => throw refuse(
                $"{section.Path} carries settings but no {EnabledKey} switch. Set {section.Path}:{EnabledKey} to true {enable}, " +
                $"or to false {disable}."),
            null or false => false,
            true => true
        };
    }

    /// <summary>Reads the provider, connection, schema and pooling under <paramref name="section"/> into <paramref name="options"/>.</summary>
    public TOptions ReadStore<TOptions>(IConfigurationSection section, TOptions options) where TOptions : IEfHostStoreOptions
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(options);
        options.Provider = section[nameof(IEfHostStoreOptions.Provider)] ?? options.Provider;
        options.ConnectionString = section[nameof(IEfHostStoreOptions.ConnectionString)];
        options.ConnectionName = section[nameof(IEfHostStoreOptions.ConnectionName)];
        options.Schema = section[nameof(IEfHostStoreOptions.Schema)];
        options.Pooling = ReadBool(section, nameof(IEfHostStoreOptions.Pooling)) ?? options.Pooling;
        return options;
    }

    public bool? ReadBool(IConfigurationSection section, string key) =>
        section[key] is not { } value || string.IsNullOrWhiteSpace(value) ? null
        : bool.TryParse(value, out var parsed) ? parsed
        : throw Invalid(section, key, value, "true or false");

    public TimeSpan? ReadTimeSpan(IConfigurationSection section, string key) =>
        section[key] is not { } value || string.IsNullOrWhiteSpace(value) ? null
        : TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var parsed) ? parsed
        : throw Invalid(section, key, value, "a time span such as 00:00:10");

    private Exception Invalid(IConfigurationSection section, string key, string value, string expected) =>
        refuse($"{section.Path}:{key} is '{value}', which is not {expected}.");
}
