namespace Elsa.Activities.Runtime.Core.Exceptions;

/// <summary>
/// Thrown at construction time when a CLR activity descriptor's stable type alias does not resolve to a
/// registered CLR type — typically because the activity's assembly is not loaded into this host (so the
/// startup registration pass never registered its alias). A domain failure (Elsa §E2.6), not a system
/// fault: cataloguing and reading the row are unaffected; only construction of an instance fails. The
/// alias is preserved verbatim so the failure names the exact unresolved identity.
/// </summary>
public sealed class UnknownActivityTypeException(string typeAlias)
    : Exception($"No CLR activity type is registered for the alias '{typeAlias}'. The activity's assembly may not be loaded in this host.")
{
    public string TypeAlias { get; } = typeAlias;
}
