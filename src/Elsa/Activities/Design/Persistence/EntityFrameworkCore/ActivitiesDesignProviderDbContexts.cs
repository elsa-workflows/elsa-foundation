using Elsa.Activities.Design.Persistence.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Activities.Design.Persistence.EntityFrameworkCore;

public sealed class ActivitiesDesignSqliteDbContext(DbContextOptions<ActivitiesDesignSqliteDbContext> options) : ActivitiesDesignDbContext(options)
{
    public const string ExpectedProviderName = Elsa.Persistence.EntityFramework.EfProviderNames.Sqlite;
    protected override void ConfigureProvider(ModelBuilder modelBuilder)
    {
        modelBuilder.UseCollation("BINARY");
        modelBuilder.Model.FindEntityType(typeof(ActivityDefinition))!.FindProperty("ConcurrencyToken")!.SetColumnType("BLOB");
        var authorityKey = "json_extract(\"ContentAuthority\", '$.authorityKey')";
        var sourceId = "json_extract(\"ContentAuthority\", '$.sourceId')";
        var integrityHash = "json_extract(\"ContentAuthority\", '$.integrityHash')";
        modelBuilder.Entity<ActivityDefinitionManagementProjectionRevision>().Property(x => x.ContentAuthorityIsValid)
            .HasComputedColumnSql($"CASE WHEN json_valid(\"ContentAuthority\") = 1 THEN CASE WHEN \"ContentAuthorityKind\" IN (0, 1) AND json_type(\"ContentAuthority\") = 'object' AND json_type(\"ContentAuthority\", '$.kind') = 'integer' AND json_extract(\"ContentAuthority\", '$.kind') = \"ContentAuthorityKind\" AND json_type(\"ContentAuthority\", '$.authorityKey') = 'text' AND \"ContentAuthorityAuthorityKey\" = {authorityKey} AND \"ContentAuthorityAuthorityKeyJson\" = json_quote(\"ContentAuthorityAuthorityKey\") AND {ActivityAuthoritySql.SqliteNonWhitespace(authorityKey)} AND (((json_type(\"ContentAuthority\", '$.sourceId') IS NULL OR json_type(\"ContentAuthority\", '$.sourceId') = 'null') AND \"ContentAuthoritySourceId\" IS NULL AND \"ContentAuthoritySourceIdJson\" = 'null') OR (json_type(\"ContentAuthority\", '$.sourceId') = 'text' AND \"ContentAuthoritySourceId\" = {sourceId} AND \"ContentAuthoritySourceIdJson\" = json_quote(\"ContentAuthoritySourceId\") AND {ActivityAuthoritySql.SqliteNonWhitespace(sourceId)})) AND json_type(\"ContentAuthority\", '$.integrityHash') = 'text' AND \"ContentAuthorityIntegrityHash\" = {integrityHash} AND (\"ContentAuthorityKind\" = 1 OR json_type(\"ContentAuthority\", '$.sourceId') IS NULL OR json_type(\"ContentAuthority\", '$.sourceId') = 'null') THEN 1 ELSE 0 END ELSE 0 END", stored: false);
    }
}

