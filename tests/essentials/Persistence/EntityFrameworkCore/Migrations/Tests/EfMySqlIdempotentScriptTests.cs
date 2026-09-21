using Elsa.Persistence.EntityFramework.Tooling;
using Xunit;

namespace Elsa.Persistence.EntityFrameworkCore.Migrations.Tests;

/// <summary>
/// The rewrite #1914 needs, driven directly rather than only through the artifact
/// <see cref="EfToolingHostTests.Script_hoists_every_mysql_guard_into_the_modules_own_stored_procedure"/>
/// asserts. Two things are worth proving here and cannot be proved there: the exact bytes the transform
/// produces, because <c>script-check</c> compares this text byte for byte, and every shape it refuses,
/// because no committed migration can make <c>MySql.EntityFrameworkCore</c> emit one. Text surgery on a
/// third party's output is only safe while it refuses everything it does not recognise, and the day Oracle
/// reformats — or fixes MySQL Bug #121043 — is the day these refusals have to fire instead of a
/// half-converted file reaching a DBA.
/// </summary>
public sealed class EfMySqlIdempotentScriptTests
{
    private const string Module = "Acme.Widgets";

    /// <summary>
    /// EF's own output shape, reduced to one migration: a history table that is already idempotent, then the
    /// top-level <c>IF NOT EXISTS(…)</c> / <c>BEGIN</c> / <c>END;</c> copied from the SQL Server generator,
    /// inside a transaction MySQL cannot honour for DDL.
    /// </summary>
    private static readonly string Generated =
        """
        CREATE TABLE IF NOT EXISTS `__EFMigrationsHistory_AcmeWidgets` (
            `MigrationId` varchar(150) NOT NULL,
            `ProductVersion` varchar(32) NOT NULL,
            PRIMARY KEY (`MigrationId`)
        );

        START TRANSACTION;
        IF NOT EXISTS(SELECT * FROM `__EFMigrationsHistory_AcmeWidgets` WHERE `MigrationId` = '20260101000000_Initial')
        BEGIN
            CREATE TABLE `acme_widgets` (
                `Id` varchar(64) NOT NULL,
                PRIMARY KEY (`Id`)
            );
        END;

        IF NOT EXISTS(SELECT * FROM `__EFMigrationsHistory_AcmeWidgets` WHERE `MigrationId` = '20260101000000_Initial')
        BEGIN
            INSERT INTO `__EFMigrationsHistory_AcmeWidgets` (`MigrationId`, `ProductVersion`)
            VALUES ('20260101000000_Initial', '10.0.10');
        END;

        COMMIT;

        """.ReplaceLineEndings("\n");

    private static readonly string Rewritten =
        """
        CREATE TABLE IF NOT EXISTS `__EFMigrationsHistory_AcmeWidgets` (
            `MigrationId` varchar(150) NOT NULL,
            `ProductVersion` varchar(32) NOT NULL,
            PRIMARY KEY (`MigrationId`)
        );

        -- The migration guards below run inside a stored procedure: MySQL accepts IF ... THEN only within a
        -- routine (MySQL Bug #121043). Applying this file therefore needs CREATE ROUTINE and ALTER ROUTINE on
        -- this database, alongside the usual DDL rights; the procedure is dropped again on the way out.
        -- EF's START TRANSACTION/COMMIT pair is deliberately not carried over: MySQL commits implicitly on
        -- every DDL statement, so an explicit transaction here would promise an atomicity it cannot deliver.
        DROP PROCEDURE IF EXISTS `elsa_migrate_acme-widgets`;
        DELIMITER //
        CREATE PROCEDURE `elsa_migrate_acme-widgets`()
        BEGIN
            IF NOT EXISTS(SELECT * FROM `__EFMigrationsHistory_AcmeWidgets` WHERE `MigrationId` = '20260101000000_Initial') THEN
                CREATE TABLE `acme_widgets` (
                    `Id` varchar(64) NOT NULL,
                    PRIMARY KEY (`Id`)
                );
            END IF;

            IF NOT EXISTS(SELECT * FROM `__EFMigrationsHistory_AcmeWidgets` WHERE `MigrationId` = '20260101000000_Initial') THEN
                INSERT INTO `__EFMigrationsHistory_AcmeWidgets` (`MigrationId`, `ProductVersion`)
                VALUES ('20260101000000_Initial', '10.0.10');
            END IF;
        END //
        DELIMITER ;
        CALL `elsa_migrate_acme-widgets`();
        DROP PROCEDURE `elsa_migrate_acme-widgets`;

        """.ReplaceLineEndings("\n");

