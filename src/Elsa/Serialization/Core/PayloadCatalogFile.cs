using System.Text.Json;

namespace Elsa.Serialization.Core;

/// <summary>
/// Reads a JSON array of catalog models from disk through <see cref="IPayloadSerializer"/>.
/// Domain readers keep their own exception types and logging; this helper has no logger.
/// </summary>
public static class PayloadCatalogFile
{
    public static TModel[] ReadArray<TModel>(
        string path,
        IPayloadSerializer serializer,
        Func<string, string, Exception?, Exception> exceptionFactory)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        ArgumentNullException.ThrowIfNull(exceptionFactory);

        if (string.IsNullOrWhiteSpace(path))
            throw exceptionFactory(path ?? string.Empty, "no file path was configured.", null);

        if (!File.Exists(path))
            throw exceptionFactory(path, "the file does not exist.", null);

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw exceptionFactory(path, "the file could not be read.", exception);
        }

        TModel[]? models;
        try
        {
            models = serializer.Deserialize<TModel[]>(json);
        }
        catch (JsonException exception)
        {
            throw exceptionFactory(path, "the file is not a valid JSON array of reconciliation models.", exception);
        }

        if (models is null)
            throw exceptionFactory(path, "the file deserialized to null.", null);

        return models;
    }
}
