using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Stores;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Testing;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Elsa.Foundation.Identity.Persistence.Groundwork;
using Elsa.Foundation.Identity.Persistence.Groundwork.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Persistence.Groundwork.Composition;
using Groundwork.Kernel;
using Groundwork.MongoDb;
using Groundwork.PostgreSql;
using Groundwork.Sqlite;
using Groundwork.SqlServer;
using Groundwork.Store;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.ProcessProbe;

internal static class Program
{
    public static async Task<int> Main()
    {
        var stdout = Console.Out;
        var stderr = Console.Error;
        Console.SetOut(TextWriter.Null);
        Console.SetError(TextWriter.Null);
        IdentityProcessProbeCommand? command = null;
        try
        {
            command = IdentityProcessProbeProtocol.DeserializeCommand(await Console.In.ReadToEndAsync());
            var result = await ExecuteAsync(command);
            Console.SetOut(stdout);
            await stdout.WriteLineAsync(IdentityProcessProbeProtocol.SerializeResult(result));
            return 0;
        }
        catch (Exception exception)
        {
            Console.SetError(stderr);
            var error = new IdentityProcessProbeError(
                command?.ProtocolVersion ?? IdentityProcessProbeProtocol.CurrentVersion,
                command?.ProviderKey ?? "unknown",
                command is null ? "invalid-command" : FailureCode(exception));
            await stderr.WriteLineAsync(IdentityProcessProbeProtocol.SerializeError(error));
            return command is null ? 2 : 3;
        }
        finally
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
        }
    }

    private static async Task<IdentityProcessProbeResult> ExecuteAsync(IdentityProcessProbeCommand command)
    {
        var units = IdentityV2StorageManifest.CreateUnits()
            .Select(unit => unit with { Name = $"{unit.Name}_{command.PhysicalSuffix}" })
            .ToArray();
        using var source = new DirectSessionSource(CreateConnection(command), units);
        var accessor = new FixedAccessContextAccessor(
            PersistenceAccessContext.Scoped(new PersistenceScope(command.User.TenantId)));
        var store = new GroundworkIdentityUserStore(new GroundworkIdentityRowStore(source, accessor), accessor);
        var requested = User(command.User);

        var (outcome, found, errorCode) = command.Operation switch
        {
            IdentityProcessProbeOperation.CreateUser => await CreateAsync(store, requested),
            IdentityProcessProbeOperation.FindByNormalizedUserName => await FindAsync(store, requested),
            IdentityProcessProbeOperation.DuplicateCreate => await DuplicateAsync(store, requested),
            _ => throw new ArgumentOutOfRangeException(nameof(command.Operation))
        };
        return new IdentityProcessProbeResult(
            command.ProtocolVersion,
            command.LaunchFingerprint,
            command.ProviderKey,
            command.Operation,
            Environment.ProcessId,
            outcome,
            IdentityProcessProbeProtocol.ComputeSha256(found.Id),
            errorCode,
            Version(found));
    }

    private static async Task<(string Outcome, AspNetCoreIdentityUser Found, string? ErrorCode)> CreateAsync(
        GroundworkIdentityUserStore store,
        AspNetCoreIdentityUser user)
    {
        var result = await store.CreateAsync(user, CancellationToken.None);
        if (!result.Succeeded)
            throw new InvalidOperationException("The Identity process probe could not create its user.");
        var found = await store.FindByNameAsync(user.NormalizedUserName!, CancellationToken.None)
                    ?? throw new InvalidOperationException("The Identity process probe could not reload its created user.");
        return ("created", found, null);
    }

    private static async Task<(string Outcome, AspNetCoreIdentityUser Found, string? ErrorCode)> FindAsync(
        GroundworkIdentityUserStore store,
        AspNetCoreIdentityUser user)
    {
        var found = await store.FindByNameAsync(user.NormalizedUserName!, CancellationToken.None)
                    ?? throw new InvalidOperationException("The Identity process probe could not find its user.");
        return ("found", found, null);
    }

    private static async Task<(string Outcome, AspNetCoreIdentityUser Found, string? ErrorCode)> DuplicateAsync(
        GroundworkIdentityUserStore store,
        AspNetCoreIdentityUser user)
    {
        var result = await store.CreateAsync(user, CancellationToken.None);
        if (result.Succeeded)
            throw new InvalidOperationException("The Identity process probe created a duplicate normalized user name.");
        var errorCode = result.Errors.FirstOrDefault()?.Code ?? "unknown";
        var found = await store.FindByNameAsync(user.NormalizedUserName!, CancellationToken.None)
                    ?? throw new InvalidOperationException("The Identity process probe lost the original user after duplicate rejection.");
        return ("duplicate-rejected", found, errorCode);
    }

    private static AspNetCoreIdentityUser User(IdentityProcessProbeUser user) => new()
    {
        Id = user.UserId,
        TenantId = user.TenantId,
        UserName = user.UserName,
        NormalizedUserName = user.NormalizedUserName,
        Email = user.Email,
        NormalizedEmail = user.NormalizedEmail,
        EmailConfirmed = true,
        SecurityStamp = $"security-{user.UserId}",
        ConcurrencyStamp = $"revision-{user.UserId}"
    };

    private static long Version(AspNetCoreIdentityUser user) =>
        IdentityRevisionStamp.TryGetUserVersion(user.ConcurrencyStamp, user.TenantId, user.Id, out var version)
            ? version
            : throw new InvalidOperationException("The Identity process probe user has no Groundwork revision stamp.");

    private static IStorageProviderConnection CreateConnection(IdentityProcessProbeCommand command) =>
        command.ProviderKey switch
        {
            "sqlite" => new SqliteProviderFactory().Create(command.State.ConnectionString),
            "postgresql" => new PostgreSqlProviderFactory().Create(command.State.ConnectionString),
            "sqlserver" => new SqlServerProviderFactory().Create(command.State.ConnectionString),
            "mongodb" => new MongoProviderFactory().Create(command.State.ConnectionString),
            _ => throw new ArgumentOutOfRangeException(nameof(command.ProviderKey))
        };

    private static string FailureCode(Exception exception)
    {
        var name = exception.GetType().Name;
        if (name.EndsWith("Exception", StringComparison.Ordinal))
            name = name[..^"Exception".Length];
        var slug = string.Concat(name.SelectMany((character, index) =>
            char.IsAsciiLetterUpper(character) && index > 0
                ? new[] { '-', char.ToLowerInvariant(character) }
                : new[] { char.ToLowerInvariant(character) }));
        return $"provider-operation-failed-{slug}";
    }

    private sealed class FixedAccessContextAccessor(PersistenceAccessContext current)
        : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }

    private sealed class DirectSessionSource : IGroundworkStorageSessionSource, IDisposable
    {
        private readonly Lock gate = new();
        private readonly IStorageProviderConnection connection;
        private readonly IReadOnlyDictionary<string, StorageUnit> units;
        private readonly Dictionary<(string UnitId, StorageAccess Access), IStorageSession> sessions = [];

        public DirectSessionSource(IStorageProviderConnection connection, IReadOnlyList<StorageUnit> units)
        {
            this.connection = connection;
            this.units = units.ToDictionary(unit => unit.Id.Value, StringComparer.Ordinal);
            foreach (var unit in units)
                connection.Schema.Apply(unit);
        }

        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null)
        {
            var key = (unitId, access);
            lock (gate)
            {
                if (sessions.TryGetValue(key, out var session))
                    return session;
                session = connection.OpenSession(Unit(unitId, targetName), access);
                sessions.Add(key, session);
                return session;
            }
        }

        public IUnitOfWork BeginUnitOfWork(
            StorageAccess access,
            BatchWriteOptions options,
            IReadOnlyList<string> unitIds,
            string? targetName = null) =>
            connection.BeginUnitOfWork(access, options, unitIds.Select(unitId => Unit(unitId, targetName)).ToArray());

        public StorageUnit Unit(string unitId, string? targetName = null) => units[unitId];

        public void Dispose() => connection.Dispose();
    }
}
