using System.Text;

namespace Elsa.Persistence.EntityFramework.Tooling;

/// <summary>
/// Rewrites the idempotent SQL <c>MySql.EntityFrameworkCore</c> generates into the only form MySQL will
/// actually execute. Oracle's <c>MySQLHistoryRepository.GetBeginIfNotExistsScript</c> and
/// <c>GetEndIfScript</c> are a verbatim copy of the SQL Server ones, so EF emits
/// <c>IF NOT EXISTS(…)</c> / <c>BEGIN</c> / <c>END;</c> at the top level of the script — a shape MySQL's
/// grammar allows only inside a stored routine, so a real server rejects the whole file with
/// <c>ERROR 1064</c> before it applies anything
/// (<see href="https://bugs.mysql.com/bug.php?id=121043">MySQL Bug #121043</see>, open and unassigned,
/// unchanged since 8.0.33 and still present in 10.0.9). Without this, <c>dotnet elsa persistence script
/// --provider MySql</c> writes an artifact no DBA can run.
/// </summary>
/// <remarks>
/// <para>
/// <b>Delete this type when MySQL Bug #121043 is fixed</b> — specifically, when the engine this build binds
/// emits its own <c>THEN</c> / <c>END IF</c>, and the routine they have to live in, rather than SQL Server's
/// <c>BEGIN</c> / <c>END;</c>. Nothing here is a design choice worth keeping: it exists only because the
/// generator upstream is wrong. That day arrives loudly rather than silently — <see cref="Rewrite"/> refuses
/// any shape it does not recognise, so a fixed upstream fails <c>script --provider MySql</c> with
/// <see cref="UnrecognisedShapeCode"/> naming this type, instead of quietly double-wrapping a script that no
/// longer needs it.
/// </para>
/// <para>
/// The rewrite is a pure function of its arguments — no clock, no environment, no culture-sensitive
/// formatting, LF only — because the artifact's per-file <c>sha256</c>, <c>script-check</c>'s byte
/// comparison and spec 171 FR-043's determinism guarantee all read what comes out of it.
/// </para>
/// <para>
/// Public for the same reason <see cref="EfToolingLineEndings"/> and <see cref="EfToolingRedaction"/> are:
/// the guarantee it makes is worth driving directly from a test, not only through the artifact it ends up in.
/// </para>
/// </remarks>
public static class EfMySqlIdempotentScript
{
    /// <summary>
    /// The refusal code <c>script</c> reports when the generated MySQL text is not the shape this rewrite
    /// knows how to convert. Named so a failing run is searchable, and so this file is findable from it.
    /// </summary>
    public const string UnrecognisedShapeCode = "mysql-script-shape-unrecognised";

    /// <summary>
    /// The <c>DELIMITER</c> the procedure body is quoted with, so its own <c>;</c> characters do not end the
    /// enclosing <c>CREATE PROCEDURE</c>. It is a client directive — the <c>mysql</c> CLI and this
    /// repository's <c>SqlScriptSplitting.SplitMySqlStatements</c> both consume it and never send it.
    /// </summary>
    private const string Delimiter = "//";

    private const string GuardPrefix = "IF NOT EXISTS(";
    private const string BlockOpen = "BEGIN";
    private const string BlockClose = "END;";
    private const string TransactionStart = "START TRANSACTION;";
    private const string TransactionCommit = "COMMIT;";
    private const string Indent = "    ";
    private const string ProcedurePrefix = "elsa_migrate_";

    /// <summary>MySQL's maximum identifier length, which the procedure name has to stay inside.</summary>
    private const int MaxIdentifierLength = 64;

    /// <summary>
    /// The header the rewritten file carries, so a DBA reading the artifact knows why a stored procedure is
    /// there, and why the transaction EF wrapped the migrations in is not.
    /// </summary>
    private static readonly string[] Header =
    [
        "-- The migration guards below run inside a stored procedure: MySQL accepts IF ... THEN only within a",
        "-- routine (MySQL Bug #121043). Applying this file therefore needs CREATE ROUTINE and ALTER ROUTINE on",
        "-- this database, alongside the usual DDL rights; the procedure is dropped again on the way out.",
        "-- EF's START TRANSACTION/COMMIT pair is deliberately not carried over: MySQL commits implicitly on",
        "-- every DDL statement, so an explicit transaction here would promise an atomicity it cannot deliver."
    ];

