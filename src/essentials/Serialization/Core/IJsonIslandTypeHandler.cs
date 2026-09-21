namespace Elsa.Serialization.Core;

/// <summary>
/// Handles payload values that must be serialized as an embedded JSON string
/// because the System.Text.Json payload serializer cannot materialize them directly.
/// </summary>
public interface IJsonIslandTypeHandler
{
    bool CanHandle(Type type);

    object Read(string? json);

    string Write(object value);
}
