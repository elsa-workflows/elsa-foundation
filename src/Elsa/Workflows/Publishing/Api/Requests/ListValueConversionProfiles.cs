using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Models;

namespace Elsa.Workflows.Publishing.Api.Requests;

/// <summary>Lists the registered conversion profiles a visual authoring picker can offer.</summary>
public sealed record ListValueConversionProfiles : IRequest<ValueConversionProfilesResponse>;
