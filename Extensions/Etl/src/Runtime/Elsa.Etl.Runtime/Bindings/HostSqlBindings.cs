using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;

namespace Elsa.Etl.Runtime.Bindings;

/// <summary>
/// Trusted host SQL bindings for SQL sources and destinations (specs 1197 and 1198). A pinned handle hashes the provider, the target the engine parses out of the
/// connection string, the allowed tables and the ledger settings, never the connection string or its password.
/// </summary>
internal sealed class HostSqlBindings(IEnumerable<EtlHostSqlBindingOptions> options, IEnumerable<IEtlSqlEngine> engines)
{
    private readonly EtlHostSqlBindingOptions[] _bindings = options.ToArray();
    private readonly Dictionary<string, IEtlSqlEngine> _engines = engines
        .GroupBy(engine => engine.Provider, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key, group => group.Count() == 1 ? group.Single()
            : throw new InvalidOperationException($"More than one ETL SQL engine is registered for '{group.Key}'."),
            StringComparer.OrdinalIgnoreCase);

    public bool ContainsBinding(EtlScope scope, string alias, string role) =>
        _bindings.Any(item => item.IsCurrent && Matches(item, scope, alias, role));

    public IReadOnlyList<EtlBindingSummary> ListBindings(EtlScope scope, string? afterAlias, string? afterRole, int take) =>
        _bindings.Where(item => item.IsCurrent && Scope(item) == scope)
            .Select(item => new EtlBindingSummary(item.Alias, item.Role))
            .Where(item => afterAlias is null || StringComparer.Ordinal.Compare(item.Alias, afterAlias) > 0 ||
                StringComparer.Ordinal.Equals(item.Alias, afterAlias) && StringComparer.Ordinal.Compare(item.Role, afterRole) > 0)
            .OrderBy(item => item.Alias, StringComparer.Ordinal).ThenBy(item => item.Role, StringComparer.Ordinal)
            .Take(take).ToArray();

    /// <summary>Resolves the current revision for a new run and checks that it allows the table.</summary>
    public HostSqlResolvedBinding ResolveCurrent(EtlScope scope, string alias, string role, string table)
    {
        var current = _bindings.Where(item => item.IsCurrent && Matches(item, scope, alias, role)).ToArray();
        if (current.Length == 0) throw Unavailable();
        if (current.Length > 1) throw new EtlBindingException("etl.binding-ambiguous", "An ETL host SQL binding has ambiguous revisions.");
        return Resolve(current[0], table);
    }

    /// <summary>Resolves the exact pinned revision for a running run and checks its handle and table.</summary>
    public HostSqlResolvedBinding ResolveExact(EtlScope scope, string alias, string role, string revision, string handle, string table)
    {
        var pinned = _bindings.Where(item => Matches(item, scope, alias, role) && item.Revision == revision).ToArray();
        if (pinned.Length != 1) throw RevisionUnavailable();
        var resolved = Resolve(pinned[0], table);
        return StringComparer.Ordinal.Equals(resolved.Handle, handle) ? resolved : throw RevisionUnavailable();
    }

    private HostSqlResolvedBinding Resolve(EtlHostSqlBindingOptions binding, string table)
    {
        if (!_engines.TryGetValue(binding.Provider, out var engine))
            throw new EtlBindingException("etl.sql-unavailable", "No ETL SQL engine is installed for the binding's provider.");
        // Exact comparison: PostgreSQL's quoted identifiers are case-sensitive, so "Orders" never approves "orders".
        if (!binding.AllowedTables.Contains(table, StringComparer.Ordinal))
            throw new EtlBindingException("etl.sql-table-not-allowed", "The SQL binding does not allow the table.");
        if (string.IsNullOrWhiteSpace(binding.ConnectionString) || !IsTableIdentifier(binding.LedgerTable))
            throw Unavailable();
        EtlSqlTarget target;
        try
        {
            target = engine.DescribeTarget(binding.ConnectionString);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or KeyNotFoundException)
        {
            throw new EtlBindingException("etl.binding-unavailable", "The SQL binding's connection string is invalid.");
        }
        var handle = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            FormatVersion = 1,
            ScopeIdentity = EtlRuntimeIdentity.ScopeKey(Scope(binding)),
            binding.Alias,
            binding.Role,
            binding.Revision,
            Provider = engine.Provider,
            target.Host,
            target.Port,
            target.Database,
            target.User,
            AllowedTables = binding.AllowedTables.Order(StringComparer.Ordinal).ToArray(),
            binding.LedgerTable,
            binding.CreateLedger
        })));
        return new(binding.Alias, binding.Revision, handle, engine, binding.ConnectionString, binding.LedgerTable, binding.CreateLedger);
    }

    /// <summary><c>name</c> or <c>schema.name</c>, each part <c>[A-Za-z_][A-Za-z0-9_]{0,62}</c>, as the compiler requires of tables.</summary>
    private static bool IsTableIdentifier(string? value) => value?.Split('.') is { Length: 1 or 2 } parts && parts.All(part =>
        part is { Length: >= 1 and <= 63 } && (char.IsAsciiLetter(part[0]) || part[0] == '_') &&
        part.All(character => char.IsAsciiLetterOrDigit(character) || character == '_'));

    private static bool Matches(EtlHostSqlBindingOptions item, EtlScope scope, string alias, string role) =>
        Scope(item) == scope && item.Alias == alias && item.Role == role;

    private static EtlScope Scope(EtlHostSqlBindingOptions item) => new(item.TenantId, item.PersistencePartition);

    private static EtlBindingException Unavailable() =>
        new("etl.binding-unavailable", "The scoped ETL binding is unavailable for this connector role.");

    private static EtlBindingException RevisionUnavailable() =>
        new("etl.binding-revision-unavailable", "The pinned ETL SQL binding revision is unavailable.");
}

/// <summary>A resolved SQL binding; its connection string never leaves the runtime.</summary>
internal sealed record HostSqlResolvedBinding(string Alias, string Revision, string Handle, IEtlSqlEngine Engine,
    string ConnectionString, string LedgerTable, bool CreateLedger)
{
    public override string ToString() => nameof(HostSqlResolvedBinding);
}