    /// <summary>
    /// The whole contract in one comparison, bytes included: the history table stays outside the procedure,
    /// every guard gains its <c>THEN</c> and loses its <c>BEGIN</c>, every <c>END;</c> becomes <c>END IF;</c>,
    /// EF's transaction control is gone, and the file calls and then drops the routine it created. The header
    /// is asserted along with the rest because it is artifact bytes too — <c>script-check</c> compares it, and
    /// it is where an operator reads which routine privileges applying this file now needs.
    /// </summary>
    [Fact]
    public void Hoists_every_guard_into_one_procedure_named_after_the_module() =>
        Assert.Equal(Rewritten, Rewrite(Generated));

    /// <summary>
    /// Pomelo names every script's procedure <c>MigrationsScript</c>. This host writes thirteen files a
    /// deployment can apply to one database, so a fixed name would have two of them dropping each other's
    /// routine mid-<c>CALL</c>.
    /// </summary>
    [Fact]
    public void Names_the_procedure_after_the_module_so_two_modules_never_collide()
    {
        var other = EfMySqlIdempotentScript.Rewrite("MySql", "Acme.Gadgets", Generated);

        Assert.Contains("CREATE PROCEDURE `elsa_migrate_acme-widgets`()", Rewrite(Generated), StringComparison.Ordinal);
        Assert.Contains("CREATE PROCEDURE `elsa_migrate_acme-gadgets`()", other, StringComparison.Ordinal);
    }

    /// <summary>
    /// The gate is load-bearing, not an optimization: SQL Server's idempotent script is the one MySQL's
    /// generator copied, so it carries the same top-level <c>IF NOT EXISTS</c> / <c>BEGIN</c> / <c>END;</c>
    /// this rewrite recognises. Reference equality, so "unchanged" cannot be satisfied by a rebuilt string
    /// that merely happens to match.
    /// </summary>
    [Theory]
    [InlineData("Sqlite")]
    [InlineData("SqlServer")]
    [InlineData("PostgreSql")]
    public void Returns_the_script_unchanged_for_every_provider_but_mysql(string provider) =>
        Assert.Same(Generated, EfMySqlIdempotentScript.Rewrite(provider, Module, Generated));

    /// <summary>
    /// A module with no migrations yet generates a history table and nothing else — already legal MySQL, so
    /// it is handed back rather than wrapped in a procedure with an empty body, which MySQL would reject.
    /// </summary>
    [Fact]
    public void Leaves_a_script_that_carries_no_guard_at_all_untouched()
    {
        var history = Generated[..Generated.IndexOf("\nSTART TRANSACTION;", StringComparison.Ordinal)];

        Assert.Same(history, Rewrite(history));
    }

    /// <summary>
    /// Determinism (spec 171 FR-043) survives the rewrite: this runs before
    /// <c>EfToolingLineEndings.Utf8Lf</c>, so it is handed CRLF on Windows and LF everywhere else and must
    /// produce the same text either way, not merely text that normalizes to the same thing later.
    /// </summary>
    [Fact]
    public void Produces_the_same_text_whatever_line_endings_ef_generated() =>
        Assert.Equal(Rewritten, Rewrite(Generated.Replace("\n", "\r\n", StringComparison.Ordinal)));

