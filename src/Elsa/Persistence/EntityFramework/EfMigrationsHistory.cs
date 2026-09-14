namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// Per-module EF migrations history table names. The default
/// <c>__EFMigrationsHistory</c> collides when two modules share one database.
/// </summary>
public static class EfMigrationsHistory
{
    public const string TablePrefix = "__EFMigrationsHistory_";

    public static string TableName(string module)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(module);
        if (module.IndexOfAny(['/', '\\', '.', ' ']) >= 0)
        {
            throw new ArgumentException(
                "A migrations history module name must be a single identifier without path or whitespace characters.",
                nameof(module));
        }

        return TablePrefix + module;
    }
}
