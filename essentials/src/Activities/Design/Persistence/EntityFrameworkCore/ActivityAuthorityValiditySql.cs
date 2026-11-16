namespace Elsa.Activities.Design.Persistence.EntityFrameworkCore;

/// <summary>
/// One named property that the database-generated <c>ContentAuthorityIsValid</c> marker proves about a stored
/// authority row. This list is the single definition of what the marker means; each provider supplies its own SQL
/// for each member, or records which other clause already proves it. A provider added later works through this
/// enumeration instead of hand-translating one long predicate, and a gap is a failing contract test rather than a
/// review miss.
/// </summary>
public enum ActivityAuthorityCheck
{
    /// <summary>The raw authority column holds well-formed JSON. Emitted as the outer guard, never as a body clause.</summary>
    JsonIsWellFormed,

    /// <summary>The persisted kind column holds a declared <c>ActivityContentAuthorityKind</c> value.</summary>
    KindInDomain,

    /// <summary>The raw authority JSON is an object rather than an array or a bare scalar.</summary>
    RootIsJsonObject,

    /// <summary><c>$.kind</c> is a whole number rather than a string, a real or a boolean.</summary>
    KindIsWholeNumber,

    /// <summary><c>$.kind</c> equals the persisted kind column.</summary>
    KindAgreesWithColumn,

    /// <summary><c>$.authorityKey</c> is a JSON string rather than a number, a boolean or null.</summary>
    AuthorityKeyIsString,

    /// <summary>The decoded authority-key column equals <c>$.authorityKey</c>.</summary>
    AuthorityKeyAgreesWithColumn,

    /// <summary>The stored authority-key JSON token re-encodes the decoded authority-key column exactly.</summary>
    AuthorityKeyTokenAgreesWithColumn,

    /// <summary>The authority key holds at least one non-whitespace character, over the full Unicode whitespace set.</summary>
    AuthorityKeyIsNotWhitespace,

    /// <summary>
    /// <c>$.sourceId</c> is either absent or JSON null, with a null source column and the literal <c>null</c> token;
    /// or it is a JSON string that both the decoded column and the token agree with.
    /// </summary>
    SourceIdIsNullOrAgreeingString,

    /// <summary>A present source id holds at least one non-whitespace character.</summary>
    SourceIdIsNotWhitespace,

    /// <summary><c>$.integrityHash</c> is a JSON string.</summary>
    IntegrityHashIsString,

    /// <summary>The persisted integrity-hash column equals <c>$.integrityHash</c>.</summary>
    IntegrityHashAgreesWithColumn,

    /// <summary>Only a provider-source authority may carry a source id; a design authority may not.</summary>
    SourceIdRequiresProviderKind,

    /// <summary>
    /// The persisted digest still binds the raw, canonical, token and decoded material together. Providers that can
    /// compare each column against the raw JSON directly prove the same property that way and record it as covered.
    /// </summary>
    MaterialDigestAgrees
}

/// <summary>
/// One provider's contribution to one <see cref="ActivityAuthorityCheck"/>: either the SQL that proves it, or the
/// record that another clause in the same predicate already does. Several clauses may carry the same check when a
/// provider needs more than one expression for it.
/// </summary>
public sealed record ActivityAuthorityClause
{
    private ActivityAuthorityClause(ActivityAuthorityCheck check, string? expression, ActivityAuthorityCheck? provenBy, string? reason)
    {
        Check = check;
        Expression = expression;
        ProvenBy = provenBy;
        Reason = reason;
    }

    /// <summary>The property this clause contributes to.</summary>
    public ActivityAuthorityCheck Check { get; }

    /// <summary>The provider SQL, or <c>null</c> when another clause proves the property.</summary>
    public string? Expression { get; }

    /// <summary>The clause that proves this property instead, when this provider emits no SQL of its own.</summary>
    public ActivityAuthorityCheck? ProvenBy { get; }

    /// <summary>Why this provider proves the property by another route.</summary>
    public string? Reason { get; }

    /// <summary>Declares the SQL a provider emits for <paramref name="check"/>.</summary>
    public static ActivityAuthorityClause Sql(ActivityAuthorityCheck check, string expression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        return new(check, expression, null, null);
    }

