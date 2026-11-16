using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfRuntimeHistoryStoreTests
{
    [Theory]
    [InlineData("malformed-head", "none")] [InlineData("malformed-head", "access")] [InlineData("malformed-head", "cancel")]
    [InlineData("invalid-data", "none")] [InlineData("invalid-data", "access")] [InlineData("invalid-data", "cancel")]
    [InlineData("io", "none")] [InlineData("io", "access")] [InlineData("io", "cancel")]
    [InlineData("provider", "none")] [InlineData("provider", "access")] [InlineData("provider", "cancel")]
    public async Task Failed_pending_reads_recheck_caller_cancellation_and_captured_access_before_classifying_diagnostics(string failureKind, string retirement)
    {
        await using var db = await Database.Create(); await db.Append(new Fact("event", "a"));
        if (failureKind == "malformed-head")
        {
            await using var corrupt = db.Open();
            await corrupt.Set<RuntimeHistoryHeadEntity>().ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ContentJson, "{"));
        }
        Exception? cause = failureKind switch
        {
            "invalid-data" => new InvalidDataException("controlled invalid retained data"),
            "io" => new IOException("controlled provider IO"),
            "provider" => new SqliteException("controlled provider failure", 5),
            _ => null
        };
        using var canceled = new CancellationTokenSource();
        var hook = new AfterHead(() =>
        {
            if (retirement == "access") db.Access.Current = PersistenceAccessContext.Scoped(new("scope"));
            else if (retirement == "cancel") canceled.Cancel();
            if (cause is not null) throw cause;
        });
        await using var context = db.Open(hook);
        Task Read() => db.Store(context).FindHeadAsync("run", canceled.Token).AsTask();
        if (retirement == "cancel")
        {
            var failure = await Assert.ThrowsAnyAsync<OperationCanceledException>(Read);
            Assert.Equal(canceled.Token, failure.CancellationToken);
        }
        else if (retirement == "access")
        {
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(Read);
            Assert.Contains("persistence access changed", failure.Message);
        }
        else if (failureKind is "malformed-head" or "invalid-data")
        {
            var failure = await Assert.ThrowsAsync<RuntimeHistoryIntegrityException>(Read);
            if (cause is not null) Assert.Same(cause, failure.InnerException);
        }
        else
        {
            var failure = await Assert.ThrowsAsync<RuntimeHistoryUnavailableException>(Read);
            Assert.Equal("run", failure.WorkflowExecutionId); Assert.Equal("find-head", failure.Operation);
            Assert.Same(cause, failure.InnerException);
        }
        Assert.True(hook.Fired); Assert.Null(context.Database.CurrentTransaction);
        Assert.Empty(context.ChangeTracker.Entries());
    }
}
