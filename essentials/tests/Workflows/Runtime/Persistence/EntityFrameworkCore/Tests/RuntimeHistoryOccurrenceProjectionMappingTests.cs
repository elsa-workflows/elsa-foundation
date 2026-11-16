using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.Schema;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Configuration;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Projection = Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeHistoryOccurrenceProjection;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class RuntimeHistoryOccurrenceProjectionMappingTests
{
    private static EfSchemaFamilyDescriptor Declaration() => new("OccurrenceMappingControls", "Workflows.Runtime", "1.0.0",
        typeof(RuntimeHistoryOccurrenceProjectionMappingTests).Assembly)
    { ContentColumns = new[] { typeof(RuntimeHistoryOccurrenceEntity), typeof(RuntimeHistoryOccurrenceCounterEntity),
        typeof(RuntimeHistoryOccurrenceMembershipEntity) }.Select(type => new EfSchemaColumn(type, "ContentJson")).ToArray() };
    private static readonly EfSchemaChain Chain = EfSchemaChain.For(Declaration());

    [Theory]
    [InlineData(0, "bad")] [InlineData(0, "AQ==")]
    [InlineData(1, "bad")] [InlineData(1, "AQ==")]
    [InlineData(2, "bad")] [InlineData(2, "AQ==")]
    public async Task Malformed_Base64_and_incomplete_UTF16_are_integrity_failures_preserving_the_decoder_cause(int shape, string encoded)
    {
        var rows = await Seed(); var node = JsonNode.Parse(rows.Content(shape))!;
        node[shape == 1 ? "activityScope" : "activity"] = encoded; rows.Content(shape, node.ToJsonString());
        var failure = Assert.Throws<RuntimeHistoryIntegrityException>(() => rows.Read(shape));
        Assert.Contains("'run'", failure.Message, StringComparison.Ordinal);
        var decoder = Assert.IsType<InvalidOperationException>(failure.InnerException);
        Assert.IsType<FormatException>(decoder.InnerException);
    }

    [Theory]
    [InlineData(0, 0)] [InlineData(0, 1)] [InlineData(0, 2)]
    [InlineData(1, 0)] [InlineData(1, 1)] [InlineData(1, 2)]
    [InlineData(2, 0)] [InlineData(2, 1)] [InlineData(2, 2)]
    public async Task Null_or_malformed_readable_content_preserves_its_cause_under_domain_integrity(int shape, int fault)
    {
        var rows = await Seed(); rows.Content(shape, fault == 0 ? null : fault == 1 ? "null" : "{broken");
        var failure = Assert.Throws<RuntimeHistoryIntegrityException>(() => rows.Read(shape));
        Assert.Contains("'run'", failure.Message, StringComparison.Ordinal);
        if (fault == 2) Assert.IsAssignableFrom<JsonException>(failure.InnerException);
        else Assert.IsType<InvalidDataException>(failure.InnerException);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task Readable_row_upcaster_failure_preserves_the_original_cause_under_domain_integrity(int shape)
    {
        var rows = await Seed(); rows.Stamp(shape, "0.9.0");
        var chain = EfSchemaChain.For(Declaration() with { Upcasters = [EfSchemaFamilyCatalog.DescribeUpcaster(typeof(FailingUpgrade))] });
        var failure = Assert.Throws<RuntimeHistoryIntegrityException>(() => rows.Read(shape, chain));
        Assert.Contains("'run'", failure.Message, StringComparison.Ordinal);
        var upcast = Assert.IsType<InvalidDataException>(failure.InnerException);
        Assert.IsType<ProbeFailure>(upcast.InnerException);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task Unreadable_schema_is_checked_before_malformed_identity_content_and_upcasting(int shape)
    {
        var rows = await Seed(); rows.Stamp(shape, "9.0.0"); rows.Content(shape, "{broken");
        var failure = Assert.Throws<EfSchemaVersionSkewException>(() => rows.Read(shape));
        Assert.Equal("9.0.0", failure.Found); Assert.Null(failure.InnerException);
    }

    [EfSchemaUpcaster("0.9.0", "1.0.0")]
    public sealed class FailingUpgrade : IEfSchemaUpcaster
    { public EfSchemaRowContent Upcast(EfSchemaRowContent row) => throw new ProbeFailure(); }
    public sealed class ProbeFailure : Exception;

    // Obtain valid committed projection rows through the actual staging seam before introducing
    // one precise retained-data fault; the test does not duplicate private row construction.
    private static async Task<Rows> Seed()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new ProjectionContext(new DbContextOptionsBuilder<ProjectionContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            var observation = new RuntimeHistoryEvent("event", "run", 1, RuntimeHistoryEventCategory.ActivityLifecycle, "ActivityStarted",
                DateTimeOffset.UnixEpoch, "activity")
            { Authority = RuntimeHistoryAuthority.CommittedTransition, RecordedAt = DateTimeOffset.UnixEpoch,
                PhysicalCommitId = "projection-seed", ActivityAncestryKnown = true };
            await Projection.StageAsync(context, "scope", "run", Chain, [observation]); await context.SaveChangesAsync(); await transaction.CommitAsync();
        }
        return new(await context.Set<RuntimeHistoryOccurrenceEntity>().AsNoTracking().SingleAsync(),
            await context.Set<RuntimeHistoryOccurrenceCounterEntity>().AsNoTracking().SingleAsync(row => row.ActivityScopeId != null),
            await context.Set<RuntimeHistoryOccurrenceMembershipEntity>().AsNoTracking().SingleAsync(row => row.ActivityScopeId != null));
    }
    private sealed class ProjectionContext(DbContextOptions<ProjectionContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model)
        { RuntimeHistoryOccurrenceEntityConfiguration.Configure(model); model.IndexSchemaVersionStamps(); }
    }
    private sealed record Rows(RuntimeHistoryOccurrenceEntity Receipt, RuntimeHistoryOccurrenceCounterEntity Counter, RuntimeHistoryOccurrenceMembershipEntity Member)
    {
        internal object Read(int shape, EfSchemaChain? chain = null) => shape switch
        { 0 => Projection.ReadReceipt(Receipt, "scope", "run", chain ?? Chain), 1 => Projection.ReadCounter(Counter, "scope", "run", chain ?? Chain),
          _ => Projection.ReadMembership(Member, "scope", "run", chain ?? Chain) };
        internal string Content(int shape) => shape switch { 0 => Receipt.ContentJson, 1 => Counter.ContentJson, _ => Member.ContentJson };
        internal void Content(int shape, string? value)
        { if (shape == 0) Receipt.ContentJson = value!; else if (shape == 1) Counter.ContentJson = value!; else Member.ContentJson = value!; }
        internal void Stamp(int shape, string value)
        { if (shape == 0) Receipt.SchemaVersion = value; else if (shape == 1) Counter.SchemaVersion = value; else Member.SchemaVersion = value; }
    }
}