    /// <summary>Declares that <paramref name="provenBy"/> already proves <paramref name="check"/> on this provider.</summary>
    public static ActivityAuthorityClause CoveredBy(ActivityAuthorityCheck check, ActivityAuthorityCheck provenBy, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (check == provenBy) throw new ArgumentException("A check cannot prove itself.", nameof(provenBy));
        return new(check, null, provenBy, reason);
    }
}

/// <summary>
/// Assembles the <c>ContentAuthorityIsValid</c> computed-column SQL for each provider from one clause list per
/// provider and one shared envelope, so no provider can get the surrounding <c>CASE</c> shape subtly wrong and the
/// set of properties being proved is legible without reading a single 1600-character predicate.
/// </summary>
public static class ActivityAuthorityValiditySql
{
    private const string UnicodeWhitespaceCodes = "9,10,11,12,13,32,133,160,5760,8192,8193,8194,8195,8196,8197,8198,8199,8200,8201,8202,8232,8233,8239,8287,12288";

    /// <summary>Every property the marker proves, in the order the JSON-native providers assert them.</summary>
    public static IReadOnlyList<ActivityAuthorityCheck> Checks { get; } = Enum.GetValues<ActivityAuthorityCheck>();

    /// <summary>
    /// Wraps a provider's clauses in the shared envelope. The first emitted clause is the well-formedness guard, so
    /// a row whose JSON cannot be parsed fails closed before any clause that would read into it.
    /// </summary>
    public static string Compose(IReadOnlyList<ActivityAuthorityClause> clauses, string trueLiteral, string falseLiteral)
    {
        ArgumentNullException.ThrowIfNull(clauses);
        var emitted = clauses.Where(clause => clause.Expression is not null).ToArray();
        if (emitted.Length < 2 || emitted[0].Check != ActivityAuthorityCheck.JsonIsWellFormed)
            throw new InvalidOperationException($"The first emitted clause must be {nameof(ActivityAuthorityCheck.JsonIsWellFormed)}, followed by at least one body clause.");
        if (emitted.Skip(1).Any(clause => clause.Check == ActivityAuthorityCheck.JsonIsWellFormed))
            throw new InvalidOperationException($"{nameof(ActivityAuthorityCheck.JsonIsWellFormed)} is the guard and cannot also be a body clause.");
        var body = string.Join(" AND ", emitted.Skip(1).Select(clause => clause.Expression));
        return $"CASE WHEN {emitted[0].Expression} THEN CASE WHEN {body} THEN {trueLiteral} ELSE {falseLiteral} END ELSE {falseLiteral} END";
    }

