using System.IO;
using Xunit;

namespace Elsa.Cli.Tests;

public sealed class ProcessIdentityTests
{
    [Fact]
    public void Linux_stat_parser_accepts_spaces_and_parentheses_in_comm()
    {
        var snapshot = ProcessIdentityReader.ParseLinuxStat(321, Stat(321, "S", "987654"));

        Assert.Equal(321, snapshot.Identity.Pid);
        Assert.Equal(987654, snapshot.Identity.StartToken);
        Assert.Equal('S', snapshot.State);
    }

    [Theory]
    [InlineData("321 (composer with spaces) S 1 2")]
    [InlineData("321 composer) S 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20")]
    public void Linux_stat_parser_rejects_malformed_records(string stat)
    {
        Assert.Throws<InvalidDataException>(() => ProcessIdentityReader.ParseLinuxStat(321, stat));
    }

    [Fact]
    public void Linux_stat_parser_rejects_a_mismatched_pid()
    {
        Assert.Throws<InvalidDataException>(() => ProcessIdentityReader.ParseLinuxStat(321, Stat(322, "S", "987654")));
    }

    [Theory]
    [InlineData("?", "987654")]
    [InlineData("S", "not-a-number")]
    [InlineData("S", "0")]
    public void Linux_stat_parser_rejects_unknown_state_or_start_token(string state, string startToken)
    {
        Assert.Throws<InvalidDataException>(() => ProcessIdentityReader.ParseLinuxStat(321, Stat(321, state, startToken)));
    }

    private static string Stat(int pid, string state, string startToken)
    {
        var fields = new string[20];
        Array.Fill(fields, "0");
        fields[0] = state;
        fields[19] = startToken;
        return $"{pid} (composer with spaces and ) parentheses) {string.Join(' ', fields)}";
    }
}
