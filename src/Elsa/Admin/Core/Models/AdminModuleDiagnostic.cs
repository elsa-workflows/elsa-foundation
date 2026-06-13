namespace Elsa.Admin.Core.Models;

public sealed record AdminModuleDiagnostic(
    string ModuleId,
    string Status,
    string Reason);
