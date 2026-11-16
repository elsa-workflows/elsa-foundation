using Elsa.Activities.Design.Core.Models;

namespace Elsa.Activities.Design.Core.Contracts;

/// <summary>
/// Optional provider/compiler/discoverer contribution of intentionally public, static diagnostic guidance.
/// Registries snapshot these declarations at registration. Never derive them from authored payloads,
/// credentials, exceptions or runtime diagnostic content.
/// </summary>
public interface IActivityProviderDiagnosticCatalog
{
    IReadOnlyList<ActivityProviderDiagnosticDescriptor> DiagnosticDescriptors { get; }
}
