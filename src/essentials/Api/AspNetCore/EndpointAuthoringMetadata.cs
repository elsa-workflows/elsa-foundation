namespace Elsa.Api.AspNetCore;

/// <summary>Identifies the endpoint authoring model used to publish an endpoint.</summary>
public sealed record EndpointAuthoringMetadata
{
    public EndpointAuthoringMetadata(string model)
    {
        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("An endpoint authoring model is required.", nameof(model));

        Model = model.Trim();
    }

    public string Model { get; }
}

public static class EndpointAuthoringModels
{
    public const string MinimalApi = "Minimal API";
    public const string Mvc = "MVC";
    public const string FastEndpoints = "FastEndpoints";
}
