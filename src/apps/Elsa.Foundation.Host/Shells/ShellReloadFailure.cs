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
/// host shares with every package it loads. The host carries and shares <c>Elsa.Persistence.EntityFramework</c> for its
/// cluster membership, so a module usually binds that copy, but this code names no EF type: a module built against a
/// computed-version copy the host does not declare can still load a private one, in a load context of its own, whose
/// exception this host could not name. A module whose exception was built against a private copy of <c>Elsa.Persistence.Schema</c> too implements an interface
/// of the same full name that is not this host's type, so the refusal is then read from it by name and reflection.
/// </para>
/// <para>
/// A refusal's command is written without knowing where the host is (<see cref="IEfModuleRefusal.HostPlaceholder"/>); the
/// host's directory is substituted here, in the command and in the message that quotes it.
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
/// <param name="Exception">The failure as CShells reported it.</param>
internal sealed record ShellReloadFailure(string Shell, string Error, ShellActivationRefusal? Refusal, Exception Exception)
{
    /// <summary>
    /// The host's own directory, which is what <c>--host</c> needs: the persistence tool loads the package set the running host
    /// last reconciled from that directory's <c>.nuplane</c> state and its build output beside it. That is where the host's
    /// binaries are, not its content root, which an operator may point anywhere and which holds neither.
    /// </summary>
    public static string HostDirectory => Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);

    private const string SharedInterfaceName = "Elsa.Persistence.Schema.IEfModuleRefusal";

    /// <summary>The failures among <paramref name="results"/>, in the order CShells reported them.</summary>
    /// <param name="results">What the reload reported.</param>
    /// <param name="hostDirectory">The directory <c>dotnet elsa persistence --host</c> takes: the host's own, holding its build output and its <c>.nuplane</c> state.</param>
    public static IReadOnlyList<ShellReloadFailure> From(IEnumerable<ReloadResult> results, string hostDirectory) =>
        [.. results.Where(result => result.Error is not null).Select(result => Describe(result.Name, result.Error!, hostDirectory))];

    /// <summary>One shell's failure, from the exception that stopped it activating, read the way <see cref="From"/> reads a reload result.</summary>
    internal static ShellReloadFailure Describe(string shell, Exception error, string hostDirectory)
    {
        var host = IEfModuleRefusal.HostPlaceholder;
        var directory = $"\"{hostDirectory}\"";
        return Find(error) is { } found
            ? new(
                shell,
                found.Exception.Message.Replace(host, directory, StringComparison.Ordinal),
                found.Reading with { Command = found.Reading.Command?.Replace(host, directory, StringComparison.Ordinal) },
                error)
            : new(shell, $"{error.GetType().Name}: the shell did not activate; see the host log for its message.", null, error);
    }

    /// <summary>The first refusal in <paramref name="error"/> or what it wraps, however deep a shell's initializer nested it.</summary>
    private static (Exception Exception, ShellActivationRefusal Reading)? Find(Exception? error) => error switch
    {
        null => null,
        AggregateException aggregate => aggregate.InnerExceptions.Select(Find).FirstOrDefault(found => found is not null),
        _ => Read(error) ?? Find(error.InnerException)
    };

    private static (Exception, ShellActivationRefusal)? Read(Exception error)
    {
        if (error is IEfModuleRefusal refusal)
            return (error, new(refusal.Module, refusal.Code, refusal.PendingMigrations, refusal.Command));

        // The module's own copy of the shared assembly: an interface of the same full name, which this one is not.
        var contract = Array.Find(error.GetType().GetInterfaces(), type => type.FullName == SharedInterfaceName);
        if (contract is null)
            return null;

        try
        {
            return (error, new(
                (string)contract.GetProperty(nameof(IEfModuleRefusal.Module))!.GetValue(error)!,
                (string)contract.GetProperty(nameof(IEfModuleRefusal.Code))!.GetValue(error)!,
                [.. (IEnumerable<string>)contract.GetProperty(nameof(IEfModuleRefusal.PendingMigrations))!.GetValue(error)!],
                (string?)contract.GetProperty(nameof(IEfModuleRefusal.Command))!.GetValue(error)));
        }
        catch (Exception exception) when (exception is NullReferenceException or InvalidCastException or System.Reflection.TargetException or System.Reflection.TargetInvocationException)
        {
            // Something of that name that is not this contract: not a refusal this host can read.
            return null;
        }
    }
}
