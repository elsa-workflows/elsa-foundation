using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Api.Requests;

public sealed record GetRuntimeDiagnosticsSettings(string? Scope = null) : IRequest<RuntimeDiagnosticsSettingsView>;
