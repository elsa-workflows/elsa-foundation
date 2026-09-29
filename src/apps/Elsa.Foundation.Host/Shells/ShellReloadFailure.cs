using CShells.Lifecycle;
using Elsa.Persistence.Schema;

namespace Elsa.Foundation.Host.Shells;

/// <summary>
/// One shell a reload could not activate, as an operator reads it. CShells catches whatever a blueprint, provider build or
/// initializer threw into <see cref="ReloadResult.Error"/> and keeps the previous generation active, so nothing but this
/// reading of the result says the reload did not happen.
/// </summary>
/// <remarks>
/// <para>
/// An EF module's refusal is recognised by <see cref="IEfModuleRefusal"/>, from <c>Elsa.Persistence.Schema</c>, which this
/// host shares with every package it loads. The exception itself is a type of the module's own copy of
/// <c>Elsa.Persistence.EntityFramework</c>, in a load context of its own, which this host neither references nor could name.
/// </para>
/// <para>
/// Only a recognised refusal's message is carried: it is built from module, migration and version names and never from
/// configuration (spec 171, FR-061). Any other failure is reported by its type, and its message is left to the host's log,
/// where the reload observer writes the whole exception.
/// </para>
/// </remarks>
/// <param name="Shell">The shell whose reload failed.</param>
/// <param name="Error">What went wrong, in words an operator can act on.</param>
/// <param name="Refusal">The EF module's refusal behind the failure, when there is one.</param>
internal sealed record ShellReloadFailure(string Shell, string Error, IEfModuleRefusal? Refusal, Exception Exception)
{
    /// <summary>The failures among <paramref name="results"/>, in the order CShells reported them.</summary>
    public static IReadOnlyList<ShellReloadFailure> From(IEnumerable<ReloadResult> results) =>
        [.. results.Where(result => result.Error is not null).Select(result => Describe(result.Name, result.Error!))];

    private static ShellReloadFailure Describe(string shell, Exception error)
    {
        var refusal = Find(error);
        return new(
            shell,
            refusal is Exception refused
                ? refused.Message
                : $"{error.GetType().Name}: the shell did not activate; see the host log for its message.",
            refusal,
            error);
    }

    /// <summary>The first refusal in <paramref name="error"/> or what it wraps, however deep a shell's initializer nested it.</summary>
    private static IEfModuleRefusal? Find(Exception? error) => error switch
    {
        null => null,
        IEfModuleRefusal refusal => refusal,
        AggregateException aggregate => aggregate.InnerExceptions.Select(Find).FirstOrDefault(refusal => refusal is not null),
        _ => Find(error.InnerException)
    };
}
