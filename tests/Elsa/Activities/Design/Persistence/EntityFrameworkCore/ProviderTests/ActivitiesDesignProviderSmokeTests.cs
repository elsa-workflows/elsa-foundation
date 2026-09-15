using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore.Stores;
using System.Data;
using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Elsa.Activities.Design.Persistence.EntityFrameworkCore.ProviderTests;

[Collection(ActivitiesDesignPostgreSqlFixture.CollectionName)]
public sealed class ActivitiesDesignPostgreSqlSmokeTests(ActivitiesDesignPostgreSqlFixture fixture)
{
    [SkippableFact]
    public Task PostgreSql_live_schema_crud_query_transaction_and_concurrency() =>
        ActivitiesDesignProviderSmoke.RunAsync(
            fixture,
            (connection) => new ActivitiesDesignPostgreSqlDbContext(
                new DbContextOptionsBuilder<ActivitiesDesignPostgreSqlDbContext>().UseNpgsql(connection).Options),
            ActivitiesDesignPostgreSqlDbContext.ExpectedProviderName);

    [SkippableFact]
    public Task PostgreSql_conditional_token_fences() =>
        ActivitiesDesignProviderSmoke.RunFenceAsync(fixture, (connection, interceptor) =>
        {
            var options = new DbContextOptionsBuilder<ActivitiesDesignPostgreSqlDbContext>().UseNpgsql(connection);
            if (interceptor is not null) options.AddInterceptors(interceptor);
            return new ActivitiesDesignPostgreSqlDbContext(options.Options);
        });
}

[Collection(ActivitiesDesignSqlServerFixture.CollectionName)]
public sealed class ActivitiesDesignSqlServerSmokeTests(ActivitiesDesignSqlServerFixture fixture)
{
    [SkippableFact]
    public Task SqlServer_live_schema_crud_query_transaction_and_concurrency() =>
        ActivitiesDesignProviderSmoke.RunAsync(
            fixture,
            (connection) => new ActivitiesDesignSqlServerDbContext(
                new DbContextOptionsBuilder<ActivitiesDesignSqlServerDbContext>().UseSqlServer(connection).Options),
            ActivitiesDesignSqlServerDbContext.ExpectedProviderName);

    [SkippableFact]
    public Task SqlServer_conditional_token_fences() =>
        ActivitiesDesignProviderSmoke.RunFenceAsync(fixture, (connection, interceptor) =>
        {
            var options = new DbContextOptionsBuilder<ActivitiesDesignSqlServerDbContext>().UseSqlServer(connection);
            if (interceptor is not null) options.AddInterceptors(interceptor);
            return new ActivitiesDesignSqlServerDbContext(options.Options);
        });
}

[Collection(ActivitiesDesignMySqlFixture.CollectionName)]
public sealed class ActivitiesDesignMySqlSmokeTests(ActivitiesDesignMySqlFixture fixture)
{
    [SkippableFact]
    public Task MySql_live_schema_crud_query_transaction_and_concurrency() =>
        ActivitiesDesignProviderSmoke.RunAsync(
            fixture,
            (connection) => new ActivitiesDesignMySqlDbContext(
                new DbContextOptionsBuilder<ActivitiesDesignMySqlDbContext>().UseMySQL(connection).Options),
            ActivitiesDesignMySqlDbContext.ExpectedProviderName);

    [SkippableFact]
    public Task MySql_conditional_token_fences() =>
        ActivitiesDesignProviderSmoke.RunFenceAsync(fixture, (connection, interceptor) =>
        {
            var options = new DbContextOptionsBuilder<ActivitiesDesignMySqlDbContext>().UseMySQL(connection);
            if (interceptor is not null) options.AddInterceptors(interceptor);
            return new ActivitiesDesignMySqlDbContext(options.Options);
        });
}

internal static class ActivitiesDesignProviderSmoke
{
    public static async Task RunFenceAsync(
        ActivitiesDesignProviderFixture fixture,
        Func<string, DbTransactionInterceptor?, ActivitiesDesignDbContext> createContext)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Native provider is unavailable.");
        var suffix = Guid.NewGuid().ToString("N");
        var authoringId = $"authoring-fence-{suffix}";
        var definitionId = $"definition-fence-{suffix}";
        var draftId = $"draft-fence-{suffix}";
        var connectionString = fixture.ConnectionString;

        await using (var seed = createContext(connectionString, null))
        {
            await seed.Database.EnsureCreatedAsync();
            seed.ActivityDefinitionAuthoringStates.Add(new ActivityDefinitionAuthoringState
            {
                Id = authoringId, DefinitionId = definitionId, TenantId = "tenant-a",
                ContentAuthority = new(ActivityContentAuthorityKind.Design, "design")
            });
            await seed.SaveChangesAsync();
        }