public sealed class ActivitiesDesignSqlServerDbContext(DbContextOptions<ActivitiesDesignSqlServerDbContext> options) : ActivitiesDesignDbContext(options)
{
    public const string ExpectedProviderName = Elsa.Persistence.EntityFramework.EfProviderNames.SqlServer;
    protected override void ConfigureProvider(ModelBuilder modelBuilder)
    {
        modelBuilder.UseCollation("Latin1_General_100_BIN2");
        modelBuilder.Model.GetEntityTypes().ToList().ForEach(entity => entity.FindProperty("ConcurrencyToken")?.SetColumnType("varbinary(16)"));
        // JSON_VALUE is limited to nvarchar(4000) on SQL Server. Use persisted nvarchar(max)
        // scalars for semantic checks; raw/token reconstruction proves canonical material and the
        // integrity digest detects scalar/token/raw drift before a row reaches paging.
        var authorityKey = "[ContentAuthorityAuthorityKey]";
        var sourceId = "[ContentAuthoritySourceId]";
        var authorityKeyToken = "[ContentAuthorityAuthorityKeyJson]";
        var sourceIdToken = "[ContentAuthoritySourceIdJson]";
        var integrityHashToken = "JSON_VALUE([ContentAuthority], '$.integrityHash')";
        var authorityIntegrityHash = "[ContentAuthorityIntegrityHash]";
        var rawWithoutIntegrity = "JSON_MODIFY([ContentAuthority], '$.integrityHash', NULL)";
        var canonicalWithoutIntegrity = "JSON_MODIFY([ContentAuthorityCanonicalJson], '$.integrityHash', NULL)";
        var hashMaterial = string.Join(", N'|', ", new[] { rawWithoutIntegrity, canonicalWithoutIntegrity, authorityKeyToken, sourceIdToken, authorityKey, sourceId, "CONVERT(nvarchar(20), [ContentAuthorityKind])" }.Select(ActivityAuthoritySql.SqlServerHashSegment));
        modelBuilder.Entity<ActivityDefinitionManagementProjectionRevision>().Property(x => x.ContentAuthorityIsValid)
            .HasComputedColumnSql($"CASE WHEN [ContentAuthority] IS NOT NULL AND ISJSON([ContentAuthority]) = 1 THEN CASE WHEN [ContentAuthorityKind] IN (0, 1) AND JSON_QUERY([ContentAuthority]) IS NOT NULL AND {authorityKey} IS NOT NULL AND {authorityKeyToken} IS NOT NULL AND LEFT({authorityKeyToken}, 1) = N'\"' AND RIGHT({authorityKeyToken}, 1) = N'\"' AND ISJSON(CONCAT(N'[', {authorityKeyToken}, N']')) = 1 AND (LEN({authorityKey}) > 4000 OR JSON_VALUE(CONCAT(N'[', {authorityKeyToken}, N']'), '$[0]') = {authorityKey}) AND ({sourceIdToken} IS NULL OR {sourceIdToken} = N'null' OR (LEFT({sourceIdToken}, 1) = N'\"' AND RIGHT({sourceIdToken}, 1) = N'\"' AND ISJSON(CONCAT(N'[', {sourceIdToken}, N']')) = 1 AND (LEN({sourceId}) > 4000 OR JSON_VALUE(CONCAT(N'[', {sourceIdToken}, N']'), '$[0]') = {sourceId}))) AND (({sourceIdToken} = N'null' AND {sourceId} IS NULL) OR ({sourceIdToken} <> N'null' AND {sourceId} IS NOT NULL)) AND {integrityHashToken} = {authorityIntegrityHash} AND {ActivityAuthoritySql.SqlServerNonWhitespace(authorityKey)} AND ({sourceId} IS NULL OR {ActivityAuthoritySql.SqlServerNonWhitespace(sourceId)}) AND ([ContentAuthorityKind] = 1 OR {sourceId} IS NULL) AND {authorityIntegrityHash} = CONVERT(varchar(64), HASHBYTES('SHA2_256', CONCAT({hashMaterial})), 2) THEN CONVERT(bit, 1) ELSE CONVERT(bit, 0) END ELSE CONVERT(bit, 0) END", stored: false);
        // SQL Server limits an index key to 900 bytes (nvarchar uses two bytes per
        // character). Preserve the provider-neutral lengths elsewhere, but proportionally
        // bound only composite SQL Server indexes so every declared key is model-valid.
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        foreach (var index in entity.GetIndexes())
        {
            var strings = index.Properties.Where(x => x.ClrType == typeof(string)).ToArray();
            var total = strings.Sum(x => x.GetMaxLength() ?? 450);
            if (total <= 450) continue;
            // TenantScopeKey is a fixed-width, provider-neutral discriminator. Keep its full
            // 66-character representation so global and opaque tenant scopes remain lossless;
            // proportionally bound only the other indexed strings to the remaining budget.
            var fixedLength = strings.Where(x => x.Name == "TenantScopeKey").Sum(x => x.GetMaxLength() ?? 450);
            var variable = strings.Where(x => x.Name != "TenantScopeKey").ToArray();
            var variableTotal = variable.Sum(x => x.GetMaxLength() ?? 450);
            foreach (var property in variable)
            {
                var current = property.GetMaxLength() ?? 450;
                property.SetMaxLength(Math.Max(1, current * (450 - fixedLength) / Math.Max(1, variableTotal)));
            }
        }
    }
}

