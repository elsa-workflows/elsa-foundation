namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// Dual apply modes required by ADR 0072: in-process auto-migrate after feature enablement,
/// and fail-closed validate so CI/CD can start with auto-migrate off.
/// </summary>
public enum EfMigratePolicy
{
    AutoMigrate = 0,
    Validate = 1
}