    /// <summary>
    /// SQLite clauses. The token is proved by decoding it and comparing the result, the way MySQL and PostgreSQL do.
    /// Comparing <c>json_quote</c> of the decoded scalar against the stored token instead compares two encodings rather than two values,
    /// and the writer's System.Text.Json encoder escapes more than SQLite's does: it writes \u00E9, \u003C, \u0026,
    /// \u002B and \u2028 where json_quote emits the character. That made every authority key holding one of those
    /// characters fail the marker on SQLite alone, which dropped the row from every paged read.
    /// </summary>
    public static IReadOnlyList<ActivityAuthorityClause> Sqlite()
    {
        const string authority = "\"ContentAuthority\"";
        const string kind = "\"ContentAuthorityKind\"";
        var authorityKey = $"json_extract({authority}, '$.authorityKey')";
        var sourceId = $"json_extract({authority}, '$.sourceId')";
        var integrityHash = $"json_extract({authority}, '$.integrityHash')";
        var sourceIdType = $"json_type({authority}, '$.sourceId')";
        return
        [
            ActivityAuthorityClause.Sql(ActivityAuthorityCheck.JsonIsWellFormed, $"json_valid({authority}) = 1"),
            ActivityAuthorityClause.Sql(ActivityAuthorityCheck.KindInDomain, $"{kind} IN (0, 1)"),
            ActivityAuthorityClause.Sql(ActivityAuthorityCheck.RootIsJsonObject, $"json_type({authority}) = 'object'"),
            ActivityAuthorityClause.Sql(ActivityAuthorityCheck.KindIsWholeNumber, $"json_type({authority}, '$.kind') = 'integer'"),
            ActivityAuthorityClause.Sql(ActivityAuthorityCheck.KindAgreesWithColumn, $"json_extract({authority}, '$.kind') = {kind}"),
            ActivityAuthorityClause.Sql(ActivityAuthorityCheck.AuthorityKeyIsString, $"json_type({authority}, '$.authorityKey') = 'text'"),
            ActivityAuthorityClause.Sql(ActivityAuthorityCheck.AuthorityKeyAgreesWithColumn, $"\"ContentAuthorityAuthorityKey\" = {authorityKey}"),
            ActivityAuthorityClause.Sql(ActivityAuthorityCheck.AuthorityKeyTokenAgreesWithColumn, "json_valid(\"ContentAuthorityAuthorityKeyJson\") = 1"),
            ActivityAuthorityClause.Sql(ActivityAuthorityCheck.AuthorityKeyTokenAgreesWithColumn, "json_type(\"ContentAuthorityAuthorityKeyJson\") = 'text'"),
            ActivityAuthorityClause.Sql(ActivityAuthorityCheck.AuthorityKeyTokenAgreesWithColumn, "json_extract(\"ContentAuthorityAuthorityKeyJson\", '$') = \"ContentAuthorityAuthorityKey\""),
            ActivityAuthorityClause.Sql(ActivityAuthorityCheck.AuthorityKeyIsNotWhitespace, SqliteNonWhitespace(authorityKey)),
            ActivityAuthorityClause.Sql(ActivityAuthorityCheck.SourceIdIsNullOrAgreeingString,
                $"((({sourceIdType} IS NULL OR {sourceIdType} = 'null') AND \"ContentAuthoritySourceId\" IS NULL AND \"ContentAuthoritySourceIdJson\" = 'null') OR ({sourceIdType} = 'text' AND \"ContentAuthoritySourceId\" = {sourceId} AND json_valid(\"ContentAuthoritySourceIdJson\") = 1 AND json_type(\"ContentAuthoritySourceIdJson\") = 'text' AND json_extract(\"ContentAuthoritySourceIdJson\", '$') = \"ContentAuthoritySourceId\" AND {SqliteNonWhitespace(sourceId)}))"),
            ActivityAuthorityClause.CoveredBy(ActivityAuthorityCheck.SourceIdIsNotWhitespace, ActivityAuthorityCheck.SourceIdIsNullOrAgreeingString,
                "The string branch of that clause carries the non-whitespace test, so an absent source id is not asked for one."),
            ActivityAuthorityClause.Sql(ActivityAuthorityCheck.IntegrityHashIsString, $"json_type({authority}, '$.integrityHash') = 'text'"),
            ActivityAuthorityClause.Sql(ActivityAuthorityCheck.IntegrityHashAgreesWithColumn, $"\"ContentAuthorityIntegrityHash\" = {integrityHash}"),
            ActivityAuthorityClause.Sql(ActivityAuthorityCheck.SourceIdRequiresProviderKind, $"({kind} = 1 OR {sourceIdType} IS NULL OR {sourceIdType} = 'null')"),
            ActivityAuthorityClause.CoveredBy(ActivityAuthorityCheck.MaterialDigestAgrees, ActivityAuthorityCheck.AuthorityKeyAgreesWithColumn,
                "SQLite reads a JSON scalar of any length, so every column is compared against the raw JSON directly and no digest is needed to bind them.")
        ];
    }