    /// <summary>
    /// Returns <paramref name="script"/> in the form MySQL executes, or unchanged for every other provider.
    /// </summary>
    /// <remarks>
    /// The provider gate is not a shortcut. SQL Server's idempotent script carries a top-level
    /// <c>IF NOT EXISTS</c> / <c>BEGIN</c> / <c>END;</c> of its own — the very shape MySQL's generator copied
    /// — and PostgreSQL's carries <c>START TRANSACTION;</c> and a <c>BEGIN</c> inside its <c>DO $EF$</c>
    /// blocks. Rewriting either would produce a file that still looked plausible.
    /// </remarks>
    /// <exception cref="Exception">
    /// An <c>EfToolingRefusal</c> carrying <see cref="UnrecognisedShapeCode"/> when the generated text is not
    /// the shape described above. Half-converted SQL is never returned: a refusal costs the artifact, which is
    /// recoverable, where a silently mis-converted one is not.
    /// </exception>
    public static string Rewrite(string provider, string module, string script)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(module);
        ArgumentNullException.ThrowIfNull(script);

        if (EfRelationalProviderBinding.Normalize(provider) != "mysql")
            return script;

        // EF builds its script with Environment.NewLine and this runs before EfToolingLineEndings.Utf8Lf
        // normalizes it, so the endings a Windows run produces are folded here rather than matched for.
        var lines = script.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        var start = GuardedRegionStart(lines, module);
        if (start < 0)
            return script;

        var body = RewriteGuards(lines, start, module);
        // A script with no guard at all — a module with no migrations yet — carries nothing MySQL would
        // reject, so it is handed back exactly as generated rather than wrapped in an empty procedure.
        if (body.Count == 0)
            return script;

        var name = ProcedureName(module);
        var builder = new StringBuilder();

        var preamble = start;
        while (preamble > 0 && lines[preamble - 1].Length == 0)
            preamble--;
        for (var index = 0; index < preamble; index++)
            builder.Append(lines[index]).Append('\n');
        if (preamble > 0)
            builder.Append('\n');

        foreach (var line in Header)
            builder.Append(line).Append('\n');

