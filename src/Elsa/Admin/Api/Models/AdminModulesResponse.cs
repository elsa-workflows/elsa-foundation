using Elsa.Admin.Core.Models;

namespace Elsa.Admin.Api.Models;

public sealed record AdminModulesResponse(
    string HostVersion,
    string SdkVersion,
    IReadOnlyCollection<AdminModuleManifest> Modules,
    IReadOnlyCollection<AdminModuleDiagnostic> Diagnostics);
