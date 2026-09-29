using System.Reflection;
using Elsa.Cluster.Core.Options;
using Xunit;

namespace Elsa.Cli.Tests;

/// <summary>
/// The tool reads the membership skew allowance from a host's appsettings by key. It cannot reference the options that
/// host binds it with, so this project, which can see both, holds the two to each other.
/// </summary>
public sealed class HostMembershipSettingsTests
{
    [Fact]
    public void The_key_the_tool_reads_is_the_one_the_hosts_membership_options_bind()
    {
        // Elsa.Cli keeps its internals to itself, and must stay free of a Cluster.Core reference, so the constant is read by reflection.
        var settings = Assembly.Load("Elsa.Cli").GetType("Elsa.Cli.HostMembershipSettings", throwOnError: true)!;
        var key = settings.GetField("SkewAllowanceKey", BindingFlags.Public | BindingFlags.Static)!.GetRawConstantValue();

        Assert.Equal($"{ClusterMembershipOptions.SectionName}:{nameof(ClusterMembershipOptions.SkewAllowance)}", key);
    }
}