        // The leading DROP is what makes the file re-runnable after a run that failed part-way: the trailing
        // one never ran, so the routine is still there. It is also the statement that puts ALTER ROUTINE on
        // the privilege list, because MySQL checks that before it checks whether the routine exists.
        builder.Append($"DROP PROCEDURE IF EXISTS `{name}`;\nDELIMITER {Delimiter}\nCREATE PROCEDURE `{name}`()\n{BlockOpen}\n");
        foreach (var line in body)
            builder.Append(line).Append('\n');
        builder.Append($"END {Delimiter}\nDELIMITER ;\nCALL `{name}`();\nDROP PROCEDURE `{name}`;\n");
        return builder.ToString();
    }

    /// <summary>
    /// The first line that belongs to the guarded region: EF's transaction statement, or the first guard.
    /// Everything before it is the <c>CREATE TABLE IF NOT EXISTS `__EFMigrationsHistory_*`</c> preamble, which
    /// is already idempotent and already legal at the top level and is copied through untouched. Returns
    /// <c>-1</c> when there is no guarded region at all, and refuses when the preamble carries block structure
    /// this rewrite would have to place and cannot.
    /// </summary>
    private static int GuardedRegionStart(string[] lines, string module)
    {
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (line == TransactionStart || line.StartsWith(GuardPrefix, StringComparison.Ordinal))
                return index;
            if (line is BlockOpen or BlockClose or TransactionCommit)
                throw Unrecognised(module, $"line {index + 1}: {line}");
        }

        return -1;
    }

    /// <summary>
    /// Converts each <c>IF NOT EXISTS(…)</c> / <c>BEGIN</c> / <c>END;</c> block into the
    /// <c>IF NOT EXISTS(…) THEN</c> / <c>END IF;</c> a routine body takes, indented one level for the
    /// procedure it is about to sit in, and drops EF's transaction statements. Every line is accounted for:
    /// anything outside that vocabulary is refused rather than copied through, because a statement this does
    /// not recognise is one it cannot promise it placed correctly.
    /// </summary>
    private static List<string> RewriteGuards(string[] lines, int start, string module)
    {
        var body = new List<string>();
        var inBlock = false;
        for (var index = start; index < lines.Length; index++)
        {
            var line = lines[index];
            // The DELIMITER the procedure is quoted with must not occur inside it: the mysql client and this
            // repository's own splitter would both end the CREATE PROCEDURE early. Nothing EF emits for these
            // migrations contains it — there is no migrationBuilder.Sql anywhere in src/, so the only string
            // literals are migration ids and a product version — so this refuses a future statement that
            // does, rather than mis-splitting it.
            if (line.Contains(Delimiter, StringComparison.Ordinal))
                throw Unrecognised(module, $"line {index + 1}: {line}");

            // A blank line is whitespace in any position. The ones EF puts between blocks are kept, so the
            // procedure body reads like the script it came from; a run of them before the first guard is not.
            if (line.Length == 0)
            {
                if (body.Count > 0)
                    body.Add(string.Empty);
                continue;
            }

            if (inBlock)
            {
                if (line == BlockClose)
                {
                    body.Add(Indent + "END IF;");
                    inBlock = false;
                    continue;
                }

                if (!line.StartsWith(Indent, StringComparison.Ordinal))
                    throw Unrecognised(module, $"line {index + 1}: {line}");

                body.Add(Indent + line);
                continue;
            }

            if (line == TransactionStart || line == TransactionCommit)
                continue;

            if (!line.StartsWith(GuardPrefix, StringComparison.Ordinal) || !line.EndsWith(')'))
                throw Unrecognised(module, $"line {index + 1}: {line}");
            if (index + 1 >= lines.Length || lines[index + 1] != BlockOpen)
                throw Unrecognised(module, $"line {index + 1}: {line}");

            body.Add(Indent + line + " THEN");
            index++;
            inBlock = true;
        }

        if (inBlock)
            throw Unrecognised(module, $"line {lines.Length}: the script ends inside a '{BlockOpen}' block.");

        while (body.Count > 0 && body[^1].Length == 0)
            body.RemoveAt(body.Count - 1);

        return body;
    }

    /// <summary>
    /// One procedure per module, named from the same slug the module's file is named from. Pomelo uses a
    /// single fixed <c>MigrationsScript</c> for every script it generates, which two of this host's module
    /// artifacts applied to one database at the same time would collide on: the second <c>CREATE PROCEDURE</c>
    /// fails, or the first one's <c>DROP</c> pulls the routine out from under the second one's <c>CALL</c>.
    /// </summary>
    private static string ProcedureName(string module)
    {
        var name = ProcedurePrefix + EfMigrationPlan.Slug(module);
        // Slug lower-cases and maps '.' to '-'; a third-party [EfModule] name (spec 171 User Story 6) can
        // still carry anything else, including the backtick that quotes this identifier.
        if (name.Length > MaxIdentifierLength || !name.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_'))
        {
            throw EfToolingRefusal.Resolution(
                UnrecognisedShapeCode,
                $"'{module}' cannot be scripted for MySQL: its idempotent script needs a stored procedure, and " +
                $"'{name}' is not a MySQL identifier of at most {MaxIdentifierLength} characters drawn from a-z, 0-9, '-' and '_'.");
        }

        return name;
    }

    private static EfToolingRefusal Unrecognised(string module, string detail) =>
        EfToolingRefusal.Resolution(
            UnrecognisedShapeCode,
            $"'{module}' generated MySQL that {nameof(EfMySqlIdempotentScript)} does not recognise, so no script was written. " +
            "It rewrites the top-level 'IF NOT EXISTS(...) / BEGIN / END;' MySql.EntityFrameworkCore emits " +
            "(MySQL Bug #121043) into a stored procedure, and refuses rather than emit half-converted SQL. " +
            "If the engine now emits 'THEN'/'END IF' itself, that bug is fixed and this rewrite should be deleted.",
            [detail]);
}
