using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Coalescing;

namespace Elsa.Workflows.Runtime.Api.Requests;

/// <summary>Reads the host's configured default persistence mode.</summary>
public sealed record GetRuntimePersistenceMode : IRequest<RuntimePersistenceModeProjection>;
