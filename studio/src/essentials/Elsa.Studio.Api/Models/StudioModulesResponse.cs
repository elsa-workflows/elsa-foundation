using Elsa.Studio.Core.Models;

namespace Elsa.Studio.Api.Models;

public sealed record StudioModulesResponse(
    string HostVersion,
    string SdkVersion,
    IReadOnlyCollection<StudioModuleManifest> Modules,
    IReadOnlyCollection<StudioModuleDiagnostic> Diagnostics);

