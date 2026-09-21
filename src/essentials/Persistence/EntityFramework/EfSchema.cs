using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// The optional database schema every first-party EF module puts its tables and its own migrations history table
/// in, so an Elsa deployment can share a database with an application that owns the default schema.
/// </summary>
/// <remarks>
/// <para>
/// A module setting wins, then the host-wide <see cref="ConfigurationKey"/>, then no schema at all, which is what
/// every existing deployment keeps: unset means the provider's own default (<c>dbo</c>, <c>public</c>, the
/// connection's database), exactly as before.
/// </para>
/// <para>
/// SQLite has no schemas, so the setting is ignored there rather than refused: one appsettings file can name a
/// schema and still run the SQLite developer shell.
/// </para>
/// <para>
/// Nothing here creates the schema: on both providers that take one, EF's own migrations-history script creates
/// it before it creates the history table, which is the first thing a migrate does.
/// </para>
/// <para>
/// MySQL refuses the setting instead. A MySQL schema <em>is</em> a database, so the setting would mean something
/// different there from what it means everywhere else, and <c>MySql.EntityFrameworkCore</c>'s history repository
/// emits a <c>CREATE DATABASE</c> with no statement terminator in front of its <c>CREATE TABLE</c>, which the
/// server rejects the moment the first module migrates. A MySQL host names the database in its connection string,
/// which is the same thing said in MySQL's own terms; the error says so.
/// </para>
/// </remarks>
public static class EfSchema
{
    /// <summary>
    /// The host-wide default: <c>Elsa:Persistence:EntityFramework:Schema</c>, or the environment variable
    /// <c>Elsa__Persistence__EntityFramework__Schema</c>. One key puts every module in one schema.
    /// </summary>
    public const string ConfigurationKey = "Elsa:Persistence:EntityFramework:Schema";

    /// <summary>What every provider in the matrix accepts as an unquoted identifier.</summary>
    public const int MaxLength = 64;

    /// <summary>
    /// The module's own setting, then <see cref="ConfigurationKey"/>, then none. Returns null for a blank value and
    /// on SQLite, so a caller can treat "no schema" as one case; refuses one on MySQL.
    /// </summary>
    public static string? Resolve(IServiceProvider services, string owner, string provider, string? schema)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        if (string.IsNullOrWhiteSpace(schema))
        {
            var configuration = (IConfiguration?)services.GetService(typeof(IConfiguration));
            schema = configuration?[ConfigurationKey];
        }

        return Normalize(owner, provider, schema);
    }

    /// <summary>
    /// Trims and validates an explicit schema. Returns null for a blank one and for SQLite, which has none, and
    /// refuses one on MySQL, where a schema is a database.
    /// </summary>
    public static string? Normalize(string owner, string provider, string? schema)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        if (string.IsNullOrWhiteSpace(schema))
            return null;
        var normalizedProvider = EfRelationalProviderBinding.Normalize(provider);
        // Ignored rather than refused: a host that names one schema for its servers still runs SQLite locally.
        if (normalizedProvider == "sqlite")
            return null;
        if (normalizedProvider == "mysql")
        {
            throw new InvalidOperationException(
                $"{owner} EF cannot use schema '{schema}' on MySQL, where a schema is a database rather than a " +
                "namespace inside one. Name it in the connection string instead (Database=" + schema.Trim() + "), " +
                $"and leave the schema setting and '{ConfigurationKey}' unset for MySQL.");
        }

        var trimmed = schema.Trim();
        // A schema reaches DDL and the migrations history table. Delimiters, dots and whitespace are refused here
        // rather than escaped, because a name that needs escaping is a configuration mistake, not a schema.
        if (trimmed.Length > MaxLength || !trimmed.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '$'))
        {
            throw new InvalidOperationException(
                $"{owner} EF schema '{schema}' is not a plain identifier. Use up to {MaxLength} ASCII letters, digits, " +
                "'_' or '$'.");
        }

        return trimmed;
    }
}

public static class EfSchemaModelBuilderExtensions
{
    /// <summary>
    /// Applies the schema this context was bound to. Every first-party module context calls this first in
    /// <c>OnModelCreating</c>; with no schema configured it changes nothing.
    /// </summary>
    public static ModelBuilder HasElsaDefaultSchema(this ModelBuilder modelBuilder, DbContext context)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        if (EfSchemaOptionsExtension.Find(context) is { } schema)
            modelBuilder.HasDefaultSchema(schema);
        return modelBuilder;
    }
}