    /// <summary>MySQL clauses. A token needs its own validity, type and decode clauses because there is no re-encoder.</summary>
    public static IReadOnlyList<ActivityAuthorityClause> MySql()
    {
        const string authority = "`ContentAuthority`";
        const string kind = "`ContentAuthorityKind`";
        var authorityKey = $"JSON_UNQUOTE(JSON_EXTRACT({authority}, '$.authorityKey'))";
        var sourceId = $"JSON_UNQUOTE(JSON_EXTRACT({authority}, '$.sourceId'))";
        var integrityHash = $"JSON_UNQUOTE(JSON_EXTRACT({authority}, '$.integrityHash'))";
        var sourceIdType = $"JSON_TYPE(JSON_EXTRACT({authority}, '$.sourceId'))";
        return
        [
            ActivityAuthorityClause.Sql(ActivityAuthorityCheck.JsonIsWellFormed, $"JSON_VALID({authority}) = 1"),
            ActivityAuthorityClause.Sql(ActivityAuthorityCheck.KindInDomain, $"{kind} IN (0, 1)"),
            ActivityAuthorityClause.Sql(ActivityAuthorityCheck.RootIsJsonObject, $"JSON_TYPE({authority}) = 'OBJECT'"),
            ActivityAuthorityClause.Sql(ActivityAuthorityCheck.KindIsWholeNumber, $"JSON_TYPE(JSON_EXTRACT({authority}, '$.kind')) = 'INTEGER'"),
            ActivityAuthorityClause.Sql(ActivityAuthorityCheck.KindAgreesWithColumn, $"CAST(JSON_UNQUOTE(JSON_EXTRACT({authority}, '$.kind')) AS SIGNED) = {kind}"),
            ActivityAuthorityClause.Sql(ActivityAuthorityCheck.AuthorityKeyIsString, $"JSON_TYPE(JSON_EXTRACT({authority}, '$.authorityKey')) = 'STRING'"),
            ActivityAuthorityClause.Sql(ActivityAuthorityCheck.AuthorityKeyAgreesWithColumn, $"`ContentAuthorityAuthorityKey` = {authorityKey}"),
            ActivityAuthorityClause.Sql(ActivityAuthorityCheck.AuthorityKeyTokenAgreesWithColumn, "JSON_VALID(`ContentAuthorityAuthorityKeyJson`) = 1"),
            ActivityAuthorityClause.Sql(ActivityAuthorityCheck.AuthorityKeyTokenAgreesWithColumn, "JSON_TYPE(JSON_EXTRACT(`ContentAuthorityAuthorityKeyJson`, '$')) = 'STRING'"),
            ActivityAuthorityClause.Sql(ActivityAuthorityCheck.AuthorityKeyTokenAgreesWithColumn, "JSON_UNQUOTE(JSON_EXTRACT(`ContentAuthorityAuthorityKeyJson`, '$')) = `ContentAuthorityAuthorityKey`"),
            ActivityAuthorityClause.Sql(ActivityAuthorityCheck.AuthorityKeyIsNotWhitespace, MySqlNonWhitespace(authorityKey)),
            ActivityAuthorityClause.Sql(ActivityAuthorityCheck.SourceIdIsNullOrAgreeingString,
                $"((({sourceIdType} IS NULL OR {sourceIdType} = 'NULL') AND `ContentAuthoritySourceId` IS NULL AND `ContentAuthoritySourceIdJson` = 'null') OR ({sourceIdType} = 'STRING' AND `ContentAuthoritySourceId` = {sourceId} AND JSON_VALID(`ContentAuthoritySourceIdJson`) = 1 AND JSON_TYPE(JSON_EXTRACT(`ContentAuthoritySourceIdJson`, '$')) = 'STRING' AND JSON_UNQUOTE(JSON_EXTRACT(`ContentAuthoritySourceIdJson`, '$')) = `ContentAuthoritySourceId` AND {MySqlNonWhitespace(sourceId)}))"),
            ActivityAuthorityClause.CoveredBy(ActivityAuthorityCheck.SourceIdIsNotWhitespace, ActivityAuthorityCheck.SourceIdIsNullOrAgreeingString,
                "The string branch of that clause carries the non-whitespace test, so an absent source id is not asked for one."),
            ActivityAuthorityClause.Sql(ActivityAuthorityCheck.IntegrityHashIsString, $"JSON_TYPE(JSON_EXTRACT({authority}, '$.integrityHash')) = 'STRING'"),
            ActivityAuthorityClause.Sql(ActivityAuthorityCheck.IntegrityHashAgreesWithColumn, $"`ContentAuthorityIntegrityHash` = {integrityHash}"),
            ActivityAuthorityClause.Sql(ActivityAuthorityCheck.SourceIdRequiresProviderKind, $"({kind} = 1 OR {sourceIdType} IS NULL OR {sourceIdType} = 'NULL')"),
            ActivityAuthorityClause.CoveredBy(ActivityAuthorityCheck.MaterialDigestAgrees, ActivityAuthorityCheck.AuthorityKeyAgreesWithColumn,
                "MySQL reads a JSON scalar of any length, so every column is compared against the raw JSON directly and no digest is needed to bind them.")
        ];
    }