public sealed class ActivitiesDesignPostgreSqlDbContext(DbContextOptions<ActivitiesDesignPostgreSqlDbContext> options) : ActivitiesDesignDbContext(options)
{
    public const string ExpectedProviderName = Elsa.Persistence.EntityFramework.EfProviderNames.PostgreSql;
    protected override void ConfigureProvider(ModelBuilder modelBuilder)
    {
        modelBuilder.UseCollation("C");
        modelBuilder.Model.GetEntityTypes().ToList().ForEach(entity => entity.FindProperty("ConcurrencyToken")?.SetColumnType("bytea"));
        var authorityKey = "\"ContentAuthority\"::jsonb ->> 'authorityKey'";
        var sourceId = "\"ContentAuthority\"::jsonb ->> 'sourceId'";
        var integrityHash = "\"ContentAuthority\"::jsonb ->> 'integrityHash'";
        modelBuilder.Entity<ActivityDefinitionManagementProjectionRevision>().Property(x => x.ContentAuthorityIsValid)
            .HasComputedColumnSql($"CASE WHEN \"ContentAuthority\" IS NOT NULL AND \"ContentAuthority\" IS JSON OBJECT THEN CASE WHEN \"ContentAuthorityKind\" IN (0, 1) AND jsonb_typeof(\"ContentAuthority\"::jsonb -> 'kind') = 'number' AND (\"ContentAuthority\"::jsonb ->> 'kind') ~ '^[01]$' AND (\"ContentAuthority\"::jsonb ->> 'kind')::integer = \"ContentAuthorityKind\" AND jsonb_typeof(\"ContentAuthority\"::jsonb -> 'authorityKey') = 'string' AND \"ContentAuthorityAuthorityKey\" = {authorityKey} AND jsonb_typeof(\"ContentAuthorityAuthorityKeyJson\"::jsonb) = 'string' AND (\"ContentAuthorityAuthorityKeyJson\"::jsonb #>> '{{}}') = \"ContentAuthorityAuthorityKey\" AND {ActivityAuthoritySql.PostgresNonWhitespace(authorityKey)} AND (((NOT (\"ContentAuthority\"::jsonb ? 'sourceId') OR jsonb_typeof(\"ContentAuthority\"::jsonb -> 'sourceId') = 'null') AND \"ContentAuthoritySourceId\" IS NULL AND \"ContentAuthoritySourceIdJson\" = 'null') OR (jsonb_typeof(\"ContentAuthority\"::jsonb -> 'sourceId') = 'string' AND \"ContentAuthoritySourceId\" = {sourceId} AND jsonb_typeof(\"ContentAuthoritySourceIdJson\"::jsonb) = 'string' AND (\"ContentAuthoritySourceIdJson\"::jsonb #>> '{{}}') = \"ContentAuthoritySourceId\" AND {ActivityAuthoritySql.PostgresNonWhitespace(sourceId)})) AND jsonb_typeof(\"ContentAuthority\"::jsonb -> 'integrityHash') = 'string' AND \"ContentAuthorityIntegrityHash\" = {integrityHash} AND (\"ContentAuthorityKind\" = 1 OR NOT (\"ContentAuthority\"::jsonb ? 'sourceId') OR jsonb_typeof(\"ContentAuthority\"::jsonb -> 'sourceId') = 'null') THEN true ELSE false END ELSE false END", stored: true);
    }
}

