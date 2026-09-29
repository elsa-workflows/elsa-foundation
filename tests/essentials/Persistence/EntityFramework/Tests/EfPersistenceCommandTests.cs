using Xunit;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.Schema;

namespace Elsa.Persistence.EntityFramework.Tests;

/// <summary>The one place the commands an operator is told to run are written: each names <c>--host</c>, which the tool requires.</summary>
public sealed class EfPersistenceCommandTests
{
    [Fact]
    public void Apply_names_the_host_it_is_given_in_quotes()
    {
        Assert.Equal(
            "dotnet elsa persistence apply --host \"/srv/elsa host\" --modules Orders --provider Sqlite --connection-env ELSA_EF_CONNECTION",
            EfPersistenceCommand.Apply("/srv/elsa host", "Orders", "Sqlite"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Apply_prints_a_quoted_placeholder_that_will_not_break_a_shell_when_no_host_is_known(string? host)
    {
        var command = EfPersistenceCommand.Apply(host, "Orders", "Sqlite");

        Assert.Contains($"--host {IEfModuleRefusal.HostPlaceholder} ", command, StringComparison.Ordinal);
        Assert.Contains("--host \"<host directory>\" ", command, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_names_the_host_too()
    {
        Assert.Equal(
            "dotnet elsa persistence script --host \"<host directory>\" --modules Orders --provider PostgreSql --output <directory>",
            EfPersistenceCommand.Script(null, "Orders", "PostgreSql"));
    }

    [Fact]
    public void An_exception_built_from_a_message_alone_is_still_a_refusal_with_empty_details()
    {
        IEfModuleRefusal refusal = new EfPendingMigrationsException("pending");

        Assert.Equal(("", IEfModuleRefusal.PendingMigrationsCode, null), (refusal.Module, refusal.Code, refusal.Command));
        Assert.Empty(refusal.PendingMigrations);
    }
}
