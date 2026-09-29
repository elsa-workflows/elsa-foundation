using Elsa.Persistence.Schema;

namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// The <c>dotnet elsa persistence</c> invocations an operator is told to run, written in one place beside the migrator that
/// refuses so that every message that names one names one that runs: <c>--host</c> is required by the tool, so it is always
/// there.
/// </summary>
/// <remarks>The connection is named by the variable it is read from, never its value (spec 171, D7).</remarks>
public static class EfPersistenceCommand
{
    /// <summary>The environment variable the commands read the connection from.</summary>
    public const string ConnectionVariable = "ELSA_EF_CONNECTION";

    /// <summary>
    /// The exact invocation that applies <paramref name="module"/>'s pending migrations on <paramref name="provider"/>.
    /// </summary>
    /// <param name="host">The published output directory of the host, when the caller knows it; otherwise a quoted placeholder that does not break a shell is printed for the operator to fill in.</param>
    /// <param name="module">The EF module, as its <c>[EfModule]</c> names it.</param>
    /// <param name="provider">The provider name the tool's <c>--provider</c> takes.</param>
    public static string Apply(string? host, string module, string provider) =>
        $"dotnet elsa persistence apply {Host(host)} --modules {module} --provider {provider} --connection-env {ConnectionVariable}";

    /// <summary>The exact invocation that writes <paramref name="module"/>'s idempotent migration script for <paramref name="provider"/>.</summary>
    public static string Script(string? host, string module, string provider) =>
        $"dotnet elsa persistence script {Host(host)} --modules {module} --provider {provider} --output <directory>";

    private static string Host(string? host) =>
        $"--host {(string.IsNullOrWhiteSpace(host) ? IEfModuleRefusal.HostPlaceholder : $"\"{host}\"")}";
}
