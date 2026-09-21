using Xunit;

namespace Elsa.Persistence.EntityFrameworkCore.CliAcceptance.ProviderTests;

/// <summary>
/// The part of issue #1875 that is reachable with no container at all: the splitting rules are exercised
/// against representative script shapes, including the ones a naive "split on every <c>;</c>" or "run the
/// whole file as one command" implementation would get wrong.
/// </summary>
public sealed class SqlScriptSplittingTests
{
    [Fact]
    public void Sql_server_batches_are_split_on_a_standalone_go_line()
    {
        const string script = """
            CREATE TABLE [t1] ([Id] int NOT NULL);
            GO

            IF NOT EXISTS (SELECT 1)
            BEGIN
                CREATE TABLE [t2] ([Id] int NOT NULL);
            END;
            GO
            """;

        var batches = SqlScriptSplitting.SplitSqlServerBatches(script);

        Assert.Equal(2, batches.Count);
        Assert.Contains("CREATE TABLE [t1]", batches[0], StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE [t2]", batches[1], StringComparison.Ordinal);
    }

    /// <summary>The exact regression this splitter exists to prevent: applying only the first batch and reporting green.</summary>
    [Fact]
    public void A_script_with_no_go_separator_is_a_single_batch()
    {
        var batches = SqlScriptSplitting.SplitSqlServerBatches("CREATE TABLE [t1] ([Id] int NOT NULL);");

        Assert.Single(batches);
    }

    [Fact]
    public void A_trailing_batch_with_no_terminating_go_is_still_included()
    {
        const string script = """
            CREATE TABLE [t1] ([Id] int NOT NULL);
            GO
            CREATE TABLE [t2] ([Id] int NOT NULL);
            """;

        var batches = SqlScriptSplitting.SplitSqlServerBatches(script);

        Assert.Equal(2, batches.Count);
        Assert.Contains("t2", batches[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Consecutive_go_lines_produce_no_empty_batch()
    {
        const string script = """
            CREATE TABLE [t1] ([Id] int NOT NULL);
            GO
            GO
            CREATE TABLE [t2] ([Id] int NOT NULL);
            GO
            """;

        var batches = SqlScriptSplitting.SplitSqlServerBatches(script);

        Assert.Equal(2, batches.Count);
    }

    /// <summary>"GO" as a repeat-count form (<c>GO 3</c>) is still a separator, the way <c>sqlcmd</c> reads it.</summary>
    [Fact]
    public void A_go_line_with_a_repeat_count_still_separates_batches()
    {
        const string script = """
            CREATE TABLE [t1] ([Id] int NOT NULL);
            GO 3
            CREATE TABLE [t2] ([Id] int NOT NULL);
            """;

        var batches = SqlScriptSplitting.SplitSqlServerBatches(script);

        Assert.Equal(2, batches.Count);
    }

    /// <summary>"GO" only separates when it is the whole line: neither a comment nor an identifier that merely contains it.</summary>
    [Theory]
    [InlineData("-- GO to production once reviewed")]
    [InlineData("SELECT 'GO' AS [Marker];")]
    [InlineData("EXEC GOTO_HANDLER;")]
    public void A_line_that_merely_mentions_go_is_not_a_separator(string line)
    {
        var script = $"CREATE TABLE [t1] ([Id] int NOT NULL);\n{line}\nCREATE TABLE [t2] ([Id] int NOT NULL);";

        var batches = SqlScriptSplitting.SplitSqlServerBatches(script);

        Assert.Single(batches);
        Assert.Contains("t1", batches[0], StringComparison.Ordinal);
        Assert.Contains("t2", batches[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Mysql_statements_split_on_a_plain_semicolon_when_no_delimiter_directive_appears()
    {
        const string script = """
            CREATE TABLE IF NOT EXISTS `t1` (`Id` int NOT NULL);
            INSERT INTO `t1` (`Id`) VALUES (1);
            """;

        var statements = SqlScriptSplitting.SplitMySqlStatements(script);

        Assert.Equal(2, statements.Count);
        Assert.StartsWith("CREATE TABLE", statements[0], StringComparison.Ordinal);
        Assert.StartsWith("INSERT INTO", statements[1], StringComparison.Ordinal);
    }

    /// <summary>
    /// The exact case the issue names: a trigger body carries its own <c>;</c> characters, so applying it
    /// through a client that only knows the default <c>;</c> terminator would fail or silently truncate.
    /// </summary>
    [Fact]
    public void A_delimiter_directive_lets_a_trigger_body_carry_its_own_semicolons()
    {
        const string script = """
            CREATE TABLE IF NOT EXISTS `t1` (`Id` int NOT NULL, `Touched` datetime NULL);
            DELIMITER $$
            CREATE TRIGGER `t1_touch` BEFORE UPDATE ON `t1`
            FOR EACH ROW
            BEGIN
                SET NEW.Touched = NOW();
                SET @dummy = 1;
            END$$
            DELIMITER ;
            INSERT INTO `t1` (`Id`) VALUES (1);
            """;

        var statements = SqlScriptSplitting.SplitMySqlStatements(script);

        Assert.Equal(3, statements.Count);
        Assert.StartsWith("CREATE TABLE", statements[0], StringComparison.Ordinal);
        Assert.Contains("CREATE TRIGGER", statements[1], StringComparison.Ordinal);
        // The trigger body's own semicolons are inside the one statement, not treated as extra statement boundaries.
        Assert.Contains("SET NEW.Touched = NOW();", statements[1], StringComparison.Ordinal);
        Assert.Contains("SET @dummy = 1;", statements[1], StringComparison.Ordinal);
        Assert.StartsWith("INSERT INTO", statements[2], StringComparison.Ordinal);
        // Neither DELIMITER line is ever sent to the server.
        Assert.DoesNotContain(statements, statement => statement.Contains("DELIMITER", StringComparison.Ordinal));
    }

    [Fact]
    public void The_default_terminator_is_restored_after_delimiter_changes_it_back()
    {
        const string script = """
            DELIMITER $$
            CREATE PROCEDURE `p1`()
            BEGIN
                SELECT 1;
            END$$
            DELIMITER ;
            CREATE TABLE IF NOT EXISTS `t1` (`Id` int NOT NULL);
            INSERT INTO `t1` (`Id`) VALUES (1);
            """;

        var statements = SqlScriptSplitting.SplitMySqlStatements(script);

        Assert.Equal(3, statements.Count);
        Assert.Contains("CREATE PROCEDURE", statements[0], StringComparison.Ordinal);
        Assert.StartsWith("CREATE TABLE", statements[1], StringComparison.Ordinal);
        Assert.StartsWith("INSERT INTO", statements[2], StringComparison.Ordinal);
    }

    [Fact]
    public void A_script_with_only_delimiter_directives_and_no_body_yields_no_statements()
    {
        var statements = SqlScriptSplitting.SplitMySqlStatements("DELIMITER $$\nDELIMITER ;\n");

        Assert.Empty(statements);
    }
}
