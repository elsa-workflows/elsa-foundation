namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// Explicitly enrolls a shell feature in named EF persistence resource resolution.
/// Its module and stable feature identity remain owned by the existing metadata declarations.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class EfPersistenceResourceParticipantAttribute : Attribute
{
}
