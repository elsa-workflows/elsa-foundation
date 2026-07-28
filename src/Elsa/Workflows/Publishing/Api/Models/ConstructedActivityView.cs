using System.Text.Json;

namespace Elsa.Workflows.Publishing.Api.Models;

/// <summary>An authored activity-version contract projected without runtime activation.</summary>
public sealed record ConstructedActivityView(
    string ActivityVersionId,
    string DescriptorType,
    JsonElement DescriptorPayload,
    IReadOnlyList<ArgumentView> Inputs,
    IReadOnlyList<ArgumentView> Outputs);

public sealed record ArgumentView(string ReferenceKey, string Name, string TypeName);
