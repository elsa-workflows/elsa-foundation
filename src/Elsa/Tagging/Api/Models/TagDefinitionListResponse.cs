using Elsa.Tagging.Core.Models;

namespace Elsa.Tagging.Api.Models;

/// <summary>Envelope reserved for cursor and facet metadata without changing the catalog list contract.</summary>
public sealed record TagDefinitionListResponse(IReadOnlyList<TagDefinition> Items);
