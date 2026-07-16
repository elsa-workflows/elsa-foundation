namespace Elsa.Activities.Runtime.Core.Exceptions;

/// <summary>
/// Thrown at startup when any second constructor registration claims the same Runtime consumer/schema pair.
/// </summary>
public sealed class DuplicateActivityConstructorException(string consumerKey, string schemaVersion, Type existing, Type attempted)
    : Exception($"Activity constructor for Runtime consumer '{consumerKey}' and schema '{schemaVersion}' is already registered with '{existing.FullName}'; refusing to overwrite with '{attempted.FullName}'.")
{
    public string ConsumerKey { get; } = consumerKey;
    public string SchemaVersion { get; } = schemaVersion;
}