    /// <summary>
    /// The refusal that documents this type's own removal. A fixed MySQL Bug #121043 emits the <c>THEN</c>
    /// itself, and the rewrite must fail the run naming itself rather than double-wrap a script that no
    /// longer needs wrapping — the one outcome that would look like success and ship unrunnable SQL again.
    /// </summary>
    [Fact]
    public void Refuses_a_guard_that_already_carries_its_own_then()
    {
        var upstreamFixed = Generated
            .Replace("_Initial')\nBEGIN\n", "_Initial') THEN\n", StringComparison.Ordinal)
            .Replace("\nEND;\n", "\nEND IF;\n", StringComparison.Ordinal);

        var refusal = Assert.ThrowsAny<Exception>(() => Rewrite(upstreamFixed));

        Assert.Contains(nameof(EfMySqlIdempotentScript), refusal.Message, StringComparison.Ordinal);
        Assert.Contains("MySQL Bug #121043", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("should be deleted", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every shape that is not the one block this recognises. Each would otherwise be copied through into a
    /// procedure body, or silently dropped, and the file would still look like a script.
    /// </summary>
    [Theory]
    // A statement outside any guard: nothing says it belongs in the procedure, or where.
    [InlineData("\nCOMMIT;\n", "\nCREATE INDEX `IX_acme_widgets_id` ON `acme_widgets` (`Id`);\nCOMMIT;\n")]
    // A guard whose BEGIN never arrives.
    [InlineData("_Initial')\nBEGIN\n    CREATE TABLE", "_Initial')\n    CREATE TABLE")]
    // A body line at column 0, which is how a reformatted generator would first show up.
    [InlineData("\n        PRIMARY KEY (`Id`)\n", "\nPRIMARY KEY (`Id`)\n")]
    // The DELIMITER the procedure is quoted with, which would end the CREATE PROCEDURE early.
    [InlineData("`Id` varchar(64) NOT NULL", "`Id` varchar(64) NOT NULL DEFAULT 'https://'")]
    // Block structure before the guarded region, where there is no procedure to put it in.
    [InlineData("CREATE TABLE IF NOT EXISTS", "BEGIN\nCREATE TABLE IF NOT EXISTS")]
    public void Refuses_any_shape_it_does_not_recognise(string original, string replacement)
    {
        var mangled = Generated.Replace(original, replacement, StringComparison.Ordinal);
        Assert.NotEqual(Generated, mangled);

        var refusal = Assert.ThrowsAny<Exception>(() => Rewrite(mangled));

        Assert.Contains("refuses rather than emit half-converted SQL", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A script that stops in the middle of a block — a truncated read, or a generator that stopped emitting
    /// — is refused rather than closed with an <c>END IF;</c> this never saw, which would hand a DBA a
    /// procedure that compiles and applies half a migration.
    /// </summary>
    [Fact]
    public void Refuses_a_block_whose_end_never_arrives()
    {
        var truncated = Generated[..Generated.IndexOf("\nEND;", StringComparison.Ordinal)];

        var refusal = Assert.ThrowsAny<Exception>(() => Rewrite(truncated));

        Assert.Contains("refuses rather than emit half-converted SQL", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// First-party module names always slug into a MySQL identifier, but spec 171 User Story 6 admits
    /// third-party ones, and nothing stops a third-party <c>[EfModule]</c> name from being too long for
    /// MySQL's 64 characters or from carrying the backtick that quotes the name. Refused while the artifact
    /// is still in memory rather than left for the server to reject — or, for the backtick, to obey.
    /// </summary>
    [Theory]
    [InlineData("Acme.`Evil`")]
    [InlineData("Acme Widgets")]
    [InlineData("AcmeWidgetsWithAnExtravagantlyLongNameThatMySqlWillNotAcceptAtAll")]
    public void Refuses_a_module_name_that_is_not_a_mysql_identifier(string module)
    {
        var refusal = Assert.ThrowsAny<Exception>(() => Rewrite(Generated, module));

        Assert.Contains("is not a MySQL identifier", refusal.Message, StringComparison.Ordinal);
    }

    private static string Rewrite(string script, string module = Module) =>
        EfMySqlIdempotentScript.Rewrite("MySql", module, script);
}
