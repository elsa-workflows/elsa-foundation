namespace Elsa.Studio.Core.Models;

public sealed record StudioModuleDiagnostic(
    string ModuleId,
    string Status,
    string Reason);