public sealed class ActivitiesDesignMySqlDbContext(DbContextOptions<ActivitiesDesignMySqlDbContext> options) : ActivitiesDesignDbContext(options)
{
    public const string ExpectedProviderName = Elsa.Persistence.EntityFramework.EfProviderNames.MySql;
    protected override void ConfigureProvider(ModelBuilder modelBuilder)
    {
        modelBuilder.UseCollation("utf8mb4_0900_bin");
        modelBuilder.Model.GetEntityTypes().ToList().ForEach(entity => entity.FindProperty("ConcurrencyToken")?.SetColumnType("varbinary(16)"));
        var authorityKey = "JSON_UNQUOTE(JSON_EXTRACT(`ContentAuthority`, '$.authorityKey'))";
        var sourceId = "JSON_UNQUOTE(JSON_EXTRACT(`ContentAuthority`, '$.sourceId'))";
        var integrityHash = "JSON_UNQUOTE(JSON_EXTRACT(`ContentAuthority`, '$.integrityHash'))";
        modelBuilder.Entity<ActivityDefinitionManagementProjectionRevision>().Property(x => x.ContentAuthorityIsValid)
            .HasComputedColumnSql($"CASE WHEN JSON_VALID(`ContentAuthority`) = 1 THEN CASE WHEN `ContentAuthorityKind` IN (0, 1) AND JSON_TYPE(`ContentAuthority`) = 'OBJECT' AND JSON_TYPE(JSON_EXTRACT(`ContentAuthority`, '$.kind')) = 'INTEGER' AND CAST(JSON_UNQUOTE(JSON_EXTRACT(`ContentAuthority`, '$.kind')) AS SIGNED) = `ContentAuthorityKind` AND JSON_TYPE(JSON_EXTRACT(`ContentAuthority`, '$.authorityKey')) = 'STRING' AND `ContentAuthorityAuthorityKey` = {authorityKey} AND JSON_VALID(`ContentAuthorityAuthorityKeyJson`) = 1 AND JSON_TYPE(JSON_EXTRACT(`ContentAuthorityAuthorityKeyJson`, '$')) = 'STRING' AND JSON_UNQUOTE(JSON_EXTRACT(`ContentAuthorityAuthorityKeyJson`, '$')) = `ContentAuthorityAuthorityKey` AND {ActivityAuthoritySql.MySqlNonWhitespace(authorityKey)} AND (((JSON_TYPE(JSON_EXTRACT(`ContentAuthority`, '$.sourceId')) IS NULL OR JSON_TYPE(JSON_EXTRACT(`ContentAuthority`, '$.sourceId')) = 'NULL') AND `ContentAuthoritySourceId` IS NULL AND `ContentAuthoritySourceIdJson` = 'null') OR (JSON_TYPE(JSON_EXTRACT(`ContentAuthority`, '$.sourceId')) = 'STRING' AND `ContentAuthoritySourceId` = {sourceId} AND JSON_VALID(`ContentAuthoritySourceIdJson`) = 1 AND JSON_TYPE(JSON_EXTRACT(`ContentAuthoritySourceIdJson`, '$')) = 'STRING' AND JSON_UNQUOTE(JSON_EXTRACT(`ContentAuthoritySourceIdJson`, '$')) = `ContentAuthoritySourceId` AND {ActivityAuthoritySql.MySqlNonWhitespace(sourceId)})) AND JSON_TYPE(JSON_EXTRACT(`ContentAuthority`, '$.integrityHash')) = 'STRING' AND `ContentAuthorityIntegrityHash` = {integrityHash} AND (`ContentAuthorityKind` = 1 OR JSON_TYPE(JSON_EXTRACT(`ContentAuthority`, '$.sourceId')) IS NULL OR JSON_TYPE(JSON_EXTRACT(`ContentAuthority`, '$.sourceId')) = 'NULL') THEN 1 ELSE 0 END ELSE 0 END", stored: false);
    }
}

internal static class ActivityAuthoritySql
{
    private const string UnicodeWhitespaceCodes = "9,10,11,12,13,32,133,160,5760,8192,8193,8194,8195,8196,8197,8198,8199,8200,8201,8202,8232,8233,8239,8287,12288";

    public static string SqliteNonWhitespace(string expression) => $"length(trim({expression}, char({UnicodeWhitespaceCodes}))) > 0";

    public static string SqlServerNonWhitespace(string expression)
    {
        foreach (var code in UnicodeWhitespaceCodes.Split(','))
            expression = $"REPLACE({expression}, NCHAR({code}), N'')";
        return $"NULLIF(LTRIM(RTRIM({expression})), N'') IS NOT NULL";
    }

    public static string PostgresNonWhitespace(string expression) => $"btrim({expression}, ' ' || chr(9) || chr(10) || chr(11) || chr(12) || chr(13) || chr(133) || chr(160) || chr(5760) || chr(8192) || chr(8193) || chr(8194) || chr(8195) || chr(8196) || chr(8197) || chr(8198) || chr(8199) || chr(8200) || chr(8201) || chr(8202) || chr(8232) || chr(8233) || chr(8239) || chr(8287) || chr(12288)) <> ''";

    public static string MySqlNonWhitespace(string expression) => $"CHAR_LENGTH(REGEXP_REPLACE({expression}, '[[:space:]]', '')) > 0";

    public static string SqlServerHashSegment(string expression) =>
        $"CONCAT(CASE WHEN {expression} IS NULL THEN '-1' ELSE CONVERT(varchar(20), LEN({expression} + N'#') - 1) END, N':', COALESCE({expression}, N''))";
}
