using Microsoft.EntityFrameworkCore;
using System.Reflection;

namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// The one place a module's declared <see cref="EfModuleAttribute.PostMigration"/> types become
/// <see cref="IEfPostMigrationAction"/> instances, and the one place a set of them is audited (ADR 0076 D8).
/// Both the running host's <see cref="EfModuleMigrator{TContext}"/> and the out-of-process
/// <c>Tooling.EfToolingHost</c> go through here, so a declaration that cannot be honoured is refused the same
/// way in both, and neither can ever run an action the other would not.
/// </summary>
public static class EfPostMigrationActions
{
    /// <summary>The command that runs a required action, and the only thing that ever calls <see cref="IEfPostMigrationAction.RunAsync"/>.</summary>
    public const string Command = "dotnet elsa persistence post-migrate";

    /// <summary>The exact invocation that clears <paramref name="module"/>'s outstanding actions on <paramref name="provider"/>.</summary>
    public static string CommandFor(string module, string provider) =>
        $"{Command} --modules {module} --provider {provider}";

    /// <summary>
    /// Instantiates <paramref name="declared"/> for <paramref name="module"/>, refusing rather than skipping a
    /// declaration this build cannot honour: a type that is not an <see cref="IEfPostMigrationAction"/>, one
    /// that cannot be constructed without arguments, one that describes itself incompletely, or two that share
    /// an <see cref="IEfPostMigrationAction.Id"/>. A skipped declaration would leave an obligation unaudited
    /// while every command still reported success, which is the one failure this seam exists to prevent.
    /// </summary>
    public static IReadOnlyList<IEfPostMigrationAction> Create(string module, IReadOnlyList<Type> declared)
    {
        if (TryCreate(module, declared, out var actions, out var faults))
            return actions;

        throw new InvalidOperationException(
            $"Module '{module}' declares a post-migration action this build cannot use: {string.Join(" ", faults)}");
    }

    /// <summary>
    /// <see cref="Create"/> without the throw, for the callers that must name every offending declaration
    /// across a whole selection rather than stopping at the first module that has one.
    /// </summary>
    public static bool TryCreate(
        string module,
        IReadOnlyList<Type> declared,
        out IReadOnlyList<IEfPostMigrationAction> actions,
        out IReadOnlyList<string> faults)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(module);
        ArgumentNullException.ThrowIfNull(declared);

        var created = new List<IEfPostMigrationAction>(declared.Count);
        var problems = new List<string>();
        foreach (var type in declared)
        {
            if (Instantiate(module, type, problems) is { } action)
                created.Add(action);
        }

        problems.AddRange(created
            .GroupBy(action => action.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => $"'{module}' declares {group.Count()} post-migration actions with id '{group.Key}'.")
            .Order(StringComparer.Ordinal));

        actions = created;
        faults = problems;
        return problems.Count == 0;
    }

    /// <summary>
    /// Every action in <paramref name="actions"/> that reports itself required against
    /// <paramref name="context"/>, in declaration order. Nothing here runs an action, and nothing swallows an
    /// audit's own failure: a database that cannot be read propagates rather than being reported as "nothing
    /// required", which would look exactly like a healthy database.
    /// </summary>
    public static async Task<IReadOnlyList<IEfPostMigrationAction>> RequiredAsync(
        DbContext context,
        IReadOnlyList<IEfPostMigrationAction> actions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(actions);

        var required = new List<IEfPostMigrationAction>();
        foreach (var action in actions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await action.AuditAsync(context, cancellationToken))
                required.Add(action);
        }

        return required;
    }

    /// <summary>
    /// Audits <paramref name="actions"/> and fails closed — naming <see cref="Command"/> — when any reports
    /// itself required (FR-056). The audit is what runs; the action never is.
    /// </summary>
    public static async Task EnsureNotRequiredAsync(
        DbContext context,
        string module,
        string provider,
        IReadOnlyList<IEfPostMigrationAction> actions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actions);
        if (actions.Count == 0)
            return;

        var required = await RequiredAsync(context, actions, cancellationToken);
        if (required.Count == 0)
            return;

        throw new EfPostMigrationRequiredException(
            module,
            [.. required.Select(action => action.Id)],
            CommandFor(module, provider));
    }

    private static IEfPostMigrationAction? Instantiate(string module, Type? type, List<string> problems)
    {
        if (type is null)
        {
            problems.Add($"'{module}' declares a null post-migration action type.");
            return null;
        }

        if (!typeof(IEfPostMigrationAction).IsAssignableFrom(type))
        {
            problems.Add($"'{module}' declares post-migration action '{type.Name}', which does not implement {nameof(IEfPostMigrationAction)}.");
            return null;
        }

        if (type.IsAbstract || type.IsInterface || type.ContainsGenericParameters)
        {
            problems.Add($"'{module}' declares post-migration action '{type.Name}', which is not a constructible type.");
            return null;
        }

        if (type.GetConstructor(BindingFlags.Public | BindingFlags.Instance, Type.EmptyTypes) is null)
        {
            problems.Add(
                $"'{module}' declares post-migration action '{type.Name}', which has no public parameterless constructor. " +
                "A post-migration action takes only the DbContext and uses no dependency injection.");
            return null;
        }

        IEfPostMigrationAction action;
        try
        {
            action = (IEfPostMigrationAction)Activator.CreateInstance(type)!;
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            var reason = (failure as TargetInvocationException)?.InnerException ?? failure;
            problems.Add($"'{module}' declares post-migration action '{type.Name}', which could not be constructed: {reason.Message}");
            return null;
        }

        var blank = new[]
            {
                (nameof(IEfPostMigrationAction.Id), action.Id),
                (nameof(IEfPostMigrationAction.Kind), action.Kind),
                (nameof(IEfPostMigrationAction.RequiredWhen), action.RequiredWhen),
                (nameof(IEfPostMigrationAction.Audit), action.Audit)
            }
            .Where(member => string.IsNullOrWhiteSpace(member.Item2))
            .Select(member => member.Item1)
            .ToArray();
        if (blank.Length > 0)
        {
            problems.Add($"'{module}' declares post-migration action '{type.Name}', which leaves {string.Join(", ", blank)} blank.");
            return null;
        }

        return action;
    }
}