        var barrier = new ProviderTransactionStartBarrier();
        await using var first = createContext(connectionString, barrier);
        await using var second = createContext(connectionString, null);
        barrier.BeforeContinue = async () =>
        {
            barrier.Enabled = false;
            var authoring = await second.ActivityDefinitionAuthoringStates.SingleAsync(x => x.Id == authoringId);
            authoring.HeadVersionId = "published-v1";
            await second.SaveChangesAsync();
        };
        barrier.Enabled = true;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => new EfActivityDesignStores(first).ExecuteAsync(
            new CreateActivityDraftRequest(Draft(draftId, definitionId, "tenant-a", 1), Layout(draftId, "tenant-a", 1), null)));
        Assert.Null(await first.ActivityDefinitionDrafts.SingleOrDefaultAsync(x => x.Id == draftId));

        await new EfActivityDesignStores(first).ExecuteAsync(new CreateActivityDraftRequest(
            Draft($"draft-current-{suffix}", definitionId, "tenant-a", 1), Layout($"draft-current-{suffix}", "tenant-a", 1), "published-v1"));

        var sourceId = $"source-fence-{suffix}";
        await using (var sourceSeed = createContext(connectionString, null))
        {
            sourceSeed.ActivityDefinitionDrafts.Add(Draft(sourceId, definitionId, "tenant-a", 4, "v1"));
            await sourceSeed.SaveChangesAsync();
        }
        await using var stale = createContext(connectionString, null);
        var staleSource = await stale.ActivityDefinitionDrafts.SingleAsync(x => x.Id == sourceId);
        await new EfActivityDesignStores(stale).ExecuteAsync(new ActivityDraftValidationState { Id = $"validation-current-{suffix}", DraftId = sourceId, TenantId = "tenant-a", Revision = 4, Diagnostics = [] });
        var staleToken = stale.Entry(staleSource).Property<byte[]>("ConcurrencyToken").OriginalValue;
        await using (var winner = createContext(connectionString, null))
        {
            var source = await winner.ActivityDefinitionDrafts.SingleAsync(x => x.Id == sourceId);
            source.Revision = 5;
            await winner.SaveChangesAsync();
        }
        var affected = await stale.ActivityDefinitionDrafts
            .Where(x => x.Id == sourceId && EF.Property<byte[]>(x, "ConcurrencyToken") == staleToken)
            .ExecuteUpdateAsync(updates => updates.SetProperty(x => EF.Property<byte[]>(x, "ConcurrencyToken"), Guid.NewGuid().ToByteArray()));
        Assert.Equal(0, affected);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => new EfActivityDesignStores(stale).ExecuteAsync(new ActivityDraftValidationState { Id = $"validation-stale-{suffix}", DraftId = sourceId, TenantId = "tenant-a", Revision = 4, Diagnostics = [] }));
        Assert.Null(await stale.ActivityDraftValidations.SingleOrDefaultAsync(x => x.Id == $"validation-stale-{suffix}"));

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => new EfActivityDesignStores(stale).ExecuteAsync(
            new CreateActivityDraftConflictCopyRequest(sourceId, 4, Draft($"copy-stale-{suffix}", definitionId, "tenant-a", 5, "v1"), Layout($"copy-stale-{suffix}", "tenant-a", 5))));
        Assert.Null(await stale.ActivityDefinitionDrafts.SingleOrDefaultAsync(x => x.Id == $"copy-stale-{suffix}"));
        await new EfActivityDesignStores(stale).ExecuteAsync(new CreateActivityDraftConflictCopyRequest(
            sourceId, 5, Draft($"copy-current-{suffix}", definitionId, "tenant-a", 5, "v1"), Layout($"copy-current-{suffix}", "tenant-a", 5)));
        Assert.NotNull(await stale.ActivityDefinitionDrafts.SingleOrDefaultAsync(x => x.Id == $"copy-current-{suffix}"));
    }

    public static async Task RunAsync(
        ActivitiesDesignProviderFixture fixture,
        Func<string, ActivitiesDesignDbContext> createContext,
        string expectedProviderName)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Native provider is unavailable.");
        var connectionString = fixture.ConnectionString;
        var suffix = Guid.NewGuid().ToString("N");

        await using (var context = createContext(connectionString))
        {
            Assert.Equal(expectedProviderName, context.Database.ProviderName);
            await context.Database.EnsureCreatedAsync();
            await RunRetentionSmokeAsync(context, suffix);
            await AssertTenantKeyIsDatabaseRequiredAsync(context, suffix);
            await RunOrdinalPagingSmokeAsync(context, suffix);

            var definition = Definition($"crud-{suffix}", "tenant-a", $"Acme.Crud.{suffix}");
            context.ActivityDefinitions.Add(definition);
            await context.SaveChangesAsync();
            var roundTrip = await context.ActivityDefinitions.SingleAsync(x => x.Id == definition.Id);
            Assert.Equal(definition.ActivityTypeKey, roundTrip.ActivityTypeKey);

            roundTrip.Category = "Updated";
            await context.SaveChangesAsync();
            Assert.Equal("Updated", await context.ActivityDefinitions.Where(x => x.Id == definition.Id).Select(x => x.Category).SingleAsync());

            var projection = Projection($"projection-{suffix}", "tenant-a", ActivityContentAuthorityKind.ProviderSource);
            context.ActivityDefinitionManagementProjections.Add(projection);
            await context.SaveChangesAsync();
            Assert.Equal(projection.ResourceId, await context.ActivityDefinitionManagementProjections
                .Where(x => x.ResourceId == projection.ResourceId && x.ContentAuthorityKind == ActivityContentAuthorityKind.ProviderSource)
                .Select(x => x.ResourceId).SingleAsync());
            var providerMaterial = await context.ActivityDefinitionManagementProjections
                .Where(x => x.ResourceId == projection.ResourceId)
                .Select(x => new { x.ContentAuthorityJson, x.ContentAuthorityKind, x.ContentAuthorityIsValid }).SingleAsync();
            var adversarialIds = new[]
            {
                $"authority-numeric-{suffix}", $"authority-kind-{suffix}", $"authority-whitespace-{suffix}",
                $"authority-decoy-numeric-key-{suffix}", $"authority-decoy-boolean-key-{suffix}",
                $"authority-decoy-numeric-source-{suffix}", $"authority-decoy-boolean-source-{suffix}"
            };
            context.ActivityDefinitionManagementProjections.AddRange(adversarialIds.Select(id => Projection(id, "tenant-a", id.Contains("source", StringComparison.Ordinal) ? ActivityContentAuthorityKind.ProviderSource : ActivityContentAuthorityKind.Design)));
            await context.SaveChangesAsync();
            var projectionType = context.Model.FindEntityType(typeof(ActivityDefinitionManagementProjectionRevision))!;
            var sql = context.GetService<ISqlGenerationHelper>();
            var table = sql.DelimitIdentifier(projectionType.GetTableName()!, projectionType.GetSchema());
            var authorityColumn = sql.DelimitIdentifier(nameof(ActivityDefinitionManagementProjectionRevision.ContentAuthority));
            var canonicalColumn = sql.DelimitIdentifier(nameof(ActivityDefinitionManagementProjectionRevision.ContentAuthorityCanonicalJson));
            var authorityKeyTokenColumn = sql.DelimitIdentifier(nameof(ActivityDefinitionManagementProjectionRevision.ContentAuthorityAuthorityKeyJson));
            var sourceIdTokenColumn = sql.DelimitIdentifier(nameof(ActivityDefinitionManagementProjectionRevision.ContentAuthoritySourceIdJson));
            var integrityHashColumn = sql.DelimitIdentifier(nameof(ActivityDefinitionManagementProjectionRevision.ContentAuthorityIntegrityHash));
            var kindColumn = sql.DelimitIdentifier(nameof(ActivityDefinitionManagementProjectionRevision.ContentAuthorityKind));
            var resourceColumn = sql.DelimitIdentifier(nameof(ActivityDefinitionManagementProjectionRevision.ResourceId));
            Assert.True(providerMaterial.ContentAuthorityIsValid, $"Provider projection validity failed for {expectedProviderName}: {providerMaterial.ContentAuthorityJson}, kind={providerMaterial.ContentAuthorityKind}.");
            var escapedProjection = Projection($"projection-escaped-{suffix}", "tenant-a", ActivityContentAuthorityKind.ProviderSource, "é<&+\u2028\\\"", "source-é<&+\u2028\\\"");
            context.ActivityDefinitionManagementProjections.Add(escapedProjection);
            await context.SaveChangesAsync();
            Assert.True(await context.ActivityDefinitionManagementProjections
                .Where(x => x.ResourceId == escapedProjection.ResourceId)
                .Select(x => x.ContentAuthorityIsValid).SingleAsync());
            await context.Database.ExecuteSqlRawAsync(string.Concat("UPDATE ", table, " SET ", authorityKeyTokenColumn, " = {0} WHERE ", resourceColumn, " = {1}"), "\"tampered\"", escapedProjection.ResourceId);
            Assert.False(await context.ActivityDefinitionManagementProjections
                .Where(x => x.ResourceId == escapedProjection.ResourceId)
                .Select(x => x.ContentAuthorityIsValid).SingleAsync());
            await context.Database.ExecuteSqlRawAsync(string.Concat("UPDATE ", table, " SET ", authorityKeyTokenColumn, " = {0} WHERE ", resourceColumn, " = {1}"), JsonSerializer.Serialize(escapedProjection.ContentAuthority!.AuthorityKey), escapedProjection.ResourceId);
            Assert.True(await context.ActivityDefinitionManagementProjections
                .Where(x => x.ResourceId == escapedProjection.ResourceId)
                .Select(x => x.ContentAuthorityIsValid).SingleAsync());
            await context.Database.ExecuteSqlRawAsync(string.Concat("UPDATE ", table, " SET ", integrityHashColumn, " = {0} WHERE ", resourceColumn, " = {1}"), "tampered", escapedProjection.ResourceId);
            Assert.False(await context.ActivityDefinitionManagementProjections
                .Where(x => x.ResourceId == escapedProjection.ResourceId)
                .Select(x => x.ContentAuthorityIsValid).SingleAsync());
            if (expectedProviderName == ActivitiesDesignSqlServerDbContext.ExpectedProviderName)
            {
                // JSON_VALUE returns NULL for nvarchar(max) scalar results over 4000 characters.
                // Both decoded scalars therefore need to remain valid when their accepted domain
                // values exceed that limit, while an unbounded whitespace source remains invalid.
                var longValue = new string('é', 5001) + "<&+\u2028\\\"";
                var longProjection = Projection($"projection-long-{suffix}", "tenant-a", ActivityContentAuthorityKind.ProviderSource, longValue, longValue);
                context.ActivityDefinitionManagementProjections.Add(longProjection);
                await context.SaveChangesAsync();
                Assert.True(await context.ActivityDefinitionManagementProjections
                    .Where(x => x.ResourceId == longProjection.ResourceId)
                    .Select(x => x.ContentAuthorityIsValid).SingleAsync());

                var longWhitespaceProjection = Projection($"projection-long-whitespace-{suffix}", "tenant-a", ActivityContentAuthorityKind.ProviderSource, "provider", "source");
                context.ActivityDefinitionManagementProjections.Add(longWhitespaceProjection);
                await context.SaveChangesAsync();
                var decodedKeyColumn = sql.DelimitIdentifier(nameof(ActivityDefinitionManagementProjectionRevision.ContentAuthorityAuthorityKey));
                var decodedSourceColumn = sql.DelimitIdentifier(nameof(ActivityDefinitionManagementProjectionRevision.ContentAuthoritySourceId));
                var longWhitespace = new string('\u00a0', 5001);
                var longWhitespaceJson = JsonSerializer.Serialize(new ActivityContentAuthority(ActivityContentAuthorityKind.ProviderSource, "provider", longWhitespace));
                await context.Database.ExecuteSqlRawAsync(string.Concat("UPDATE ", table, " SET ", authorityColumn, " = {0}, ", canonicalColumn, " = {1}, ", sourceIdTokenColumn, " = {2}, ", decodedSourceColumn, " = {3} WHERE ", resourceColumn, " = {4}"), longWhitespaceJson, longWhitespaceJson, JsonSerializer.Serialize(longWhitespace), longWhitespace, longWhitespaceProjection.ResourceId);
                Assert.False(await context.ActivityDefinitionManagementProjections
                    .Where(x => x.ResourceId == longWhitespaceProjection.ResourceId)
                    .Select(x => x.ContentAuthorityIsValid).SingleAsync());

                // A long non-null source on a Design authority must not be mistaken for NULL
                // when the provider cannot return a large JSON scalar through JSON_VALUE.
                var designLongSourceProjection = Projection($"projection-design-long-source-{suffix}", "tenant-a", ActivityContentAuthorityKind.Design, "design", null);
                context.ActivityDefinitionManagementProjections.Add(designLongSourceProjection);
                await context.SaveChangesAsync();
                var designLongSourceJson = JsonSerializer.Serialize(new ActivityContentAuthority(ActivityContentAuthorityKind.Design, "design", longValue));
                await context.Database.ExecuteSqlRawAsync(string.Concat("UPDATE ", table, " SET ", authorityColumn, " = {0}, ", canonicalColumn, " = {1}, ", sourceIdTokenColumn, " = {2}, ", decodedSourceColumn, " = {3} WHERE ", resourceColumn, " = {4}"), designLongSourceJson, designLongSourceJson, JsonSerializer.Serialize(longValue), longValue, designLongSourceProjection.ResourceId);
                Assert.False(await context.ActivityDefinitionManagementProjections
                    .Where(x => x.ResourceId == designLongSourceProjection.ResourceId)
                    .Select(x => x.ContentAuthorityIsValid).SingleAsync());

                // A scalar-only edit must not become authoritative merely because raw and token
                // JSON still agree; the persisted digest binds every representation together.
                await context.Database.ExecuteSqlRawAsync(string.Concat("UPDATE ", table, " SET ", decodedKeyColumn, " = {0} WHERE ", resourceColumn, " = {1}"), "scalar-drift", longProjection.ResourceId);
                Assert.False(await context.ActivityDefinitionManagementProjections
                    .Where(x => x.ResourceId == longProjection.ResourceId)
                    .Select(x => x.ContentAuthorityIsValid).SingleAsync());
            }
            var numericAuthorityKey = "{\"kind\":0,\"authorityKey\":123,\"sourceId\":null}";
            await context.Database.ExecuteSqlRawAsync(string.Concat("UPDATE ", table, " SET ", authorityColumn, " = {0}, ", canonicalColumn, " = {1}, ", authorityKeyTokenColumn, " = {2}, ", sourceIdTokenColumn, " = {3} WHERE ", resourceColumn, " = {4}"), numericAuthorityKey, numericAuthorityKey, "123", "null", adversarialIds[0]);
            var invalidKind = "{\"kind\":42,\"authorityKey\":\"design\",\"sourceId\":null}";
            await context.Database.ExecuteSqlRawAsync(string.Concat("UPDATE ", table, " SET ", authorityColumn, " = {0}, ", canonicalColumn, " = {1}, ", kindColumn, " = {2} WHERE ", resourceColumn, " = {3}"), invalidKind, invalidKind, 42, adversarialIds[1]);
            var whitespaceAuthorityKey = JsonSerializer.Serialize(new ActivityContentAuthority(ActivityContentAuthorityKind.Design, "\t\n\u00a0"));
            await context.Database.ExecuteSqlRawAsync(string.Concat("UPDATE ", table, " SET ", authorityColumn, " = {0}, ", canonicalColumn, " = {1} WHERE ", resourceColumn, " = {2}"), whitespaceAuthorityKey, whitespaceAuthorityKey, adversarialIds[2]);
            var numericKeyDecoy = "{\"kind\":0,\"authorityKey\":123,\"sourceId\":null,\"decoy\":{\"authorityKey\":\"x\"}}";
            await context.Database.ExecuteSqlRawAsync(string.Concat("UPDATE ", table, " SET ", authorityColumn, " = {0}, ", canonicalColumn, " = {1}, ", authorityKeyTokenColumn, " = {2}, ", sourceIdTokenColumn, " = {3} WHERE ", resourceColumn, " = {4}"), numericKeyDecoy, numericKeyDecoy, "123", "null", adversarialIds[3]);
            var booleanKeyDecoy = "{\"kind\":0,\"authorityKey\":true,\"sourceId\":null,\"decoy\":{\"authorityKey\":\"x\"}}";
            await context.Database.ExecuteSqlRawAsync(string.Concat("UPDATE ", table, " SET ", authorityColumn, " = {0}, ", canonicalColumn, " = {1}, ", authorityKeyTokenColumn, " = {2}, ", sourceIdTokenColumn, " = {3} WHERE ", resourceColumn, " = {4}"), booleanKeyDecoy, booleanKeyDecoy, "true", "null", adversarialIds[4]);
            var numericSourceDecoy = "{\"kind\":1,\"authorityKey\":\"provider\",\"sourceId\":123,\"decoy\":{\"sourceId\":\"x\"}}";
            await context.Database.ExecuteSqlRawAsync(string.Concat("UPDATE ", table, " SET ", authorityColumn, " = {0}, ", canonicalColumn, " = {1}, ", authorityKeyTokenColumn, " = {2}, ", sourceIdTokenColumn, " = {3} WHERE ", resourceColumn, " = {4}"), numericSourceDecoy, numericSourceDecoy, "\"provider\"", "123", adversarialIds[5]);
            var booleanSourceDecoy = "{\"kind\":1,\"authorityKey\":\"provider\",\"sourceId\":true,\"decoy\":{\"sourceId\":\"x\"}}";
            await context.Database.ExecuteSqlRawAsync(string.Concat("UPDATE ", table, " SET ", authorityColumn, " = {0}, ", canonicalColumn, " = {1}, ", authorityKeyTokenColumn, " = {2}, ", sourceIdTokenColumn, " = {3} WHERE ", resourceColumn, " = {4}"), booleanSourceDecoy, booleanSourceDecoy, "\"provider\"", "true", adversarialIds[6]);
                Assert.Equal(0, await context.ActivityDefinitionManagementProjections
                .Where(x => adversarialIds.Contains(x.ResourceId) && x.ContentAuthorityIsValid)
                .CountAsync());
            if (expectedProviderName == ActivitiesDesignSqlServerDbContext.ExpectedProviderName)
            {
                var duplicateIds = new[]
                {
                    $"authority-duplicate-key-string-first-{suffix}", $"authority-duplicate-key-number-first-{suffix}", $"authority-trailing-key-decoy-{suffix}",
                    $"authority-duplicate-source-string-first-{suffix}", $"authority-duplicate-source-number-first-{suffix}", $"authority-trailing-source-decoy-{suffix}"
                };
                context.ActivityDefinitionManagementProjections.AddRange(duplicateIds.Select(id => Projection(id, "tenant-a", id.Contains("source", StringComparison.Ordinal) ? ActivityContentAuthorityKind.ProviderSource : ActivityContentAuthorityKind.Design)));
                await context.SaveChangesAsync();
                var duplicateCases = new[]
                {
                    "{\"kind\":0,\"authorityKey\":\"x\",\"authorityKey\":123,\"sourceId\":null}",
                    "{\"kind\":0,\"authorityKey\":123,\"authorityKey\":\"x\",\"sourceId\":null}",
                    "{\"kind\":0,\"authorityKey\":\"x\",\"decoy\":\"x\",\"sourceId\":null}",
                    "{\"kind\":1,\"authorityKey\":\"provider\",\"sourceId\":\"x\",\"sourceId\":123}",
                    "{\"kind\":1,\"authorityKey\":\"provider\",\"sourceId\":123,\"sourceId\":\"x\"}",
                    "{\"kind\":1,\"authorityKey\":\"provider\",\"sourceId\":\"x\",\"decoy\":\"x\"}"
                };
                for (var index = 0; index < duplicateIds.Length; index++)
                    await context.Database.ExecuteSqlRawAsync(string.Concat("UPDATE ", table, " SET ", authorityColumn, " = {0}, ", canonicalColumn, " = {1} WHERE ", resourceColumn, " = {2}"), duplicateCases[index], duplicateCases[index], duplicateIds[index]);
            Assert.Equal(0, await context.ActivityDefinitionManagementProjections
                    .Where(x => duplicateIds.Contains(x.ResourceId) && x.ContentAuthorityIsValid)
                    .CountAsync());
            }

            var atomic = new EfDesignAtomicWrite(context);
            var exactOperation = new EfDesignAtomicWriteRequest(
                new EfDesignOperationIdentity($"activity.test.{suffix}", $"exact-key-{suffix}"), "provider-exact", ["provider-smoke"], "tenant-a");
            var trailingKeyOperation = new EfDesignAtomicWriteRequest(
                new EfDesignOperationIdentity($"activity.test.{suffix}", $"exact-key-{suffix} "), "provider-key", ["provider-smoke"], "tenant-a");
            var trailingKindOperation = new EfDesignAtomicWriteRequest(
                new EfDesignOperationIdentity($"activity.test.{suffix} ", $"exact-key-{suffix}"), "provider-kind", ["provider-smoke"], "tenant-a");
            static EfDesignAtomicWriteStageResult AtomicResult(string value) =>
                EfDesignAtomicWriteStageResult.Accepted(value, $"{{\"result\":\"{value}\"}}");

            Assert.Equal(EfDesignAtomicWriteStatus.Committed,
                (await atomic.ExecuteAsync(exactOperation, (_, _) => Task.FromResult(AtomicResult("exact")))).Status);
            Assert.Equal(EfDesignAtomicWriteStatus.Committed,
                (await atomic.ExecuteAsync(trailingKeyOperation, (_, _) => Task.FromResult(AtomicResult("key")))).Status);
            Assert.Equal(EfDesignAtomicWriteStatus.Committed,
                (await atomic.ExecuteAsync(trailingKindOperation, (_, _) => Task.FromResult(AtomicResult("kind")))).Status);
            Assert.Equal(3, await context.ActivityDesignOperations.CountAsync());
            Assert.Equal(EfDesignAtomicWriteStatus.Replayed,
                (await atomic.ExecuteAsync(trailingKeyOperation, (_, _) => throw new InvalidOperationException("stage must not replay"))).Status);
            Assert.Equal(EfDesignAtomicWriteStatus.Conflict,
                (await atomic.ExecuteAsync(new EfDesignAtomicWriteRequest(trailingKeyOperation.Operation, "provider-key-conflict", trailingKeyOperation.MutatedUnits, "tenant-a"), (_, _) => throw new InvalidOperationException("stage must not run for a conflict"))).Status);

            var globalReceipt = Receipt($"receipt-global-{suffix}", null, $"fork-op-{suffix}");
            var literalGlobalReceipt = Receipt($"receipt-literal-global-{suffix}", "<global>", $"fork-op-{suffix}");
            var trailingReceipt = Receipt($"receipt-trailing-{suffix}", null, $"fork-op-{suffix} ", "provider-smoke-actor ");
            context.ActivityForkReceipts.AddRange(globalReceipt, literalGlobalReceipt, trailingReceipt);
            await context.SaveChangesAsync();
            Assert.Equal(ActivitiesDesignDbContext.GlobalTenantKey, globalReceipt.TenantScopeKey);
            Assert.Equal(ActivitiesDesignDbContext.NormalizeTenantKey("<global>"), literalGlobalReceipt.TenantScopeKey);
            context.ActivityForkReceipts.Add(Receipt($"receipt-global-duplicate-{suffix}", null, $"fork-op-{suffix}"));
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
            context.ChangeTracker.Clear();

            var longCandidateId = new string('x', 379);
            var longCandidate = Candidate($"candidate-long-{suffix}", longCandidateId, "tenant-a");
            var trailingCandidate = Candidate($"candidate-trailing-{suffix}", longCandidateId + " ", "tenant-a");
            var actorScopedCandidate = Candidate($"candidate-actor-scoped-{suffix}", longCandidateId, "tenant-a", "provider-smoke-actor ");
            context.ActivityForkCandidates.AddRange(longCandidate, trailingCandidate, actorScopedCandidate);
            await context.SaveChangesAsync();
            Assert.Equal(longCandidateId, await context.ActivityForkCandidates.Where(x => x.Id == longCandidate.Id).Select(x => x.CandidateId).SingleAsync());
            Assert.Equal(longCandidateId + " ", await context.ActivityForkCandidates.Where(x => x.Id == trailingCandidate.Id).Select(x => x.CandidateId).SingleAsync());
            Assert.Equal("provider-smoke-actor ", await context.ActivityForkCandidates.Where(x => x.Id == actorScopedCandidate.Id).Select(x => x.ActorId).SingleAsync());

            var refreshedRoundTrip = await context.ActivityDefinitions.SingleAsync(x => x.Id == definition.Id);
            context.ActivityDefinitions.Remove(refreshedRoundTrip);
            await context.SaveChangesAsync();
            Assert.False(await context.ActivityDefinitions.AnyAsync(x => x.Id == definition.Id));
        }

        var rollbackId = $"rollback-{suffix}";
        await using (var context = createContext(connectionString))
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            context.ActivityDefinitions.Add(Definition(rollbackId, "tenant-a", $"Acme.Rollback.{suffix}"));
            await context.SaveChangesAsync();
            await transaction.RollbackAsync();
        }
        await using (var verification = createContext(connectionString))
            Assert.False(await verification.ActivityDefinitions.AnyAsync(x => x.Id == rollbackId));

        var concurrencyId = $"concurrency-{suffix}";
        await using (var seed = createContext(connectionString))
        {
            seed.ActivityDefinitions.Add(Definition(concurrencyId, "tenant-a", $"Acme.Concurrency.{suffix}"));
            await seed.SaveChangesAsync();
        }
        await using var leftContext = createContext(connectionString);
        await using var rightContext = createContext(connectionString);
        var left = await leftContext.ActivityDefinitions.SingleAsync(x => x.Id == concurrencyId);
        var right = await rightContext.ActivityDefinitions.SingleAsync(x => x.Id == concurrencyId);
        left.Category = "Left";
        right.Category = "Right";
        await leftContext.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => rightContext.SaveChangesAsync());

        var globalSuffix = Guid.NewGuid().ToString("N");
        await using var firstGlobalContext = createContext(connectionString);
        await using var secondGlobalContext = createContext(connectionString);
        firstGlobalContext.ActivityDefinitions.Add(Definition($"global-1-{globalSuffix}", null, $"Acme.Global.{globalSuffix}"));
        secondGlobalContext.ActivityDefinitions.Add(Definition($"global-2-{globalSuffix}", null, $"Acme.Global.{globalSuffix}"));
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstSave = SaveAfterGateAsync(firstGlobalContext, gate, firstReady);
        var secondSave = SaveAfterGateAsync(secondGlobalContext, gate, secondReady);
        await Task.WhenAll(firstReady.Task, secondReady.Task);
        gate.SetResult();
        var outcomes = await Task.WhenAll(firstSave, secondSave);
        Assert.Equal(1, outcomes.Count(exception => exception is null));
        Assert.Equal(1, outcomes.Count(exception => exception is DbUpdateException));
    }

    private static async Task<Exception?> SaveAfterGateAsync(
        ActivitiesDesignDbContext context,
        TaskCompletionSource gate,
        TaskCompletionSource ready)
    {
        ready.SetResult();
        await gate.Task;
        try
        {
            await context.SaveChangesAsync();
            return null;
        }
        catch (DbUpdateException exception)
        {
            return exception;
        }
    }

    private static async Task AssertTenantKeyIsDatabaseRequiredAsync(ActivitiesDesignDbContext context, string suffix)
    {
        var entityType = context.Model.FindEntityType(typeof(ActivityDefinition))!;
        var sqlHelper = context.GetService<ISqlGenerationHelper>();
        var table = sqlHelper.DelimitIdentifier(entityType.GetTableName()!, entityType.GetSchema());
        var columns = string.Join(", ", new[] { "Id", "TenantId", "ActivityTypeKey", "Category", "CreatedAt", "LastModifiedAt" }.Select(name => sqlHelper.DelimitIdentifier(name)));
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"INSERT INTO {table} ({columns}) VALUES (@id, @tenant, @type, @category, @created, @modified)";
        AddParameter(command, "@id", $"raw-missing-tenant-key-{suffix}");
        AddParameter(command, "@tenant", "tenant-a");
        AddParameter(command, "@type", $"Acme.RawMissingTenantKey.{suffix}");
        AddParameter(command, "@category", "Tests");
        AddParameter(command, "@created", DateTime.UtcNow, DbType.DateTime);
        AddParameter(command, "@modified", DateTime.UtcNow, DbType.DateTime);
        await context.Database.OpenConnectionAsync();
        try
        {
            await Assert.ThrowsAnyAsync<DbException>(() => command.ExecuteNonQueryAsync());
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
        Assert.False(await context.ActivityDefinitions.AnyAsync(x => x.ActivityTypeKey == $"Acme.RawMissingTenantKey.{suffix}"));
    }

    private static void AddParameter(DbCommand command, string name, object value, DbType? dbType = null)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        if (dbType is { } type)
            parameter.DbType = type;
        command.Parameters.Add(parameter);
    }

    private static ActivityDefinition Definition(string id, string? tenantId, string activityTypeKey) =>
        new() { Id = id, TenantId = tenantId, ActivityTypeKey = activityTypeKey, Category = "Tests" };

    private static async Task RunRetentionSmokeAsync(ActivitiesDesignDbContext context, string suffix)
    {
        var watermark = await context.ActivityManagementProjectionWatermarks.SingleOrDefaultAsync(x => x.Id == ActivityManagementProjectionWatermark.CurrentId);
        var baseSequence = watermark?.Sequence ?? 0;
        var targetSequence = checked(Math.Max(baseSequence + 1, (watermark?.RetainedFromSequence ?? 1) + 1));
        if (watermark is null)
            context.ActivityManagementProjectionWatermarks.Add(watermark = new() { Id = ActivityManagementProjectionWatermark.CurrentId, RetainedFromSequence = 1 });
        watermark.Sequence = checked(targetSequence + 1);
        watermark.AdvancedAt = DateTimeOffset.UtcNow;
        var expiredSnapshotSequence = targetSequence - 1;
        if (!await context.ActivityManagementProjectionSnapshots.AnyAsync(x => x.Sequence == expiredSnapshotSequence))
            context.ActivityManagementProjectionSnapshots.Add(new ActivityManagementProjectionSnapshot
            {
                Id = expiredSnapshotSequence.ToString("D20"), Sequence = expiredSnapshotSequence, AsOf = DateTimeOffset.UtcNow
            });
        var expired = Projection($"retention-{suffix}", "tenant-a", ActivityContentAuthorityKind.Design, sequence: targetSequence, validToSequenceExclusive: targetSequence);
        context.ActivityDefinitionManagementProjections.Add(expired);
        await context.SaveChangesAsync();

        var retention = new EfActivityManagementProjectionRetention(context);
        await retention.ExpireBeforeAsync(targetSequence, DateTimeOffset.UtcNow);
        Assert.False(await context.ActivityDefinitionManagementProjections.AnyAsync(x => x.ResourceId == expired.ResourceId));
        Assert.False(await context.ActivityManagementProjectionSnapshots.AnyAsync(x => x.Sequence == expiredSnapshotSequence));
        Assert.Equal(targetSequence, await context.ActivityManagementProjectionWatermarks.Select(x => x.RetainedFromSequence).SingleAsync());
        await retention.ExpireBeforeAsync(targetSequence, DateTimeOffset.UtcNow);
        Assert.Equal(targetSequence, await context.ActivityManagementProjectionWatermarks.Select(x => x.RetainedFromSequence).SingleAsync());
    }

    private static async Task RunOrdinalPagingSmokeAsync(ActivitiesDesignDbContext context, string suffix)
    {
        var marker = $"CASESMOKE{suffix.ToUpperInvariant()}";
        var ids = Enumerable.Range(0, 300)
            .SelectMany(index => new[] { $"{marker}-{index:D4}", $"{marker.ToLowerInvariant()}-{index:D4}" })
            .ToArray();
        var snapshotSequence = await context.ActivityManagementProjectionWatermarks
            .Select(x => x.Sequence).SingleAsync();
        if (!await context.ActivityManagementProjectionSnapshots.AnyAsync(x => x.Sequence == snapshotSequence))
            context.ActivityManagementProjectionSnapshots.Add(new ActivityManagementProjectionSnapshot
            {
                Id = snapshotSequence.ToString("D20"), Sequence = snapshotSequence, AsOf = DateTimeOffset.UtcNow
            });
        context.ActivityDefinitionManagementProjections.AddRange(ids.Select(id =>
            Projection(id, "tenant-a", ActivityContentAuthorityKind.Design, sortKey: marker, searchText: marker)));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var store = new EfActivityDesignStores(context);
        var first = await store.ReadDefinitionsAsync(new ActivityManagementProjectionPageQuery("tenant-a", snapshotSequence, 0, 500, Search: marker));
        var second = await store.ReadDefinitionsAsync(new ActivityManagementProjectionPageQuery("tenant-a", snapshotSequence, 500, 100, Search: marker));
        var firstAgain = await store.ReadDefinitionsAsync(new ActivityManagementProjectionPageQuery("tenant-a", snapshotSequence, 0, 500, Search: marker));
        var secondAgain = await store.ReadDefinitionsAsync(new ActivityManagementProjectionPageQuery("tenant-a", snapshotSequence, 500, 100, Search: marker));
        Assert.Equal(ids.Length, first.TotalCount);
        Assert.Equal(ids.Length, second.TotalCount);
        var combined = first.Items.Concat(second.Items).Select(x => x.ResourceId).ToArray();
        Assert.Equal(ids.OrderBy(id => id, StringComparer.Ordinal), combined.OrderBy(id => id, StringComparer.Ordinal));
        Assert.Equal(combined, firstAgain.Items.Concat(secondAgain.Items).Select(x => x.ResourceId));
        Assert.Equal(500, first.NextOffset);
        Assert.Null(second.NextOffset);
    }

    private static ActivityDefinitionManagementProjectionRevision Projection(string id, string tenantId, ActivityContentAuthorityKind authority, string? authorityKey = null, string? sourceId = null, long sequence = 1, long validToSequenceExclusive = long.MaxValue, string? sortKey = null, string? searchText = null) =>
        new()
        {
            Id = id, ResourceId = id, DefinitionId = id, TenantId = tenantId,
            ValidFromSequence = sequence, ValidToSequenceExclusive = validToSequenceExclusive,
            ValidFromKey = sequence.ToString("D20"), ValidToKey = validToSequenceExclusive.ToString("D20"),
            VisibilityKey = tenantId, SortKey = sortKey ?? id, SearchText = searchText ?? id, ActivityTypeKey = "Acme.Test", Category = "Tests",
            ContentAuthority = new(authority, authorityKey ?? (authority == ActivityContentAuthorityKind.Design ? "design" : "provider"), sourceId),
            ContentAuthorityKind = authority, UpdatedAt = DateTimeOffset.UtcNow
        };

    private static ActivityForkReceipt Receipt(string id, string? tenantId, string idempotencyKey, string actorId = "provider-smoke-actor") =>
        new()
        {
            Id = id, TenantId = tenantId, IdempotencyKey = idempotencyKey, CandidateId = $"candidate-{id}", PublicCandidateId = $"public-{id}", RequestFingerprint = $"request-{id}", AccessBindingFingerprint = $"access-{id}", ActorId = actorId, AuthorizationProfile = "provider-smoke-profile", DefinitionId = $"definition-{id}", ActivityTypeKey = $"Acme.Fork.{id}", DraftId = $"draft-{id}", DefinitionMaterialJson = "{}", AuthoringState = new ActivityDefinitionAuthoringState { Id = $"authoring-{id}", TenantId = tenantId, DefinitionId = $"definition-{id}", ContentAuthority = new(ActivityContentAuthorityKind.Design, "design") }, Draft = new ActivityDefinitionDraft { Id = $"draft-{id}", DefinitionId = $"definition-{id}", TenantId = tenantId, Revision = 1, State = new(new("1", [], [], []), new("provider", "1", JsonDocument.Parse("{}").RootElement.Clone()), new Dictionary<string, string>()) }, Layout = new ActivityDefinitionDraftLayout { Id = $"layout-{id}", DraftId = $"draft-{id}", TenantId = tenantId, Revision = 1, Records = [] }, MigrationDiagnostics = [], AppliedAt = DateTimeOffset.UtcNow
        };

    private static ActivityForkCandidate Candidate(string id, string candidateId, string tenantId, string actorId = "provider-smoke-actor") =>
        new()
        {
            Id = id, TenantId = tenantId, CandidateId = candidateId, PreviewIdempotencyKey = $"preview-{id}", RequestFingerprint = $"request-{id}", AccessBindingFingerprint = $"access-{id}", ActorId = actorId, AuthorizationProfile = "provider-smoke-profile", SourceDefinitionId = $"source-definition-{id}", SourceVersionId = $"source-version-{id}", SourceVersion = "1.0.0", SourceLifecycle = ActivityDefinitionVersionLifecycle.Active, SourceProviderFingerprint = "source-provider", TargetProviderFingerprint = "target-provider", SourceContractFingerprint = "source-contract", TargetContractFingerprint = "target-contract", ReservedDefinition = new ActivityDefinition { Id = $"definition-{id}", TenantId = tenantId, ActivityTypeKey = $"Acme.Candidate.{id}", Category = "Tests" }, ReservedAuthoringState = new ActivityDefinitionAuthoringState { Id = $"authoring-{id}", TenantId = tenantId, DefinitionId = $"definition-{id}", ContentAuthority = new(ActivityContentAuthorityKind.Design, "design") }, ReservedDraft = new ActivityDefinitionDraft { Id = $"draft-{id}", DefinitionId = $"definition-{id}", TenantId = tenantId, Revision = 1, State = new(new("1", [], [], []), new("provider", "1", JsonDocument.Parse("{}").RootElement.Clone()), new Dictionary<string, string>()) }, ReservedLayout = new ActivityDefinitionDraftLayout { Id = $"layout-{id}", DraftId = $"draft-{id}", TenantId = tenantId, Revision = 1, Records = [] }, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5), RetainUntil = DateTimeOffset.UtcNow.AddDays(1), RetentionKey = DateTimeOffset.UtcNow.AddDays(1).UtcDateTime.ToString("O"), MigrationDiagnostics = []
        };

    private static ActivityDefinitionDraft Draft(string id, string definitionId, string tenantId, long revision, string? sourceVersionId = null) =>
        new() { Id = id, DefinitionId = definitionId, TenantId = tenantId, Revision = revision, SourceVersionId = sourceVersionId, State = new(new("1", [], [], []), new("provider", "1", JsonDocument.Parse("{}").RootElement.Clone()), new Dictionary<string, string>()) };

    private static ActivityDefinitionDraftLayout Layout(string draftId, string tenantId, long revision) =>
        new() { Id = $"layout-{draftId}", DraftId = draftId, TenantId = tenantId, Revision = revision, Records = [] };

    private sealed class ProviderTransactionStartBarrier : DbTransactionInterceptor
    {
        private int paused;

        public bool Enabled { get; set; }
        public Func<Task>? BeforeContinue { get; set; }

        public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection,
            TransactionStartingEventData eventData,
            InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default)
        {
            if (Enabled && Interlocked.Exchange(ref paused, 1) == 0 && BeforeContinue is not null)
                await BeforeContinue();
            return await base.TransactionStartingAsync(connection, eventData, result, cancellationToken);
        }
    }
}
