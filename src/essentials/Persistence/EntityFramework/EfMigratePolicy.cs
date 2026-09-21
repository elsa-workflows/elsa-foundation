namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// Dual apply modes introduced by the historical ADR 0072 pilot and carried forward by accepted
/// ADR 0073: in-process auto-migrate after feature enablement, and fail-closed validate so CI/CD
/// can start with auto-migrate off.
/// </summary>
public enum EfMigratePolicy
{
    AutoMigrate = 0,
    Validate = 1
}