    /// <summary>PostgreSQL clauses. <c>IS JSON OBJECT</c> in the guard already rejects a non-object root.</summary>
    public static IReadOnlyList<ActivityAuthorityClause> PostgreSql()
    {
        const string authority = "\"ContentAuthority\"";
        const string kind = "\"ContentAuthorityKind\"";
        var json = $"{authority}::jsonb";
        var authorityKey = $"{json} ->> 'authorityKey'";
        var sourceId = $"{json} ->> 'sourceId'";
        var integrityHash = $"{json} ->> 'integrityHash'";
        var sourceIdAbsent = $"NOT ({json} ? 'sourceId')";
        var sourceIdType = $"jsonb_typeof({json} -> 'sourceId')";
        return
        [
            ActivityAuthorityClause.Sql(ActivityAuthorityCheck.JsonIsWellFormed, $"{authority} IS NOT NULL AND {authority} IS JSON OBJECT"),
            ActivityAuthorityClause.Sql(ActivityAuthorityCheck.KindInDomain, $"{kind} IN (0, 1)"),
            ActivityAuthorityClause.CoveredBy(ActivityAuthorityCheck.RootIsJsonObject, ActivityAuthorityCheck.JsonIsWellFormed,
                "IS JSON OBJECT in the guard already rejects an array or a bare scalar, so no separate root-type clause is needed."),
            ActivityAuthorityClause.Sql(ActivityAuthorityCheck.KindIsWholeNumber, $"jsonb_typeof({json} -> 'kind') = 'number'"),
            ActivityAuthorityClause.Sql(ActivityAuthorityCheck.KindIsWholeNumber, $"({json} ->> 'kind') ~ '^[01]$'"),
            ActivityAuthorityClause.Sql(ActivityAuthorityCheck.KindAgreesWithColumn, $"({json} ->> 'kind')::integer = {kind}"),
            ActivityAuthorityClause.Sql(ActivityAuthorityCheck.AuthorityKeyIsString, $"jsonb_typeof({json} -> 'authorityKey') = 'string'"),
            ActivityAuthorityClause.Sql(ActivityAuthorityCheck.AuthorityKeyAgreesWithColumn, $"\"ContentAuthorityAuthorityKey\" = {authorityKey}"),
            ActivityAuthorityClause.Sql(ActivityAuthorityCheck.AuthorityKeyTokenAgreesWithColumn, "jsonb_typeof(\"ContentAuthorityAuthorityKeyJson\"::jsonb) = 'string'"),
            ActivityAuthorityClause.Sql(ActivityAuthorityCheck.AuthorityKeyTokenAgreesWithColumn, "(\"ContentAuthorityAuthorityKeyJson\"::jsonb #>> '{}') = \"ContentAuthorityAuthorityKey\""),
            ActivityAuthorityClause.Sql(ActivityAuthorityCheck.AuthorityKeyIsNotWhitespace, PostgresNonWhitespace(authorityKey)),
            ActivityAuthorityClause.Sql(ActivityAuthorityCheck.SourceIdIsNullOrAgreeingString,
                $"((({sourceIdAbsent} OR {sourceIdType} = 'null') AND \"ContentAuthoritySourceId\" IS NULL AND \"ContentAuthoritySourceIdJson\" = 'null') OR ({sourceIdType} = 'string' AND \"ContentAuthoritySourceId\" = {sourceId} AND jsonb_typeof(\"ContentAuthoritySourceIdJson\"::jsonb) = 'string' AND (\"ContentAuthoritySourceIdJson\"::jsonb #>> '{{}}') = \"ContentAuthoritySourceId\" AND {PostgresNonWhitespace(sourceId)}))"),
            ActivityAuthorityClause.CoveredBy(ActivityAuthorityCheck.SourceIdIsNotWhitespace, ActivityAuthorityCheck.SourceIdIsNullOrAgreeingString,
                "The string branch of that clause carries the non-whitespace test, so an absent source id is not asked for one."),
            ActivityAuthorityClause.Sql(ActivityAuthorityCheck.IntegrityHashIsString, $"jsonb_typeof({json} -> 'integrityHash') = 'string'"),
            ActivityAuthorityClause.Sql(ActivityAuthorityCheck.IntegrityHashAgreesWithColumn, $"\"ContentAuthorityIntegrityHash\" = {integrityHash}"),
            ActivityAuthorityClause.Sql(ActivityAuthorityCheck.SourceIdRequiresProviderKind, $"({kind} = 1 OR {sourceIdAbsent} OR {sourceIdType} = 'null')"),
            ActivityAuthorityClause.CoveredBy(ActivityAuthorityCheck.MaterialDigestAgrees, ActivityAuthorityCheck.AuthorityKeyAgreesWithColumn,
                "PostgreSQL reads a JSON scalar of any length, so every column is compared against the raw JSON directly and no digest is needed to bind them.")
        ];
    }

