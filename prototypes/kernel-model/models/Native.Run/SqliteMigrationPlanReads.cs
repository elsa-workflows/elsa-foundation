using System.Text;
using System.Text.Json;
using Kernel.Foundation.Contracts;
using Kernel.WorkflowActor;
using Microsoft.Data.Sqlite;

namespace Kernel.Native.Run;

/// <summary>The SQLite adapter for the operator's plan outcomes read model.</summary>
internal sealed class SqliteMigrationPlanReads(string file, FoundationReadCursor cursor) : IMigrationPlanReads
{
    private const int ActorRefBytes = 64;

    internal static async Task<SqliteMigrationPlanReads> OpenAsync(string file, CancellationToken stop)
    {
        await using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Mode = SqliteOpenMode.ReadOnly }.ConnectionString);
        await db.OpenAsync(stop);
        using var command = db.CreateCommand();
        command.CommandText = "SELECT cursor_key FROM plan_read_keys WHERE singleton = 1";
        if (await command.ExecuteScalarAsync(stop) is not byte[] { Length: 32 } key)
            throw new InvalidOperationException("The read model does not have an initialized continuation key.");
        return new(file, new(key));
    }

    public async ValueTask<MigrationPlanResultPage> ListAsync(BoundScope scope, ActorRef plan, int pageSize,
        PageToken? from = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrEmpty(plan.Id, nameof(plan));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pageSize, 500);
        var context = JsonSerializer.SerializeToUtf8Bytes(new { Read = "migration-plan-results", scope.Tenant, Plan = plan.Id, Size = pageSize });
        var after = from is { } token ? Unseal(context, token) : (ActorRef?)null;
        var page = await new SqlitePlanProjection(file, scope, plan).ReadPageAsync(after, pageSize, cancellationToken);
        PageToken? next = page.After is { } last ? Seal(context, last) : null;
        return new(page.Closed, new(page.Rows.Select(row => new MigrationPlanResult(row.Actor, row.Result)).ToArray(), next, page.AsOf));
    }

    private PageToken Seal(ReadOnlySpan<byte> context, ActorRef actor)
    {
        // This physical adapter stores built-in exact references, which have 64 ASCII bytes.
        if (actor.Id.Length != ActorRefBytes || actor.Id.Any(c => !char.IsAscii(c)))
            throw new InvalidOperationException("The built-in read model contains an invalid actor reference.");
        return new(cursor.Seal(context, Encoding.ASCII.GetBytes(actor.Id)));
    }

    private ActorRef Unseal(ReadOnlySpan<byte> context, PageToken token)
    {
        Span<byte> plain = stackalloc byte[ActorRefBytes];
        if (!cursor.TryOpen(context, token.Value, plain))
            throw new ArgumentException("The continuation does not match this plan results request.", "from");
        return new(Encoding.ASCII.GetString(plain));
    }
}
