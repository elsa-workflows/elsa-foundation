using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Testing;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests;

public sealed class AspNetCoreIdentityV2ProcessProtocolTests
{
    [Fact]
    public void Process_protocol_never_renders_provider_state_or_identity_payloads()
    {
        const string connectionString = "Server=secret-host;Password=secret-password";
        var user = new IdentityProcessProbeUser(
            "secret-tenant",
            "secret-user",
            "secret-name",
            "SECRET-NAME",
            "secret@example.test",
            "SECRET@EXAMPLE.TEST");
        var command = new IdentityProcessProbeCommand(
            IdentityProcessProbeProtocol.CurrentVersion,
            new string('a', 64),
            "sqlserver",
            "identity_probe",
            IdentityProcessProbeOperation.CreateUser,
            user,
            new IdentityProcessProbeState(connectionString));

        Assert.DoesNotContain(connectionString, command.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(user.UserId, command.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(user.NormalizedUserName, user.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(connectionString, command.State.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Process_runner_has_a_bounded_default_and_honors_caller_cancellation()
    {
        Assert.Equal(TimeSpan.FromSeconds(60), IdentityProcessProbeRunner.DefaultTimeout);
        var runner = new IdentityProcessProbeRunner();
        var user = new IdentityProcessProbeUser(
            "tenant-cancelled",
            "user-cancelled",
            "cancelled",
            "CANCELLED",
            "cancelled@example.test",
            "CANCELLED@EXAMPLE.TEST");
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(
            "sqlite",
            "identity_cancelled",
            IdentityProcessProbeOperation.CreateUser,
            user,
            new IdentityProcessProbeState("Data Source=:memory:"),
            cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task Process_runner_rejects_a_nonpositive_timeout_before_launching_a_helper()
    {
        var runner = new IdentityProcessProbeRunner();
        var user = new IdentityProcessProbeUser(
            "tenant-timeout",
            "user-timeout",
            "timeout",
            "TIMEOUT",
            "timeout@example.test",
            "TIMEOUT@EXAMPLE.TEST");

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => runner.RunAsync(
            "sqlite",
            "identity_timeout",
            IdentityProcessProbeOperation.CreateUser,
            user,
            new IdentityProcessProbeState("Data Source=:memory:"),
            timeout: TimeSpan.Zero));
    }
}