    /// <summary>
    /// SQL Server clauses. <c>JSON_VALUE</c> returns NULL for a scalar longer than 4000 characters, so the decoded
    /// columns cannot simply be compared against the raw JSON. Token reconstruction proves the canonical material and
    /// a persisted digest binds raw, canonical, token and decoded material together, which is how this provider
    /// reaches the properties the others assert with a direct comparison.
    /// </summary>
    public static IReadOnlyList<ActivityAuthorityClause> SqlServer()
    {
        const string authority = "[ContentAuthority]";
        const string kind = "[ContentAuthorityKind]";
        const string authorityKey = "[ContentAuthorityAuthorityKey]";
        const string sourceId = "[ContentAuthoritySourceId]";
        const string authorityKeyToken = "[ContentAuthorityAuthorityKeyJson]";
        const string sourceIdToken = "[ContentAuthoritySourceIdJson]";
        const string authorityIntegrityHash = "[ContentAuthorityIntegrityHash]";
        var integrityHashToken = $"JSON_VALUE({authority}, '$.integrityHash')";
        var rawWithoutIntegrity = $"JSON_MODIFY({authority}, '$.integrityHash', NULL)";
        const string canonicalWithoutIntegrity = "JSON_MODIFY([ContentAuthorityCanonicalJson], '$.integrityHash', NULL)";
        var hashMaterial = string.Join(", N'|', ", new[] { rawWithoutIntegrity, canonicalWithoutIntegrity, authorityKeyToken, sourceIdToken, authorityKey, sourceId, $"CONVERT(nvarchar(20), {kind})" }.Select(SqlServerHashSegment));
        const string digestReason = "JSON_VALUE returns NULL above 4000 characters, so this provider cannot compare the column against the raw JSON. The digest covers the raw JSON, the canonical JSON, both tokens, both decoded scalars and the kind, so any drift between them fails this clause instead.";
        return
        [
            ActivityAuthorityClause.Sql(ActivityAuthorityCheck.JsonIsWellFormed, $"{authority} IS NOT NULL AND ISJSON({authority}) = 1"),
            ActivityAuthorityClause.Sql(ActivityAuthorityCheck.KindInDomain, $"{kind} IN (0, 1)"),
            ActivityAuthorityClause.Sql(ActivityAuthorityCheck.RootIsJsonObject, $"JSON_QUERY({authority}) IS NOT NULL"),
            ActivityAuthorityClause.CoveredBy(ActivityAuthorityCheck.KindIsWholeNumber, ActivityAuthorityCheck.MaterialDigestAgrees, digestReason),
            ActivityAuthorityClause.CoveredBy(ActivityAuthorityCheck.KindAgreesWithColumn, ActivityAuthorityCheck.MaterialDigestAgrees, digestReason),
            ActivityAuthorityClause.Sql(ActivityAuthorityCheck.AuthorityKeyIsString, $"{authorityKey} IS NOT NULL"),
            ActivityAuthorityClause.CoveredBy(ActivityAuthorityCheck.AuthorityKeyAgreesWithColumn, ActivityAuthorityCheck.MaterialDigestAgrees, digestReason),
            ActivityAuthorityClause.Sql(ActivityAuthorityCheck.AuthorityKeyTokenAgreesWithColumn, $"{authorityKeyToken} IS NOT NULL"),
            ActivityAuthorityClause.Sql(ActivityAuthorityCheck.AuthorityKeyTokenAgreesWithColumn, $"LEFT({authorityKeyToken}, 1) = N'\"'"),
            ActivityAuthorityClause.Sql(ActivityAuthorityCheck.AuthorityKeyTokenAgreesWithColumn, $"RIGHT({authorityKeyToken}, 1) = N'\"'"),
            ActivityAuthorityClause.Sql(ActivityAuthorityCheck.AuthorityKeyTokenAgreesWithColumn, $"ISJSON(CONCAT(N'[', {authorityKeyToken}, N']')) = 1"),
            ActivityAuthorityClause.Sql(ActivityAuthorityCheck.AuthorityKeyTokenAgreesWithColumn, $"(LEN({authorityKey}) > 4000 OR JSON_VALUE(CONCAT(N'[', {authorityKeyToken}, N']'), '$[0]') = {authorityKey})"),
            ActivityAuthorityClause.Sql(ActivityAuthorityCheck.SourceIdIsNullOrAgreeingString,
                $"({sourceIdToken} IS NULL OR {sourceIdToken} = N'null' OR (LEFT({sourceIdToken}, 1) = N'\"' AND RIGHT({sourceIdToken}, 1) = N'\"' AND ISJSON(CONCAT(N'[', {sourceIdToken}, N']')) = 1 AND (LEN({sourceId}) > 4000 OR JSON_VALUE(CONCAT(N'[', {sourceIdToken}, N']'), '$[0]') = {sourceId})))"),
            ActivityAuthorityClause.Sql(ActivityAuthorityCheck.SourceIdIsNullOrAgreeingString,
                $"(({sourceIdToken} = N'null' AND {sourceId} IS NULL) OR ({sourceIdToken} <> N'null' AND {sourceId} IS NOT NULL))"),
            ActivityAuthorityClause.CoveredBy(ActivityAuthorityCheck.IntegrityHashIsString, ActivityAuthorityCheck.MaterialDigestAgrees, digestReason),
            ActivityAuthorityClause.Sql(ActivityAuthorityCheck.IntegrityHashAgreesWithColumn, $"{integrityHashToken} = {authorityIntegrityHash}"),
            ActivityAuthorityClause.Sql(ActivityAuthorityCheck.AuthorityKeyIsNotWhitespace, SqlServerNonWhitespace(authorityKey)),
            ActivityAuthorityClause.Sql(ActivityAuthorityCheck.SourceIdIsNotWhitespace, $"({sourceId} IS NULL OR {SqlServerNonWhitespace(sourceId)})"),
            ActivityAuthorityClause.Sql(ActivityAuthorityCheck.SourceIdRequiresProviderKind, $"({kind} = 1 OR {sourceId} IS NULL)"),
            ActivityAuthorityClause.Sql(ActivityAuthorityCheck.MaterialDigestAgrees, $"{authorityIntegrityHash} = CONVERT(varchar(64), HASHBYTES('SHA2_256', CONCAT({hashMaterial})), 2)")
        ];
    }

    private static string SqliteNonWhitespace(string expression) => $"length(trim({expression}, char({UnicodeWhitespaceCodes}))) > 0";

    private static string SqlServerNonWhitespace(string expression)
    {
        foreach (var code in UnicodeWhitespaceCodes.Split(','))
            expression = $"REPLACE({expression}, NCHAR({code}), N'')";
        return $"NULLIF(LTRIM(RTRIM({expression})), N'') IS NOT NULL";
    }

    // Code 32 is written as the literal space that opens btrim's character set; the rest follow as chr() calls.
    private static string PostgresNonWhitespace(string expression) =>
        $"btrim({expression}, ' ' || {string.Join(" || ", UnicodeWhitespaceCodes.Split(',').Where(code => code != "32").Select(code => $"chr({code})"))}) <> ''";

    private static string MySqlNonWhitespace(string expression) => $"CHAR_LENGTH(REGEXP_REPLACE({expression}, '[[:space:]]', '')) > 0";

    private static string SqlServerHashSegment(string expression) =>
        $"CONCAT(CASE WHEN {expression} IS NULL THEN '-1' ELSE CONVERT(varchar(20), LEN({expression} + N'#') - 1) END, N':', COALESCE({expression}, N''))";
}
