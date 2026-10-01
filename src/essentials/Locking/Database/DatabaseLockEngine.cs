namespace Elsa.Locking.Database;

/// <summary>The database engines that can hold distributed locks (#2192).</summary>
public enum DatabaseLockEngine
{
    PostgreSql,
    SqlServer,
    MySql
}
